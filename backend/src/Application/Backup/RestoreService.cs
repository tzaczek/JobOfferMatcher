using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Application.Backup;

/// <summary>
/// Orchestrates the guarded, all-or-nothing restore (003 US2, data-model §7): acquire the maintenance
/// gate (draining the in-flight scan) → fully validate the upload before touching anything (FR-011) →
/// decide cross-version compatibility (refuse <c>Newer</c>) → write the automatic safety pre-backup →
/// stage the new <c>cv-data</c> → wipe + reload the DB in one transaction with the file swap committed
/// just before <c>COMMIT</c> → (Older) run the enrichment backfill. Any failure after the wipe begins
/// rolls the database back and swaps the files back; the safety backup is the last-resort recovery.
/// </summary>
public sealed class RestoreService(
    IBackupArchiveStore archiveStore,
    IDatabaseSnapshotStore snapshotStore,
    IMigrationInspector migrations,
    ISafetyBackupStore safetyStore,
    BackupService backupService,
    ICvFileStore cvFileStore,
    IEnrichmentBackfill backfill,
    MaintenanceGate gate,
    TimeProvider time,
    ILogger<RestoreService> logger)
{
    /// <summary>How long to wait for an in-flight scan to drain before reporting busy.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);

    public async Task<Result<RestoreReport>> RestoreAsync(Stream upload, CancellationToken ct = default)
    {
        if (!await gate.TryBeginRestoreAsync(DrainTimeout, ct))
        {
            return BackupErrors.BusyMaintenance;
        }

        try
        {
            // 1. Validate the upload fully BEFORE touching anything (FR-011 — live data stays intact).
            var read = await archiveStore.ReadAsync(upload, ct);
            if (read.IsFailure)
            {
                return read.Error;
            }

            var archive = read.Value;

            // 2. Cross-version decision (FR-017): refuse a backup from a newer app version.
            var compatibility = BackupCompatibilityPolicy.Decide(archive.Manifest.MigrationTip, migrations.KnownMigrations());
            if (compatibility == BackupCompatibility.Newer)
            {
                return BackupErrors.IncompatibleNewer;
            }

            // 3. Automatic safety pre-backup of the CURRENT state (FR-009) — the rollback of last resort.
            string safetyPath;
            try
            {
                var safety = await backupService.BuildAsync(ct);
                safetyPath = await safetyStore.PersistAsync(safety.TempPath, time.GetUtcNow(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Restore aborted: the safety pre-backup could not be created. Live data untouched.");
                return BackupErrors.RestoreFailed;
            }

            // 4. Stage the new cv-data; 5. wipe + reload the DB with the file swap committed inside the tx.
            var payloads = archive.CvFiles.Select(c => new CvFilePayload(c.Name, c.Data)).ToList();
            using var swap = cvFileStore.StageSwap(payloads);
            var swapped = false;
            try
            {
                await snapshotStore.RestoreAsync(
                    archive.Tables,
                    _ =>
                    {
                        swap.Commit();
                        swapped = true;
                        return Task.CompletedTask;
                    },
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (swapped)
                {
                    swap.Rollback();
                }

                logger.LogError(
                    ex,
                    "Restore failed mid-flight; database rolled back and cv-data swapped back. Safety backup at {Path}.",
                    safetyPath);
                return BackupErrors.RestoreFailed;
            }

            // 6. Older backup → idempotent enrichment backfill (synthesise satellites added by later migrations).
            var backfillApplied = compatibility == BackupCompatibility.Older;
            if (backfillApplied)
            {
                await backfill.RunAsync(ct);
            }

            var tableCounts = archive.Manifest.Tables.ToDictionary(t => t.Name, t => t.RowCount);
            logger.LogInformation(
                "Restore complete ({Compatibility}); {Cv} CV file(s); safety backup at {Path}.",
                compatibility, archive.CvFiles.Count, safetyPath);
            return new RestoreReport(time.GetUtcNow(), compatibility, tableCounts, archive.CvFiles.Count, safetyPath, backfillApplied);
        }
        finally
        {
            gate.EndRestore();
        }
    }
}
