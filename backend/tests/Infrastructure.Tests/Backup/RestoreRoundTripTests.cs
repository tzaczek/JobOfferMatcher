using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Application.Backup;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// US2 real-Postgres round-trip test (003 T024, SC-004): back up → mutate/delete → restore → the data
/// (every table, incl. salary_bands Currency / member_offer_ids jsonb / enum+owned columns) and the CV
/// file bytes match the backup 1:1. Zero data loss across both stores.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RestoreRoundTripTests(PostgresFixture postgres)
{
    private sealed record RestoreReportDto(
        DateTimeOffset RestoredAtUtc,
        string Compatibility,
        Dictionary<string, long> TableCounts,
        int CvFileCount,
        string SafetyBackupPath,
        bool BackfillApplied);

    [Fact]
    public async Task Backup_then_wipe_then_restore_reproduces_every_store_exactly()
    {
        await postgres.ResetAsync();
        var (factory, cvDir, _) = BackupTestSupport.NewFactory(postgres.ConnectionString);
        await using var _f = factory;
        var client = factory.CreateClient();

        BackupTestSupport.SeededData seeded;
        IReadOnlyDictionary<string, (long, string)> before;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            seeded = await BackupTestSupport.SeedAsync(db, cvDir);
            before = await BackupTestSupport.FingerprintAllAsync(db);
        }

        var archive = await client.GetByteArrayAsync("/api/backup");

        // Mutate both stores: delete an offer (cascades its satellites) + remove the CV file from disk.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var offer = await db.Offers.SingleAsync(o => o.Id == seeded.OfferB);
            db.Offers.Remove(offer);
            await db.SaveChangesAsync();
        }

        File.Delete(Path.Combine(cvDir, seeded.CvFileName));

        // Restore the archive.
        using var content = BackupTestSupport.MultipartArchive(archive);
        var response = await client.PostAsync("/api/backup/restore", content);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<RestoreReportDto>();
        report!.Compatibility.ShouldBe("Same");
        report.CvFileCount.ShouldBe(1);
        report.SafetyBackupPath.ShouldNotBeNullOrWhiteSpace();
        File.Exists(report.SafetyBackupPath).ShouldBeTrue();

        // Every table is byte-identical to the backup.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var after = await BackupTestSupport.FingerprintAllAsync(db);
            foreach (var table in BackupTables.InsertOrder)
            {
                after[table].ShouldBe(before[table], $"table '{table}' did not round-trip");
            }
        }

        // The CV file bytes are restored exactly.
        var restoredCv = await File.ReadAllBytesAsync(Path.Combine(cvDir, seeded.CvFileName));
        restoredCv.ShouldBe(seeded.CvBytes);
    }
}
