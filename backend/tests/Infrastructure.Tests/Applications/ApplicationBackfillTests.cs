using System.Net.Http.Json;
using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Tests.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobOfferMatcher.Infrastructure.Tests.Applications;

/// <summary>
/// No-data-loss backfill tests (T036, SC-001/SC-007): <see cref="DatabaseInitializer.BackfillApplicationsAsync"/>
/// reconstructs an application at the first stage for every applied offer that lacks one, migrating the
/// legacy <c>offer.ApplicationNote</c> to the first journal entry, and re-running it creates nothing
/// (idempotent). This mirrors the pre-005 upgrade AND the older-restore path (both call the same method).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ApplicationBackfillTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Backfill_reconstructs_the_application_and_migrates_the_legacy_note_idempotently()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("legacy", 22000m));
        (await http.PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null })).EnsureSuccessStatusCode();

        // Mark applied with a note (creates the offer flag + note AND an application + first journal note).
        var offer = (await http.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all"))!
            .Data.Single(o => o.Title == "Role legacy");
        var offerGuid = Guid.Parse(offer.OfferId);
        (await http.PutAsJsonAsync(
            $"/api/offers/{offer.OfferId}/application",
            new { appliedAt = "2026-06-15", note = "applied via referral" })).EnsureSuccessStatusCode();

        // Simulate the PRE-005 state: an applied offer + its note, but NO application row (delete cascades the
        // journal note away too). The offer.Applied flag + offer.ApplicationNote remain — the legacy data.
        await using (var db = postgres.CreateContext())
        {
            await db.Applications.Where(a => a.OfferId == OfferIdOf(offerGuid)).ExecuteDeleteAsync();
            (await db.Applications.CountAsync()).ShouldBe(0);
            (await db.ApplicationNotes.CountAsync()).ShouldBe(0);
        }

        // Run the backfill — the load-bearing no-data-loss mechanism.
        await using (var db = postgres.CreateContext())
        {
            await DatabaseInitializer.BackfillApplicationsAsync(db, TimeProvider.System, NullLogger.Instance);
        }

        await using (var db = postgres.CreateContext())
        {
            var firstStage = await db.PipelineStages.OrderBy(s => s.Position).FirstAsync();
            var application = await db.Applications.SingleAsync();
            application.OfferId.ShouldBe(OfferIdOf(offerGuid));
            application.CurrentStageId.ShouldBe(firstStage.Id);
            application.Status.ShouldBe(ApplicationStatus.Active);
            application.AppliedAt!.Value.UtcDateTime.Date.ShouldBe(new DateTime(2026, 6, 15));

            var note = await db.ApplicationNotes.SingleAsync();
            note.Body.ShouldBe("applied via referral");
        }

        // Re-running the backfill is a no-op (idempotent — only fills gaps).
        await using (var db = postgres.CreateContext())
        {
            await DatabaseInitializer.BackfillApplicationsAsync(db, TimeProvider.System, NullLogger.Instance);
        }

        await using (var db = postgres.CreateContext())
        {
            (await db.Applications.CountAsync()).ShouldBe(1);
            (await db.ApplicationNotes.CountAsync()).ShouldBe(1);
        }
    }

    private static Domain.Common.Ids.OfferId OfferIdOf(Guid value) => Domain.Common.Ids.OfferId.From(value);

    private sealed record OffersEnvelope(List<OfferItem> Data);

    private sealed record OfferItem(string OfferId, string Title);
}
