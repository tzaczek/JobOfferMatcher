using System.Net.Http.Json;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Tests.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static JobOfferMatcher.Infrastructure.Tests.EnrichmentFlow;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// US2 end-to-end on real Postgres (T033, FR-006/SC-001): an offer starts <c>pending</c>; the worker
/// write-back over <c>/api/enrichment/results</c> makes the feed show <c>produced</c> + summary; a
/// later description change re-flips that offer to <c>pending</c> (read-path stale guard).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OfferSummaryFlowTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Pending_summary_becomes_produced_then_pending_again_on_description_change()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            AddOfferWithSatellites(db, "sum-1");
            await db.SaveChangesAsync();
        }

        // Before the worker runs, the feed shows pending — never a fabricated summary (FR-005).
        var before = await client.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all");
        before!.Data.Single().EnrichmentState.ShouldBe("pending");
        before.Data.Single().Summary.ShouldBeNull();

        // The queue offers a summary work item.
        var pending = await client.GetFromJsonAsync<PendingEnvelope>("/api/enrichment/pending");
        var item = pending!.Items.Single(i => i.Kind == "offerSummary");

        // The worker posts a produced result, echoing the inputs hash.
        var submit = await client.PostAsJsonAsync("/api/enrichment/results", new
        {
            results = new[]
            {
                new
                {
                    workItemId = item.WorkItemId,
                    kind = "offerSummary",
                    inputsHash = item.InputsHash,
                    status = "produced",
                    summary = "A concise senior .NET role.",
                    keySkills = new[] { "C#", ".NET" },
                },
            },
        });
        submit.EnsureSuccessStatusCode();
        var submitResult = await submit.Content.ReadFromJsonAsync<SubmitEnvelope>();
        submitResult!.Accepted.ShouldBe(1);

        // The feed now shows the produced summary + key skills.
        var produced = await client.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all");
        var offer = produced!.Data.Single();
        offer.EnrichmentState.ShouldBe("produced");
        offer.Summary.ShouldBe("A concise senior .NET role.");
        offer.KeySkills.ShouldBe(["C#", ".NET"]);

        // A later description change invalidates the summary inputs ⇒ the offer is pending again (FR-006).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Offers.SingleAsync();
            stored.RefreshMinorContent(new OfferContent
            {
                Title = stored.Title,
                Company = stored.Company,
                CanonicalUrl = stored.CanonicalUrl,
                RequiredSkills = [.. stored.RequiredSkills],
                NiceToHaveSkills = [.. stored.NiceToHaveSkills],
                DescriptionHtml = "<p>Now with a totally different description.</p>",
                PublishedAt = stored.PublishedAt,
            });
            await db.SaveChangesAsync();
        }

        var after = await client.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all");
        after!.Data.Single().EnrichmentState.ShouldBe("pending");
        after.Data.Single().Summary.ShouldBeNull();
    }
}
