using JobOfferMatcher.Domain.Scans;

namespace JobOfferMatcher.Application.Scheduling;

/// <summary>The poll-tick's decision: whether to run now, with which trigger, and the window to record.</summary>
public sealed record CatchUpDecision(bool ShouldRun, TriggerType Trigger, DateTimeOffset? AdvanceTo)
{
    public static readonly CatchUpDecision NoRun = new(false, TriggerType.Scheduled, null);
}

/// <summary>
/// Pure catch-up policy (research §3), so it is unit-tested deterministically: one tick computes the
/// previous cron occurrence; if we haven't run it yet, run ONCE (collapsing all missed windows into
/// that occurrence). First run is seeded as <see cref="TriggerType.Initial"/> — never a spurious
/// CatchUp for a pre-install window.
/// </summary>
public static class CatchUpPolicy
{
    public static CatchUpDecision Decide(bool enabled, DateTimeOffset? lastRunUtc, DateTimeOffset? previousOccurrence)
    {
        if (!enabled || previousOccurrence is null)
        {
            return CatchUpDecision.NoRun;
        }

        var prev = previousOccurrence.Value;

        if (lastRunUtc is null)
        {
            // Fresh install: run once as Initial and seed — do NOT fabricate a CatchUp.
            return new CatchUpDecision(true, TriggerType.Initial, prev);
        }

        if (lastRunUtc < prev)
        {
            // One catch-up for the most-recent missed window; advancing to prev collapses the rest.
            return new CatchUpDecision(true, TriggerType.CatchUp, prev);
        }

        return CatchUpDecision.NoRun;
    }
}
