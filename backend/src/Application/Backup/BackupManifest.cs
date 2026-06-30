namespace JobOfferMatcher.Application.Backup;

/// <summary>
/// The self-describing header of a backup archive (003 data-model §2), serialized as
/// <c>manifest.json</c> (camelCase). It records the format version, when the snapshot was taken,
/// the app/EF product version, the applied-migration <see cref="MigrationTip"/> (load-bearing for
/// cross-version restore, FR-017), the ordered table inventory with each table's explicit column
/// list + row count (FR-005), and the CV file inventory with sizes + SHA-256.
/// </summary>
public sealed record BackupManifest(
    int BackupFormatVersion,
    DateTimeOffset CreatedAtUtc,
    string AppProductVersion,
    string MigrationTip,
    IReadOnlyList<BackupTable> Tables,
    IReadOnlyList<CvFileEntry> CvFiles,
    int CvFileCount)
{
    /// <summary>The archive schema version this build writes; restore refuses an unknown/greater value.</summary>
    public const int CurrentFormatVersion = 1;
}

/// <summary>
/// One transported table: its name, the explicit ordered column list (replayed verbatim into
/// <c>COPY … FROM STDIN (columns)</c> — the load-bearing element for cross-version restore), and the
/// per-category row count (FR-005) so inspect reads counts without parsing payloads.
/// </summary>
public sealed record BackupTable(string Name, IReadOnlyList<string> Columns, long RowCount);

/// <summary>One stored CV file in the archive's <c>cv-data/</c> folder: its name, byte size, and SHA-256.</summary>
public sealed record CvFileEntry(string Name, long Size, string Sha256);
