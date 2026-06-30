using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Domain.Enrichment;
using static JobOfferMatcher.Application.Tests.EnrichmentDoubles;

namespace JobOfferMatcher.Application.Tests;

/// <summary>US2 offer-summary validation (T032): loose validation + key-skill clamping (FR-005/FR-006).</summary>
public sealed class OfferSummaryTests
{
    [Fact]
    public async Task Produced_summary_clamps_key_skills_to_the_configured_max()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [], offers: [offer], maxKeySkills: 3);

        await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{offer.Id.Value}:summary", "offerSummary", SummaryHash(offer), "produced",
                Summary: "A summary.", KeySkills: ["C#", ".NET", "EF Core", "Azure", "Kafka"])
        ]));

        harness.Enrichment.Enrichments[offer.Id].KeySkills.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Empty_summary_is_rejected_as_a_failure()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [], offers: [offer], retryLimit: 1);

        var response = await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{offer.Id.Value}:summary", "offerSummary", SummaryHash(offer), "produced",
                Summary: "   ", KeySkills: ["C#"])
        ]));

        response.Results.Single().Outcome.ShouldBe("failed");
        harness.Enrichment.Enrichments[offer.Id].State.ShouldBe(EnrichmentState.Failed);
    }

    [Fact]
    public async Task Missing_skills_array_is_rejected()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [], offers: [offer], retryLimit: 1);

        var response = await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{offer.Id.Value}:summary", "offerSummary", SummaryHash(offer), "produced",
                Summary: "A summary.", KeySkills: null)
        ]));

        response.Results.Single().Outcome.ShouldBe("failed");
    }
}
