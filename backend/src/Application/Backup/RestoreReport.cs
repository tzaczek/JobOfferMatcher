namespace JobOfferMatcher.Application.Backup;

/// <summary>
/// The outcome of a successful restore (003 contracts §3, FR-013): when it ran, how the backup
/// related to this build, the per-table restored counts, the CV file count, the server-side safety
/// backup path (the rollback-of-last-resort), and whether the enrichment backfill ran (Older path).
/// </summary>
public sealed record RestoreReport(
    DateTimeOffset RestoredAtUtc,
    BackupCompatibility Compatibility,
    IReadOnlyDictionary<string, long> TableCounts,
    int CvFileCount,
    string SafetyBackupPath,
    bool BackfillApplied);

/// <summary>
/// Port for persisting the automatic pre-restore safety backup (003 ADR-4): takes an already-built
/// archive and files it under the gitignored backups dir as <c>jobs-safety-&lt;ts&gt;.zip</c>, returning
/// its final path. That archive is the rollback source if the restore fails after the wipe begins.
/// </summary>
public interface ISafetyBackupStore
{
    Task<string> PersistAsync(string builtArchivePath, DateTimeOffset takenAt, CancellationToken ct = default);
}
