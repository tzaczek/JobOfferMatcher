using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Enrichment;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>OfferEnrichment state-machine unit tests (T014): retry→Failed at the limit; Invalidate re-arms but keeps payload.</summary>
public sealed class OfferEnrichmentStateTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_row_is_pending()
    {
        var e = OfferEnrichment.CreatePending(OfferId.New());
        e.State.ShouldBe(EnrichmentState.Pending);
        e.Attempts.ShouldBe(0);
        e.Summary.ShouldBeNull();
    }

    [Fact]
    public void Mark_produced_sets_payload_and_resets_attempts()
    {
        var e = OfferEnrichment.CreatePending(OfferId.New());
        e.RecordFailure("boom", retryLimit: 3);

        e.MarkProduced("A concise summary.", ["C#", ".NET"], "SHA256:1:abc", At);

        e.State.ShouldBe(EnrichmentState.Produced);
        e.Summary.ShouldBe("A concise summary.");
        e.KeySkills.ShouldBe(["C#", ".NET"]);
        e.InputsHash.ShouldBe("SHA256:1:abc");
        e.ProducedAt.ShouldBe(At);
        e.Attempts.ShouldBe(0);
        e.LastError.ShouldBeNull();
    }

    [Fact]
    public void Failures_become_terminal_only_at_the_retry_limit()
    {
        var e = OfferEnrichment.CreatePending(OfferId.New());

        e.RecordFailure("1", retryLimit: 3);
        e.State.ShouldBe(EnrichmentState.Pending);
        e.RecordFailure("2", retryLimit: 3);
        e.State.ShouldBe(EnrichmentState.Pending);
        e.RecordFailure("3", retryLimit: 3);
        e.State.ShouldBe(EnrichmentState.Failed);
        e.Attempts.ShouldBe(3);
        e.LastError.ShouldBe("3");
    }

    [Fact]
    public void Invalidate_rearms_to_pending_but_keeps_the_last_payload_internally()
    {
        var e = OfferEnrichment.CreatePending(OfferId.New());
        e.MarkProduced("kept summary", ["C#"], "SHA256:1:old", At);

        e.Invalidate();

        e.State.ShouldBe(EnrichmentState.Pending);
        e.Attempts.ShouldBe(0);
        e.Summary.ShouldBe("kept summary"); // payload retained as a read-path backstop (no longer current)
    }

    [Fact]
    public void Rearm_only_revives_a_failed_row()
    {
        var e = OfferEnrichment.CreatePending(OfferId.New());
        e.MarkProduced("s", [], "SHA256:1:h", At);

        e.Rearm();
        e.State.ShouldBe(EnrichmentState.Produced); // produced rows are untouched by failed-rerun

        e.RecordFailure("x", retryLimit: 1);
        e.State.ShouldBe(EnrichmentState.Failed);
        e.Rearm();
        e.State.ShouldBe(EnrichmentState.Pending);
    }
}
