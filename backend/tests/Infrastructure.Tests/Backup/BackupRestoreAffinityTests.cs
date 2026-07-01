using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// 006 backup/restore for the affinity satellite (T043, FR-017/SC-006): a produced <c>offer_affinity</c>
/// row survives a full backup → wipe → restore byte-identical (proving it joined
/// <c>BackupTables.InsertOrder</c>); and an OLDER (pre-006) backup that lacks the table restores into HEAD
/// with the affinity rows re-synthesised as <c>Pending</c> by the shared enrichment backfill (no new port).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackupRestoreAffinityTests(PostgresFixture postgres)
{
    private sealed record RestoreReportDto(string Compatibility, bool BackfillApplied);

    [Fact]
    public async Task Backup_restore_round_trips_a_produced_offer_affinity_row()
    {
        await postgres.ResetAsync();
        var (factory, cvDir, _) = BackupTestSupport.NewFactory(postgres.ConnectionString);
        await using var _f = factory;
        var client = factory.CreateClient();

        OfferId offerId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var offer = BackupTestSupport.OfferWithSalary("aff-backup", Currency.Pln, 18000m, 24000m);
            db.Offers.Add(offer);
            offerId = offer.Id;
            db.OfferEnrichments.Add(OfferEnrichment.CreatePending(offer.Id));
            db.OfferFits.Add(OfferFit.CreatePending(offer.Id));

            var affinity = OfferAffinity.CreatePending(offer.Id);
            affinity.MarkProduced(74, ["remote", "senior .NET"], "Close to your applied roles.", "SHA256:1:abc", DateTimeOffset.UtcNow);
            db.OfferAffinities.Add(affinity);
            await db.SaveChangesAsync();
        }

        var archive = await client.GetByteArrayAsync("/api/backup");

        // Wipe the affinity row (cascade-safe standalone delete).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.OfferAffinities.Remove(await db.OfferAffinities.SingleAsync(a => a.OfferId == offerId));
            await db.SaveChangesAsync();
            (await db.OfferAffinities.CountAsync()).ShouldBe(0);
        }

        using var content = BackupTestSupport.MultipartArchive(archive);
        (await client.PostAsync("/api/backup/restore", content)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var restored = await db.OfferAffinities.AsNoTracking().SingleAsync(a => a.OfferId == offerId);
            restored.State.ShouldBe(EnrichmentState.Produced);
            restored.Score.ShouldBe(74);
            restored.Resembles.ShouldBe(["remote", "senior .NET"]);
            restored.InputsHash.ShouldBe("SHA256:1:abc");
        }
    }

    [Fact]
    public async Task Older_pre006_backup_restore_backfills_pending_affinity_rows()
    {
        await postgres.ResetAsync();
        var (factory, cvDir, _) = BackupTestSupport.NewFactory(postgres.ConnectionString);
        await using var _f = factory;
        var client = factory.CreateClient();

        string olderTip;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await BackupTestSupport.SeedAsync(db, cvDir); // 2 offers + enrichment/fit (no affinity rows)
            olderTip = db.Database.GetMigrations().First();
        }

        var archive = await client.GetByteArrayAsync("/api/backup");

        // Synthesise a PRE-006 backup: tip rewound + the offer_affinity table absent (as if 006 added it).
        var older = BackupTestSupport.Rebuild(
            archive,
            editManifest: m => m with
            {
                MigrationTip = olderTip,
                Tables = [.. m.Tables.Where(t => t.Name != "offer_affinity")],
            },
            dropEntries: new HashSet<string> { "db/offer_affinity.copy" });

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
            // The affinity rows were re-synthesised as Pending by the shared backfill (SC-006, no data lost).
            (await db.OfferAffinities.CountAsync()).ShouldBe(offerCount);
            (await db.OfferAffinities.AllAsync(a => a.State == EnrichmentState.Pending)).ShouldBeTrue();
        }
    }
}
