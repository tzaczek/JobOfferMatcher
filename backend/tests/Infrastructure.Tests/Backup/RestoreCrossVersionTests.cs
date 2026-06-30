using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Application.Backup;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// US2 real-Postgres cross-version tests (003 T027, FR-017): an OLDER backup (missing the satellite
/// tables a later migration added) restores into HEAD — the missing rows are synthesised as
/// <c>Pending</c> by the enrichment backfill; a NEWER backup (unknown migration tip) is refused with
/// <c>IncompatibleNewer</c> and live data is left intact.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RestoreCrossVersionTests(PostgresFixture postgres)
{
    private sealed record RestoreReportDto(string Compatibility, int CvFileCount, bool BackfillApplied);

    [Fact]
    public async Task Older_backup_restores_into_head_and_backfills_pending_satellites()
    {
        await postgres.ResetAsync();
        var (factory, cvDir, _) = BackupTestSupport.NewFactory(postgres.ConnectionString);
        await using var _f = factory;
        var client = factory.CreateClient();

        string olderTip;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await BackupTestSupport.SeedAsync(db, cvDir);
            olderTip = db.Database.GetMigrations().First(); // a known earlier migration
        }

        var archive = await client.GetByteArrayAsync("/api/backup");

        // Synthesise an OLDER backup: tip rewound + the enrichment satellite tables absent (as if a later
        // migration added them). Restore loads offers without satellites; the Older path backfills them.
        var older = BackupTestSupport.Rebuild(
            archive,
            editManifest: m => m with
            {
                MigrationTip = olderTip,
                Tables = [.. m.Tables.Where(t => t.Name is not "offer_enrichment" and not "offer_fit")],
            },
            dropEntries: new HashSet<string> { "db/offer_enrichment.copy", "db/offer_fit.copy" });

        using var content = BackupTestSupport.MultipartArchive(older);
        var response = await client.PostAsync("/api/backup/restore", content);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<RestoreReportDto>();
        report!.Compatibility.ShouldBe("Older");
        report.BackfillApplied.ShouldBeTrue();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var offerCount = await db.Offers.CountAsync();
            offerCount.ShouldBe(2);
            // The backfill synthesised a Pending satellite per offer (FR-014).
            (await db.OfferEnrichments.CountAsync()).ShouldBe(offerCount);
            (await db.OfferFits.CountAsync()).ShouldBe(offerCount);
            (await db.OfferEnrichments.AllAsync(e => e.State == EnrichmentState.Pending)).ShouldBeTrue();
            (await db.OfferFits.AllAsync(f => f.State == EnrichmentState.Pending)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Newer_backup_is_refused_and_live_data_is_intact()
    {
        await postgres.ResetAsync();
        var (factory, cvDir, _) = BackupTestSupport.NewFactory(postgres.ConnectionString);
        await using var _f = factory;
        var client = factory.CreateClient();

        IReadOnlyDictionary<string, (long, string)> before;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await BackupTestSupport.SeedAsync(db, cvDir);
            before = await BackupTestSupport.FingerprintAllAsync(db);
        }

        var archive = await client.GetByteArrayAsync("/api/backup");
        var newer = BackupTestSupport.Rebuild(archive, editManifest: m => m with { MigrationTip = "29990101000000_FromTheFuture" });

        using var content = BackupTestSupport.MultipartArchive(newer);
        var response = await client.PostAsync("/api/backup/restore", content);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var after = await BackupTestSupport.FingerprintAllAsync(db);
            foreach (var table in BackupTables.InsertOrder)
            {
                after[table].ShouldBe(before[table], $"table '{table}' changed despite a refused (newer) restore");
            }
        }
    }
}
