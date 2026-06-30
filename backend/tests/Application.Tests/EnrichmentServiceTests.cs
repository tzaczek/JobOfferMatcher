using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using static JobOfferMatcher.Application.Tests.EnrichmentDoubles;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// EnrichmentService unit tests (T020): pending-work ordering (profile-first, available-first,
/// newest-first — FR-019), the recompute stale guard, retry→Failed, and Failed re-arm on re-run.
/// </summary>
public sealed class EnrichmentServiceTests
{
    [Fact]
    public async Task Pending_work_orders_profile_first_then_summaries()
    {
        var harness = new EnrichmentHarness(cvs: [PendingCv()], offers: [AvailableOffer("o1", Now.AddDays(-2))]);

        var work = await harness.Service.GetPendingWorkAsync(25);

        var kinds = work.Items.Select(KindOf).ToList();
        kinds[0].ShouldBe("cvProfile");        // profiles first (FR-019)
        kinds.ShouldContain("offerSummary");
        kinds.ShouldNotContain("offerFit");    // no produced profile ⇒ fits absent
        work.Meta.HasProducedProfile.ShouldBeFalse();
    }

    [Fact]
    public async Task Pending_work_orders_available_before_unavailable_then_newest_first()
    {
        var older = AvailableOffer("older", Now.AddDays(-5));
        var newer = AvailableOffer("newer", Now.AddDays(-1));
        var gone = AvailableOffer("gone", Now);
        gone.MarkUnavailable(Now);

        var harness = new EnrichmentHarness(cvs: [], offers: [newer, older, gone]);

        var work = await harness.Service.GetPendingWorkAsync(25);
        var titles = work.Items.OfType<OfferSummaryWorkItem>().Select(i => i.Offer.Title).ToList();

        titles.ShouldBe(["Role newer", "Role older", "Role gone"]);
    }

    [Fact]
    public async Task Fits_are_emitted_only_with_a_produced_profile()
    {
        var harness = new EnrichmentHarness(cvs: [ProducedCv()], offers: [AvailableOffer("o1", Now)]);

        var work = await harness.Service.GetPendingWorkAsync(25);

        work.Meta.HasProducedProfile.ShouldBeTrue();
        work.Items.OfType<OfferFitWorkItem>().Count().ShouldBe(1);
        work.Items.Select(KindOf).ToList().ShouldBe(["offerSummary", "offerFit"]); // summary before fit
    }

    [Fact]
    public async Task Submit_rejects_a_stale_hash_and_leaves_the_row_pending()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [], offers: [offer]);

        var response = await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{offer.Id.Value}:summary", "offerSummary", "SHA256:1:not-current", "produced", Summary: "x", KeySkills: ["C#"])
        ]));

        response.Results.Single().Outcome.ShouldBe("stale");
        harness.Enrichment.Enrichments[offer.Id].State.ShouldBe(EnrichmentState.Pending);
    }

    [Fact]
    public async Task Summary_failures_become_terminal_at_the_retry_limit()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [], offers: [offer], retryLimit: 3);
        var hash = SummaryHash(offer);

        for (var i = 0; i < 2; i++)
        {
            var r = await harness.Service.SubmitResultsAsync(Failed(offer, hash));
            r.Results.Single().Outcome.ShouldBe("pendingRetry");
        }

        (await harness.Service.SubmitResultsAsync(Failed(offer, hash))).Results.Single().Outcome.ShouldBe("failed");
        harness.Enrichment.Enrichments[offer.Id].State.ShouldBe(EnrichmentState.Failed);
    }

    [Fact]
    public async Task Rerun_failed_rearms_terminal_rows()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [], offers: [offer]);
        harness.Enrichment.Enrichments[offer.Id].RecordFailure("x", retryLimit: 1); // → Failed

        await harness.Service.TriggerRerunAsync("failed");

        harness.Enrichment.Enrichments[offer.Id].State.ShouldBe(EnrichmentState.Pending);
    }

    private static SubmitResultsRequest Failed(Offer offer, string hash) => new([
        new EnrichmentResultItem($"offer:{offer.Id.Value}:summary", "offerSummary", hash, "failed", Reason: "empty")
    ]);
}
