using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using JobOfferMatcher.Application.Backup;
using JobOfferMatcher.Domain.Common;
using Microsoft.Extensions.Configuration;

namespace JobOfferMatcher.Infrastructure.Backup;

/// <summary>
/// The <c>.zip</c> container for a backup (003 data-model §1/§2): <c>manifest.json</c> +
/// <c>db/&lt;table&gt;.copy</c> (raw COPY-text payloads) + <c>cv-data/&lt;name&gt;</c> (raw PDF bytes).
/// Writes to a server-side temp file first (complete-or-error, FR-006); reads + fully validates an
/// uploaded archive before any restore touches live data (FR-011). Unencrypted (FR-019).
/// </summary>
public sealed class ZipBackupArchiveStore(IConfiguration configuration) : IBackupArchiveStore
{
    private const string ManifestEntry = "manifest.json";
    private const string DbPrefix = "db/";
    private const string CvPrefix = "cv-data/";
    private const string CandidateCvTable = "candidate_cv";
    private const string CandidateCvFileNameColumn = "file_name";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // The archive is inspected by humans; keep non-ASCII text readable rather than \uXXXX-escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<string> WriteAsync(
        BackupManifest manifest,
        IReadOnlyList<TableSnapshot> tables,
        IReadOnlyList<CvFileSource> cvFiles,
        CancellationToken ct = default)
    {
        // Build the backup under the gitignored backups dir, so a transient temp never lands in the repo.
        var dir = BackupStorage.ResolveDirectory(configuration);
        var tempPath = Path.Combine(dir, $".tmp-backup-{Guid.NewGuid():N}.zip");

        try
        {
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
                await using (var manifestStream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, ct);
                }

                foreach (var table in tables)
                {
                    var entry = archive.CreateEntry($"{DbPrefix}{table.Name}.copy", CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(table.Data, ct);
                }

                foreach (var cv in cvFiles)
                {
                    var entry = archive.CreateEntry($"{CvPrefix}{cv.Name}", CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var source = new FileStream(cv.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await source.CopyToAsync(entryStream, ct);
                }
            }

            return tempPath;
        }
        catch
        {
            SafeDelete(tempPath);
            throw;
        }
    }

    public async Task<Result<BackupArchive>> ReadAsync(Stream archive, CancellationToken ct = default)
    {
        // Buffer to a seekable MemoryStream — an IFormFile stream may not support the random access
        // ZipArchive needs, and archives are single-user sized.
        using var buffer = new MemoryStream();
        await archive.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            return BackupErrors.Invalid("The uploaded file is not a readable zip archive.");
        }

        using (zip)
        {
            var manifestEntry = zip.GetEntry(ManifestEntry);
            if (manifestEntry is null)
            {
                return BackupErrors.Invalid("The archive has no manifest.json — it is not a backup.");
            }

            BackupManifest? manifest;
            try
            {
                await using var manifestStream = manifestEntry.Open();
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, JsonOptions, ct);
            }
            catch (JsonException)
            {
                return BackupErrors.Invalid("The manifest.json is corrupt or unreadable.");
            }

            if (manifest is null || manifest.Tables is null || manifest.CvFiles is null)
            {
                return BackupErrors.Invalid("The manifest.json is missing required fields.");
            }

            if (manifest.BackupFormatVersion > BackupManifest.CurrentFormatVersion)
            {
                return BackupErrors.Invalid(
                    $"This backup uses archive format v{manifest.BackupFormatVersion}, which this app does not understand.");
            }

            // Every manifest table must be a known table and have its db/*.copy payload present.
            var tables = new List<TableRestore>(manifest.Tables.Count);
            foreach (var table in manifest.Tables)
            {
                if (!BackupTables.InsertOrder.Contains(table.Name))
                {
                    return BackupErrors.Invalid($"The archive lists an unknown table '{table.Name}'.");
                }

                var dbEntry = zip.GetEntry($"{DbPrefix}{table.Name}.copy");
                if (dbEntry is null)
                {
                    return BackupErrors.Invalid($"The archive is missing db/{table.Name}.copy.");
                }

                tables.Add(new TableRestore(table.Name, table.Columns, await ReadAllBytesAsync(dbEntry, ct)));
            }

            // Every declared CV file must be present and hash-match.
            var cvFiles = new List<RestoredCvFile>(manifest.CvFiles.Count);
            var archivedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cv in manifest.CvFiles)
            {
                var cvEntry = zip.GetEntry($"{CvPrefix}{cv.Name}");
                if (cvEntry is null)
                {
                    return BackupErrors.Invalid($"The archive is missing CV file '{cv.Name}'.");
                }

                var bytes = await ReadAllBytesAsync(cvEntry, ct);
                if (!string.Equals(BackupHashing.Sha256Hex(bytes), cv.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return BackupErrors.Invalid($"CV file '{cv.Name}' failed its integrity (SHA-256) check.");
                }

                cvFiles.Add(new RestoredCvFile(cv.Name, bytes));
                archivedNames.Add(cv.Name);
            }

            // Referential check: every candidate_cv.file_name must have a matching archived file.
            var warnings = new List<string>();
            var referenced = ReferencedCvFileNames(tables);
            foreach (var name in referenced)
            {
                if (!archivedNames.Contains(name))
                {
                    return BackupErrors.Invalid($"A CV row references file '{name}', which is not in the archive.");
                }
            }

            // Orphan archived files (not referenced by any row) are non-fatal — still restored.
            foreach (var name in archivedNames)
            {
                if (!referenced.Contains(name))
                {
                    warnings.Add($"Archived CV file '{name}' is not referenced by any candidate_cv row.");
                }
            }

            // Hand restore the tables in dependency order regardless of manifest order.
            var ordered = tables
                .OrderBy(t => BackupTables.InsertOrder.ToList().IndexOf(t.Name))
                .ToList();

            return new BackupArchive(manifest, ordered, cvFiles, warnings);
        }
    }

    /// <summary>Pull the <c>file_name</c> values out of the <c>candidate_cv</c> COPY-text payload (skips nulls).</summary>
    private static HashSet<string> ReferencedCvFileNames(IReadOnlyList<TableRestore> tables)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var cvTable = tables.FirstOrDefault(t => t.Name == CandidateCvTable);
        if (cvTable is null)
        {
            return names;
        }

        var columnIndex = cvTable.Columns.ToList().IndexOf(CandidateCvFileNameColumn);
        if (columnIndex < 0)
        {
            return names;
        }

        var text = Encoding.UTF8.GetString(cvTable.Data);
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('\t');
            if (columnIndex >= fields.Length)
            {
                continue;
            }

            var value = fields[columnIndex];
            if (value == "\\N")
            {
                continue; // SQL NULL in COPY text format.
            }

            names.Add(UnescapeCopyText(value));
        }

        return names;
    }

    /// <summary>Minimal COPY-text unescape for the handful of sequences a value may carry (filenames rarely do).</summary>
    private static string UnescapeCopyText(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                sb.Append(value[i]);
                continue;
            }

            sb.Append(value[++i] switch
            {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                '\\' => '\\',
                var c => c,
            });
        }

        return sb.ToString();
    }

    private static async Task<byte[]> ReadAllBytesAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        await using var stream = entry.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        return memory.ToArray();
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp file.
        }
    }
}
