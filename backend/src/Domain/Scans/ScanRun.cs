using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Scans;

/// <summary>
/// Immutable-once-finalized record of one scan (data-model §ScanRun). Keyed
/// <c>UNIQUE(window_utc, trigger)</c> so a crashed/duplicate catch-up cannot double-fire
/// (research §3). Outcome gates disappearance reconciliation (FR-015).
/// </summary>
public sealed class ScanRun
{
    private IReadOnlyList<SourceId> _sourceIds = [];

    public ScanRunId Id { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public DateTimeOffset? WindowUtc { get; private set; }
    public TriggerType Trigger { get; private set; }
    public IReadOnlyList<SourceId> SourceIds => _sourceIds;
    public ScanCounts Counts { get; private set; } = ScanCounts.Zero;
    public ScanOutcome Outcome { get; private set; } = ScanOutcome.Failed;
    public IncompleteReason? IncompleteReason { get; private set; }

    public bool IsFinished => FinishedAt is not null;

    private ScanRun()
    {
        // EF Core materialization.
    }

    public static ScanRun Start(
        ScanRunId id,
        DateTimeOffset startedAt,
        TriggerType trigger,
        IReadOnlyList<SourceId> sourceIds,
        DateTimeOffset? windowUtc = null) =>
        new()
        {
            Id = id,
            StartedAt = startedAt,
            Trigger = trigger,
            _sourceIds = [.. sourceIds],
            WindowUtc = windowUtc,
        };

    public void Finish(DateTimeOffset finishedAt, ScanCounts counts, ScanOutcome outcome, IncompleteReason? reason = null)
    {
        FinishedAt = finishedAt;
        Counts = counts;
        Outcome = outcome;
        IncompleteReason = reason;
    }
}
