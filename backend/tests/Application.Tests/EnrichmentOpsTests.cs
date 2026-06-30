using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Domain.Enrichment;
using static JobOfferMatcher.Application.Tests.EnrichmentDoubles;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// US5 operations (T057): the status counts are eligibility-gated (fits count only with a produced CV
/// profile, so <c>pendingTotal</c> can reach 0 — SC-007); <c>rerun failed</c> re-arms terminal rows while
/// leaving produced ones; <c>rerun all</c> forces a full re-run. (Backfill idempotency is covered by the
/// Infrastructure EnrichmentBackfillTests against real Postgres.)
/// </summary>
public sealed class EnrichmentOpsTests
{
    [Fact]
    public async Task Pending_fits_are_not_counted_without_a_produced_profile()
    {
        // No produced profile ⇒ the ~N pending fit rows must NOT inflate the count (else they'd be stuck).
        var harness = new EnrichmentHarness(cvs: [PendingCv()], offers: [AvailableOffer("o1", Now), AvailableOffer("o2", Now)]);

        var status = await harness.Service.GetStatusAsync();

        status.PendingFits.ShouldBe(0);
        status.PendingSummaries.ShouldBe(2);
        status.PendingProfiles.ShouldBe(1);
        status.HasProducedProfile.ShouldBeFalse();
    }

    [Fact]
    public async Task Pending_fits_are_counted_once_a_profile_is_produced()
    {
        var harness = new EnrichmentHarness(cvs: [ProducedCv()], offers: [AvailableOffer("o1", Now), AvailableOffer("o2", Now)]);

        var status = await harness.Service.GetStatusAsync();

        status.PendingFits.ShouldBe(2);
        status.HasProducedProfile.ShouldBeTrue();
    }

    [Fact]
    public async Task Rerun_all_forces_produced_rows_back_to_pending()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [], offers: [offer]);
        await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{offer.Id.Value}:summary", "offerSummary", SummaryHash(offer), "produced",
                Summary: "A summary.", KeySkills: ["C#"])
        ]));
        harness.Enrichment.Enrichments[offer.Id].State.ShouldBe(EnrichmentState.Produced);

        await harness.Service.TriggerRerunAsync("all");

        harness.Enrichment.Enrichments[offer.Id].State.ShouldBe(EnrichmentState.Pending);
    }

    [Fact]
    public async Task Rerun_failed_rearms_failed_but_leaves_produced()
    {
        var produced = AvailableOffer("produced", Now);
        var failed = AvailableOffer("failed", Now);
        var harness = new EnrichmentHarness(cvs: [], offers: [produced, failed]);

        await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{produced.Id.Value}:summary", "offerSummary", SummaryHash(produced), "produced",
                Summary: "ok", KeySkills: ["C#"])
        ]));
        harness.Enrichment.Enrichments[failed.Id].RecordFailure("x", retryLimit: 1); // → Failed

        await harness.Service.TriggerRerunAsync("failed");

        harness.Enrichment.Enrichments[failed.Id].State.ShouldBe(EnrichmentState.Pending);   // re-armed
        harness.Enrichment.Enrichments[produced.Id].State.ShouldBe(EnrichmentState.Produced); // untouched
    }
}
