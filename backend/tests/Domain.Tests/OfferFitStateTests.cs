using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Enrichment;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>OfferFit state-machine unit tests (T015): produce/fail/rearm mirror OfferEnrichment; score is only set on produce.</summary>
public sealed class OfferFitStateTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_row_is_pending_with_no_score()
    {
        var f = OfferFit.CreatePending(OfferId.New());
        f.State.ShouldBe(EnrichmentState.Pending);
        f.Score.ShouldBeNull();
    }

    [Fact]
    public void Mark_produced_sets_score_matched_missing_rationale()
    {
        var f = OfferFit.CreatePending(OfferId.New());

        f.MarkProduced(82, ["C#", "EF Core"], ["Kafka"], "Strong backend match.", "SHA256:1:abc", At);

        f.State.ShouldBe(EnrichmentState.Produced);
        f.Score.ShouldBe(82);
        f.Matched.ShouldBe(["C#", "EF Core"]);
        f.Missing.ShouldBe(["Kafka"]);
        f.Rationale.ShouldBe("Strong backend match.");
        f.InputsHash.ShouldBe("SHA256:1:abc");
        f.Attempts.ShouldBe(0);
    }

    [Fact]
    public void Failures_become_terminal_only_at_the_retry_limit()
    {
        var f = OfferFit.CreatePending(OfferId.New());
        f.RecordFailure("1", retryLimit: 2);
        f.State.ShouldBe(EnrichmentState.Pending);
        f.RecordFailure("2", retryLimit: 2);
        f.State.ShouldBe(EnrichmentState.Failed);
    }

    [Fact]
    public void Invalidate_and_force_pending_rearm_to_pending()
    {
        var f = OfferFit.CreatePending(OfferId.New());
        f.MarkProduced(50, [], [], null, "SHA256:1:h", At);

        f.Invalidate();
        f.State.ShouldBe(EnrichmentState.Pending);

        f.MarkProduced(50, [], [], null, "SHA256:1:h", At);
        f.ForcePending();
        f.State.ShouldBe(EnrichmentState.Pending);
    }
}
