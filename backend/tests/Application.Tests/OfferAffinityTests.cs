using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using static JobOfferMatcher.Application.Tests.EnrichmentDoubles;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// Affinity queue tests (T024/T041): the ≥3 applied-offer emission gate, the write-back stale-hash
/// guard, a valid produced write-back, retry→terminal Failed, and /rerun re-arm — all on the shared
/// EnrichmentService harness (the 4th <c>offerAffinity</c> kind on the same queue).
/// </summary>
public sealed class OfferAffinityTests
{
    private static List<Offer> Applied(params string[] keys)
    {
        var offers = keys.Select(k => AvailableOffer(k, Now)).ToList();
        foreach (var o in offers)
        {
            o.MarkApplied(Now, null);
        }

        return offers;
    }

    [Fact]
    public async Task No_affinity_items_below_three_applied_offers()
    {
        var applied = Applied("a1", "a2"); // 2 applied
        var candidate = AvailableOffer("c1", Now);
        var harness = new EnrichmentHarness(cvs: [], offers: [.. applied, candidate]);

        var work = await harness.Service.GetPendingWorkAsync(50);

        work.Items.OfType<OfferAffinityWorkItem>().ShouldBeEmpty();
        work.Meta.HasAffinityBasis.ShouldBeFalse();
        work.Meta.AppliedCount.ShouldBe(2);
        work.Meta.PendingAffinity.ShouldBe(0); // gated to 0 below the basis
    }

    [Fact]
    public async Task Affinity_items_emitted_at_three_applied_with_self_excluded_basis()
    {
        var applied = Applied("a1", "a2", "a3"); // 3 applied → basis exists
        var harness = new EnrichmentHarness(cvs: [], offers: [.. applied]);

        var work = await harness.Service.GetPendingWorkAsync(50);

        var items = work.Items.OfType<OfferAffinityWorkItem>().ToList();
        items.Count.ShouldBe(3);
        work.Meta.HasAffinityBasis.ShouldBeTrue();
        work.Meta.AppliedCount.ShouldBe(3);
        work.Meta.PendingAffinity.ShouldBe(3);
        // Each item's applied basis excludes the candidate itself (no trivial self-match).
        items.ShouldAllBe(i => i.AppliedBasis.Count == 2);
    }

    [Fact]
    public async Task Affinity_writeback_rejects_a_stale_hash()
    {
        var applied = Applied("a1", "a2", "a3");
        var harness = new EnrichmentHarness(cvs: [], offers: [.. applied]);
        var candidate = applied[0];

        var response = await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{candidate.Id.Value}:affinity", "offerAffinity", "SHA256:1:not-current", "produced",
                Score: 74, Resembles: ["remote"])
        ]));

        response.Results.Single().Outcome.ShouldBe("stale");
        harness.Enrichment.Affinities[candidate.Id].State.ShouldBe(EnrichmentState.Pending);
    }

    [Fact]
    public async Task Affinity_writeback_marks_produced_on_a_current_hash()
    {
        var applied = Applied("a1", "a2", "a3");
        var harness = new EnrichmentHarness(cvs: [], offers: [.. applied]);
        var candidate = applied[0];
        var hash = AffinityHash(candidate, applied);

        var response = await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{candidate.Id.Value}:affinity", "offerAffinity", hash, "produced",
                Score: 74, Resembles: ["senior .NET", "remote"], Rationale: "Close to your applied roles.")
        ]));

        response.Results.Single().Outcome.ShouldBe("produced");
        var row = harness.Enrichment.Affinities[candidate.Id];
        row.State.ShouldBe(EnrichmentState.Produced);
        row.Score.ShouldBe(74);
        row.Resembles.ShouldBe(["senior .NET", "remote"]);
    }

    [Fact]
    public async Task Affinity_out_of_range_score_is_treated_as_a_failure()
    {
        var applied = Applied("a1", "a2", "a3");
        var harness = new EnrichmentHarness(cvs: [], offers: [.. applied], retryLimit: 3);
        var candidate = applied[0];
        var hash = AffinityHash(candidate, applied);

        var response = await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{candidate.Id.Value}:affinity", "offerAffinity", hash, "produced", Score: 150)
        ]));

        response.Results.Single().Outcome.ShouldBe("pendingRetry");
    }

    [Fact]
    public async Task Affinity_failures_become_terminal_at_the_retry_limit_and_rerun_rearms()
    {
        var applied = Applied("a1", "a2", "a3");
        var harness = new EnrichmentHarness(cvs: [], offers: [.. applied], retryLimit: 3);
        var candidate = applied[0];
        var hash = AffinityHash(candidate, applied);

        SubmitResultsRequest Fail() => new([
            new EnrichmentResultItem($"offer:{candidate.Id.Value}:affinity", "offerAffinity", hash, "failed", Reason: "too sparse")
        ]);

        (await harness.Service.SubmitResultsAsync(Fail())).Results.Single().Outcome.ShouldBe("pendingRetry");
        (await harness.Service.SubmitResultsAsync(Fail())).Results.Single().Outcome.ShouldBe("pendingRetry");
        (await harness.Service.SubmitResultsAsync(Fail())).Results.Single().Outcome.ShouldBe("failed");
        harness.Enrichment.Affinities[candidate.Id].State.ShouldBe(EnrichmentState.Failed);

        // /rerun scope=failed re-arms the terminal affinity row (T041).
        await harness.Service.TriggerRerunAsync("failed");
        harness.Enrichment.Affinities[candidate.Id].State.ShouldBe(EnrichmentState.Pending);
    }

    [Fact]
    public async Task Rerun_all_forces_every_affinity_pending()
    {
        var applied = Applied("a1", "a2", "a3");
        var harness = new EnrichmentHarness(cvs: [], offers: [.. applied]);
        var candidate = applied[0];
        harness.Enrichment.Affinities[candidate.Id].MarkProduced(80, [], null, "SHA256:1:h", Now);

        await harness.Service.TriggerRerunAsync("all");

        harness.Enrichment.Affinities.Values.ShouldAllBe(a => a.State == EnrichmentState.Pending);
    }
}
