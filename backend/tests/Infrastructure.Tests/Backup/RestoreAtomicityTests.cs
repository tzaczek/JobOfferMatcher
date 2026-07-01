using JobOfferMatcher.Application.Backup;
using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Infrastructure.Backup;
using JobOfferMatcher.Infrastructure.Cv;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// US2 real-Postgres all-or-nothing test (003 T025, FR-012): a failure injected at the cv-data swap (after
/// TRUNCATE+COPY, before COMMIT) rolls the database back and leaves <c>cv-data</c> untouched — live state
/// is byte-identical to before the restore, and the automatic safety backup exists.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RestoreAtomicityTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_failure_at_the_swap_rolls_back_both_stores_and_keeps_a_safety_backup()
    {
        await postgres.ResetAsync();
        var cvDir = BackupTestSupport.NewTempDir("cv");
        var backupDir = BackupTestSupport.NewTempDir("backup");
        var config = BackupTestSupport.Config(postgres.ConnectionString, cvDir, backupDir);

        BackupTestSupport.SeededData seeded;
        await using (var db = postgres.CreateContext())
        {
            seeded = await BackupTestSupport.SeedAsync(db, cvDir);
        }

        var snapshotStore = new PostgresSnapshotStore(config);
        var archiveStore = new ZipBackupArchiveStore(config);
        var safetyStore = new LocalSafetyBackupStore(config);
        var realCvStore = new LocalCvFileStore(config);
        var faultyCvStore = new ThrowingSwapCvFileStore(realCvStore);
        var gate = new MaintenanceGate(new ScanConcurrencyGuard());

        await using var inspectorDb = postgres.CreateContext();
        var migrations = new EfMigrationInspector(inspectorDb);
        var backupService = new BackupService(
            snapshotStore, archiveStore, migrations, realCvStore, gate, TimeProvider.System, NullLogger<BackupService>.Instance);

        // Build a backup of the seeded (2-offer) state, then mutate live so the archive differs from live.
        var artifact = await backupService.BuildAsync();
        var archiveBytes = await File.ReadAllBytesAsync(artifact.TempPath);
        File.Delete(artifact.TempPath);

        await using (var db = postgres.CreateContext())
        {
            db.Offers.Remove(await db.Offers.SingleAsync(o => o.Id == seeded.OfferB));
            await db.SaveChangesAsync();
        }

        IReadOnlyDictionary<string, (long, string)> before;
        await using (var db = postgres.CreateContext())
        {
            before = await BackupTestSupport.FingerprintAllAsync(db);
        }

        var noOpBackfill = new NoOpBackfill();
        var restore = new RestoreService(
            archiveStore, snapshotStore, migrations, safetyStore, backupService, faultyCvStore,
            noOpBackfill, noOpBackfill, gate, TimeProvider.System, NullLogger<RestoreService>.Instance);

        using var upload = new MemoryStream(archiveBytes);
        var result = await restore.RestoreAsync(upload);

        // The restore failed and reported it (no partial state presented as success).
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RestoreFailed");

        // DB rolled back to the mutated (1-offer) state — NOT the archive's 2-offer state.
        await using (var db = postgres.CreateContext())
        {
            var after = await BackupTestSupport.FingerprintAllAsync(db);
            foreach (var table in BackupTables.InsertOrder)
            {
                after[table].ShouldBe(before[table], $"table '{table}' was not rolled back");
            }

            (await db.Offers.CountAsync()).ShouldBe(1);
        }

        // cv-data untouched (the original file is still there, intact).
        File.Exists(Path.Combine(cvDir, seeded.CvFileName)).ShouldBeTrue();
        (await File.ReadAllBytesAsync(Path.Combine(cvDir, seeded.CvFileName))).ShouldBe(seeded.CvBytes);

        // The automatic safety pre-backup was written before the wipe began (FR-009).
        Directory.GetFiles(backupDir, "jobs-safety-*.zip").ShouldNotBeEmpty();
    }

    private sealed class ThrowingSwapCvFileStore(ICvFileStore inner) : ICvFileStore
    {
        public Task<string> SaveAsync(Domain.Common.Ids.CvId id, string originalFileName, Stream content, CancellationToken ct = default) =>
            inner.SaveAsync(id, originalFileName, content, ct);

        public void Delete(string storedFileName) => inner.Delete(storedFileName);

        public string GetAbsolutePath(string storedFileName) => inner.GetAbsolutePath(storedFileName);

        public IReadOnlyList<StoredCvFile> EnumerateAll() => inner.EnumerateAll();

        public ICvDirectorySwap StageSwap(IReadOnlyList<CvFilePayload> files) => new ThrowingSwap();
    }

    private sealed class ThrowingSwap : ICvDirectorySwap
    {
        public void Commit() => throw new IOException("injected swap failure");

        public void Rollback() { }

        public void Dispose() { }
    }

    private sealed class NoOpBackfill : IEnrichmentBackfill, JobOfferMatcher.Application.Applications.IApplicationBackfill
    {
        public Task RunAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
