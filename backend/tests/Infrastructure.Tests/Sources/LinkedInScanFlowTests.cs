using System.Net.Http.Json;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Persistence.Seed;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Domain.Common.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static JobOfferMatcher.Infrastructure.Tests.EnrichmentFlow;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// Feature 008, US1 (T018, real Postgres): a manual scan with a fake <c>ILinkedInClient</c> upserts
/// LinkedIn offers keyed by <c>(source_id, LinkedIn job id)</c>, captures the body for new offers, and
/// creates Pending enrichment/fit/affinity satellites (SC-005). A second scan of the same jobs adds
/// <b>zero</b> duplicate offers (SC-002). Only the seeded LinkedIn source is scanned (no live network).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LinkedInScanFlowTests(PostgresFixture postgres)
{
    private const string AllOffers = "/api/offers?status=all&availability=all";
    private static readonly string LinkedInSourceId = DatabaseSeeder.DefaultLinkedInSourceId.Value.ToString();

    [Fact]
    public async Task Manual_scan_upserts_offers_with_body_and_pending_satellites_and_no_duplicates_on_rescan()
    {
        await postgres.ResetAsync();
        var linkedIn = new FakeLinkedInClient();
        linkedIn.SetRecommended(
            SourceFetchStatus.Ok,
            FakeLinkedInClient.Card("4428922336", title: "Senior .NET Engineer"),
            FakeLinkedInClient.Card("4428922337", title: "Backend Developer"));
        linkedIn.SetBody("4428922336", "<p>Real requirements.</p>");

        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient(), fakeLinkedInClient: linkedIn);
        var client = factory.CreateClient();

        (await ScanLinkedInAsync(client)).EnsureSuccessStatusCode();

        var offers = (await client.GetFromJsonAsync<OffersEnvelope>(AllOffers))!.Data;
        offers.Count.ShouldBe(2);
        var senior = offers.Single(o => o.Title == "Senior .NET Engineer");
        var backend = offers.Single(o => o.Title == "Backend Developer");

        // Body captured for the offer that has one; the other shows "not available" (null), not a failure.
        var seniorDetail = await client.GetFromJsonAsync<OfferDetailDto>($"/api/offers/{senior.OfferId}");
        seniorDetail!.DescriptionHtml.ShouldNotBeNull();
        seniorDetail.DescriptionHtml!.ShouldContain("Real requirements.");
        var backendDetail = await client.GetFromJsonAsync<OfferDetailDto>($"/api/offers/{backend.OfferId}");
        backendDetail!.DescriptionHtml.ShouldBeNull();

        // Every collected offer carries invariant Pending enrichment/fit/affinity satellites (SC-005).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var offerId in new[] { Guid.Parse(senior.OfferId), Guid.Parse(backend.OfferId) })
            {
                var id = OfferId.From(offerId);
                (await db.OfferEnrichments.SingleAsync(e => e.OfferId == id)).State.ShouldBe(EnrichmentState.Pending);
                (await db.OfferFits.SingleAsync(f => f.OfferId == id)).State.ShouldBe(EnrichmentState.Pending);
                (await db.OfferAffinities.SingleAsync(a => a.OfferId == id)).State.ShouldBe(EnrichmentState.Pending);

                // Identity is the LinkedIn job id under the LinkedIn source.
                var offer = await db.Offers.SingleAsync(o => o.Id == id);
                offer.ExternalRef.SourceId.ShouldBe(DatabaseSeeder.DefaultLinkedInSourceId);
            }
        }

        // Re-scan the same two jobs → no duplicate offers (identity dedup across scans, SC-002).
        (await ScanLinkedInAsync(client)).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<OffersEnvelope>(AllOffers))!.Data.Count.ShouldBe(2);
    }

    private static Task<HttpResponseMessage> ScanLinkedInAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/scans/run", new { sourceIds = new[] { LinkedInSourceId } });

    private sealed record OfferDetailDto(string? DescriptionHtml);
}
