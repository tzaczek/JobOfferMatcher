using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Enrichment;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>
/// OfferAffinity state-machine unit tests (T017) — an OfferFit twin: produce/fail/rearm mirror
/// OfferFit; score + resembles are only set on produce; failure is terminal only at the retry limit.
/// </summary>
public sealed class OfferAffinityStateTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_row_is_pending_with_no_score()
    {
        var a = OfferAffinity.CreatePending(OfferId.New());
        a.State.ShouldBe(EnrichmentState.Pending);
        a.Score.ShouldBeNull();
        a.Resembles.ShouldBeEmpty();
    }

    [Fact]
    public void Min_applications_gate_is_three()
    {
        OfferAffinity.MinApplications.ShouldBe(3);
    }

    [Fact]
    public void Mark_produced_sets_score_resembles_rationale()
    {
        var a = OfferAffinity.CreatePending(OfferId.New());

        a.MarkProduced(74, ["senior .NET", "remote", "B2B"], "Close to 3 roles you applied to.", "SHA256:1:abc", At);

        a.State.ShouldBe(EnrichmentState.Produced);
        a.Score.ShouldBe(74);
        a.Resembles.ShouldBe(["senior .NET", "remote", "B2B"]);
        a.Rationale.ShouldBe("Close to 3 roles you applied to.");
        a.InputsHash.ShouldBe("SHA256:1:abc");
        a.ProducedAt.ShouldBe(At);
        a.Attempts.ShouldBe(0);
        a.LastError.ShouldBeNull();
    }

    [Fact]
    public void Failures_become_terminal_only_at_the_retry_limit()
    {
        var a = OfferAffinity.CreatePending(OfferId.New());
        a.RecordFailure("1", retryLimit: 2);
        a.State.ShouldBe(EnrichmentState.Pending);
        a.Attempts.ShouldBe(1);
        a.RecordFailure("2", retryLimit: 2);
        a.State.ShouldBe(EnrichmentState.Failed);
        a.LastError.ShouldBe("2");
    }

    [Fact]
    public void Invalidate_and_force_pending_rearm_to_pending()
    {
        var a = OfferAffinity.CreatePending(OfferId.New());
        a.MarkProduced(50, [], null, "SHA256:1:h", At);

        a.Invalidate();
        a.State.ShouldBe(EnrichmentState.Pending);
        a.Attempts.ShouldBe(0);

        a.MarkProduced(50, [], null, "SHA256:1:h", At);
        a.ForcePending();
        a.State.ShouldBe(EnrichmentState.Pending);
    }

    [Fact]
    public void Rearm_only_revives_a_terminal_failed_row()
    {
        var a = OfferAffinity.CreatePending(OfferId.New());
        a.MarkProduced(60, [], null, "SHA256:1:h", At);

        // Produced → Rearm is a no-op (only Failed is re-armed).
        a.Rearm();
        a.State.ShouldBe(EnrichmentState.Produced);

        a.RecordFailure("x", retryLimit: 1);
        a.State.ShouldBe(EnrichmentState.Failed);
        a.Rearm();
        a.State.ShouldBe(EnrichmentState.Pending);
        a.Attempts.ShouldBe(0);
    }
}
