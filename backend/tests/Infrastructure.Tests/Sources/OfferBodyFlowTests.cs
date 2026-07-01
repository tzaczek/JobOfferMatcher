using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.DependencyInjection;
using static JobOfferMatcher.Infrastructure.Tests.EnrichmentFlow;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// Offer-body capture on scan (T034, feature 006 US2, real Postgres): a scan fetches the detail body
/// for new/updated/body-missing offers, the body is sanitised on read, a missing body surfaces the
/// "not available" path (a fetch failure never fails the scan), and an offer that gains a body has its
/// own summary/affinity re-pended (FR-016) while another offer's stays produced.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OfferBodyFlowTests(PostgresFixture postgres)
{
    private const string AllOffers = "/api/offers?status=all&availability=all";

    [Fact]
    public async Task Scan_captures_the_body_sanitised_and_a_missing_body_shows_unavailable()
    {
        await postgres.ResetAsync();
        var fake = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, fake);
        var client = factory.CreateClient();

        fake.SetOffers(("with-body", 22000), ("no-body", 21000));
        // A body WITH a script tag to prove read-time sanitisation; "no-body" has no detail body registered.
        fake.SetBody("with-body-slug", "<p>Real requirements.</p><script>alert('xss')</script>");

        (await ScanJjitAsync(client)).EnsureSuccessStatusCode();

        var offers = (await client.GetFromJsonAsync<OffersEnvelope>(AllOffers))!.Data;
        var withBody = offers.Single(o => o.Title == "Role with-body");
        var noBody = offers.Single(o => o.Title == "Role no-body");

        var withBodyDetail = await client.GetFromJsonAsync<OfferDetailDto>($"/api/offers/{withBody.OfferId}");
        withBodyDetail!.DescriptionHtml.ShouldNotBeNull();
        withBodyDetail.DescriptionHtml!.ShouldContain("Real requirements.");
        withBodyDetail.DescriptionHtml.ShouldNotContain("<script>"); // sanitised (FR-015)

        var noBodyDetail = await client.GetFromJsonAsync<OfferDetailDto>($"/api/offers/{noBody.OfferId}");
        noBodyDetail!.DescriptionHtml.ShouldBeNull(); // "description not available" (FR-014) — scan not failed
    }

    [Fact]
    public async Task An_offer_that_gains_a_body_repends_its_own_metrics_but_not_another_offers()
    {
        await postgres.ResetAsync();
        var fake = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, fake);
        var client = factory.CreateClient();

        // Three applied offers (a different source) form the affinity basis; the scan never touches them.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            AddAppliedOfferWithSatellites(db, "basis-1");
            AddAppliedOfferWithSatellites(db, "basis-2");
            AddAppliedOfferWithSatellites(db, "basis-3");
            await db.SaveChangesAsync();
        }

        // Scan 1: collect X and Y with no bodies yet.
        fake.SetOffers(("gainer", 22000), ("keeper", 21000));
        (await ScanJjitAsync(client)).EnsureSuccessStatusCode();

        var x = (await Offers(client)).Single(o => o.Title == "Role gainer");
        var y = (await Offers(client)).Single(o => o.Title == "Role keeper");

        // Produce a summary + affinity for both X and Y.
        await ProduceSummaryAndAffinity(client, Guid.Parse(x.OfferId));
        await ProduceSummaryAndAffinity(client, Guid.Parse(y.OfferId));

        var producedX = (await Offers(client)).Single(o => o.OfferId == x.OfferId);
        producedX.EnrichmentState.ShouldBe("produced");
        producedX.Affinity!.State.ShouldBe("produced");

        // Give ONLY X a body, then re-scan: X is body-missing → gains a body → its summary/fit/affinity re-pend.
        fake.SetBody("gainer-slug", "<p>Now with a real body.</p>");
        (await ScanJjitAsync(client)).EnsureSuccessStatusCode();

        var afterX = (await Offers(client)).Single(o => o.OfferId == x.OfferId);
        afterX.EnrichmentState.ShouldBe("pending");        // its own summary re-pended (FR-016)
        afterX.Affinity!.State.ShouldBe("pending");        // its own affinity re-pended

        var afterY = (await Offers(client)).Single(o => o.OfferId == y.OfferId);
        afterY.EnrichmentState.ShouldBe("produced");       // untouched — Y didn't change
        afterY.Affinity!.State.ShouldBe("produced");       // basis unchanged (body is Minor) → Y stays produced
    }

    /// <summary>Scan ONLY the seeded justjoin.it source (the fake client) — keeps live sources out of the queue.</summary>
    private static Task<HttpResponseMessage> ScanJjitAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/scans/run", new { sourceIds = new[] { DatabaseSeeder.DefaultJustJoinItSourceId.Value.ToString() } });

    private static async Task<List<OfferItem>> Offers(HttpClient client) =>
        (await client.GetFromJsonAsync<OffersEnvelope>(AllOffers))!.Data;

    /// <summary>Drain and produce the summary + affinity work items for one offer.</summary>
    private static async Task ProduceSummaryAndAffinity(HttpClient client, Guid offerId)
    {
        var pending = await client.GetFromJsonAsync<PendingEnvelope>("/api/enrichment/pending?limit=100");
        pending!.Meta.AppliedCount.ShouldBe(3, $"appliedCount; kinds=[{string.Join(",", pending.Items.Select(i => i.Kind))}]");
        pending.Meta.HasAffinityBasis.ShouldBeTrue();
        var summary = pending.Items.Single(i => i.Kind == "offerSummary" && i.OfferId == offerId);
        var affinity = pending.Items.Single(i => i.Kind == "offerAffinity" && i.OfferId == offerId);

        (await client.PostAsJsonAsync("/api/enrichment/results", new
        {
            results = new object[]
            {
                new { workItemId = summary.WorkItemId, kind = "offerSummary", inputsHash = summary.InputsHash, status = "produced", summary = "A role.", keySkills = new[] { "C#" } },
                new { workItemId = affinity.WorkItemId, kind = "offerAffinity", inputsHash = affinity.InputsHash, status = "produced", score = 70, resembles = new[] { "remote" } },
            },
        })).EnsureSuccessStatusCode();
    }

    private sealed record OfferDetailDto(string? DescriptionHtml);
}
