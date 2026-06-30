using System.Runtime.InteropServices;
using System.Security.Cryptography;
using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Application.Backup;

/// <summary>
/// Orchestrates creating a complete backup (003 US1, FR-001..006): take the maintenance slot, build the
/// self-describing manifest (migration tip + per-table columns/row counts + CV hashes), snapshot the DB
/// and embed the CV files into one archive on a server-side temp file, and hand back the artifact for the
/// endpoint to stream. Non-destructive — it only reads live data and runs concurrently with scans (MVCC).
/// </summary>
public sealed class BackupService(
    IDatabaseSnapshotStore snapshotStore,
    IBackupArchiveStore archiveStore,
    IMigrationInspector migrations,
    ICvFileStore fileStore,
    MaintenanceGate gate,
    TimeProvider time,
    ILogger<BackupService> logger)
{
    public async Task<Result<BackupArtifact>> CreateAsync(CancellationToken ct = default)
    {
        if (!gate.TryBeginBackup())
        {
            return BackupErrors.BusyMaintenance;
        }

        try
        {
            return await BuildAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Backup failed before streaming.");
            return BackupErrors.BackupFailed;
        }
        finally
        {
            gate.EndBackup();
        }
    }

    /// <summary>
    /// Build a complete backup to a server-side temp file <b>without</b> taking the maintenance slot —
    /// used by the restore to write its automatic safety pre-backup while it already holds the gate.
    /// </summary>
    public async Task<BackupArtifact> BuildAsync(CancellationToken ct = default)
    {
        var createdAt = time.GetUtcNow();
        var tip = await migrations.AppliedTipAsync(ct);

        var (cvEntries, cvSources) = await DescribeCvFilesAsync(ct);
        var snapshot = await snapshotStore.SnapshotAsync(BackupTables.InsertOrder, ct);

        var manifest = new BackupManifest(
            BackupManifest.CurrentFormatVersion,
            createdAt,
            RuntimeInformation.FrameworkDescription,
            tip,
            [.. snapshot.Select(s => new BackupTable(s.Name, s.Columns, s.RowCount))],
            cvEntries,
            cvEntries.Count);

        var tempPath = await archiveStore.WriteAsync(manifest, snapshot, cvSources, ct);
        var fileName = $"jobs-backup-{createdAt.UtcDateTime:yyyy-MM-dd-HHmmss}.zip";
        return new BackupArtifact(tempPath, fileName, manifest);
    }

    /// <summary>
    /// Verify an uploaded backup without restoring (003 US3, FR-005): read + validate the archive, read
    /// per-table counts from the manifest's <c>rowCount</c> (no payload parsing), and compute the
    /// cross-version compatibility. Strictly read-only — never touches live data.
    /// </summary>
    public async Task<Result<BackupInspection>> InspectAsync(Stream upload, CancellationToken ct = default)
    {
        var read = await archiveStore.ReadAsync(upload, ct);
        if (read.IsFailure)
        {
            return read.Error;
        }

        var archive = read.Value;
        var compatibility = BackupCompatibilityPolicy.Decide(archive.Manifest.MigrationTip, migrations.KnownMigrations());
        var tableCounts = archive.Manifest.Tables.ToDictionary(t => t.Name, t => t.RowCount);
        var totalCvBytes = archive.Manifest.CvFiles.Sum(c => c.Size);

        return new BackupInspection(
            Valid: true,
            archive.Manifest.CreatedAtUtc,
            archive.Manifest.AppProductVersion,
            archive.Manifest.MigrationTip,
            compatibility,
            tableCounts,
            archive.Manifest.CvFileCount,
            totalCvBytes,
            archive.Warnings);
    }

    private async Task<(IReadOnlyList<CvFileEntry> Entries, IReadOnlyList<CvFileSource> Sources)> DescribeCvFilesAsync(CancellationToken ct)
    {
        var entries = new List<CvFileEntry>();
        var sources = new List<CvFileSource>();

        foreach (var cv in fileStore.EnumerateAll())
        {
            await using var file = new FileStream(cv.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
            entries.Add(new CvFileEntry(cv.FileName, file.Length, sha256));
            sources.Add(new CvFileSource(cv.FileName, cv.AbsolutePath));
        }

        return (entries, sources);
    }
}

/// <summary>A built backup ready to stream: the server-side temp file, its download name, and the manifest.</summary>
public sealed record BackupArtifact(string TempPath, string FileName, BackupManifest Manifest);

/// <summary>The read-only summary of an uploaded backup (003 US3, contracts §2): validity, when it was
/// taken, the source version, compatibility, per-table counts, and CV totals — no live data involved.</summary>
public sealed record BackupInspection(
    bool Valid,
    DateTimeOffset CreatedAtUtc,
    string AppProductVersion,
    string MigrationTip,
    BackupCompatibility Compatibility,
    IReadOnlyDictionary<string, long> TableCounts,
    int CvFileCount,
    long TotalCvBytes,
    IReadOnlyList<string> Warnings);
