using JobOfferMatcher.Domain.Scans;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// Outcome of one source collection pass, reported to the orchestrator
/// (contracts/ijobsource-port.md). A <see cref="ScanOutcome.Partial"/> with
/// <see cref="IncompleteReason.ChallengeDetected"/> is the "source blocked" signal the
/// orchestrator escalates to manual login iff an interactive-browser adapter exists (FR-040).
/// </summary>
public sealed record CollectionResult(ScanOutcome Outcome, IncompleteReason? Reason, int CollectedCount)
{
    public bool SourceBlocked => Outcome != ScanOutcome.Complete && Reason == IncompleteReason.ChallengeDetected;

    public static CollectionResult Complete(int count) => new(ScanOutcome.Complete, null, count);

    public static CollectionResult Partial(IncompleteReason reason, int count) =>
        new(ScanOutcome.Partial, reason, count);

    public static CollectionResult Failed(IncompleteReason reason, int count) =>
        new(ScanOutcome.Failed, reason, count);
}
