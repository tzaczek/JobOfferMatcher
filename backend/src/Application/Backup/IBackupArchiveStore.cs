using JobOfferMatcher.Domain.Common;

namespace JobOfferMatcher.Application.Backup;

/// <summary>
/// Port over the archive container (003 data-model §1/§2): writes the <c>.zip</c>
/// (<c>manifest.json</c> + <c>db/&lt;table&gt;.copy</c> + <c>cv-data/&lt;name&gt;</c>) to a server-side
/// temp file, and reads + fully validates an uploaded archive before any restore touches live data.
/// </summary>
public interface IBackupArchiveStore
{
    /// <summary>
    /// Build the archive on a server-side temp file (complete-or-error, FR-006) and return its path.
    /// The caller streams then deletes it. CV bytes are copied from each <see cref="CvFileSource"/>.
    /// </summary>
    Task<string> WriteAsync(
        BackupManifest manifest,
        IReadOnlyList<TableSnapshot> tables,
        IReadOnlyList<CvFileSource> cvFiles,
        CancellationToken ct = default);

    /// <summary>
    /// Read and validate an uploaded archive (manifest present/parseable + understood version; every
    /// <c>cv-data</c> file present with matching SHA-256; <c>candidate_cv.file_name</c> ↔ archived files
    /// cross-check; required <c>db/*.copy</c> present). Returns the parsed content (+ non-fatal warnings)
    /// or an <see cref="BackupErrors.InvalidArchive"/> failure. Never touches live data.
    /// </summary>
    Task<Result<BackupArchive>> ReadAsync(Stream archive, CancellationToken ct = default);
}

/// <summary>A CV file to embed in the archive: its stored name and absolute on-disk path.</summary>
public sealed record CvFileSource(string Name, string AbsolutePath);

/// <summary>The validated, in-memory content of an uploaded archive (ready for inspect or restore).</summary>
public sealed record BackupArchive(
    BackupManifest Manifest,
    IReadOnlyList<TableRestore> Tables,
    IReadOnlyList<RestoredCvFile> CvFiles,
    IReadOnlyList<string> Warnings);

/// <summary>One CV file extracted from an archive: its name and raw bytes (already SHA-verified).</summary>
public sealed record RestoredCvFile(string Name, byte[] Data);

/// <summary>The expected (non-exceptional) backup/restore failures, mapped to HTTP by the Web boundary.</summary>
public static class BackupErrors
{
    public static readonly Error BusyMaintenance =
        new("BusyMaintenance", "A backup or restore is already running. Try again once it completes.");

    public static readonly Error InvalidArchive =
        new("InvalidArchive", "The uploaded file is not a valid backup archive.");

    public static readonly Error IncompatibleNewer =
        new("IncompatibleNewer", "This backup was created by a newer app version and cannot be restored. Update the app first.");

    public static readonly Error BackupFailed =
        new("BackupFailed", "The backup could not be created.");

    public static readonly Error RestoreFailed =
        new("RestoreFailed", "The restore failed and live data was rolled back to its previous state.");

    /// <summary>An <see cref="InvalidArchive"/> with a specific reason, surfaced to the user.</summary>
    public static Error Invalid(string reason) => new(InvalidArchive.Code, reason);
}
