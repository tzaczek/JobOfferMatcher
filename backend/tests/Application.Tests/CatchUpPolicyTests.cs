using JobOfferMatcher.Application.Scheduling;
using JobOfferMatcher.Domain.Scans;
using Microsoft.Extensions.Time.Testing;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// Unit tests (T037) for the poll-tick catch-up policy with an injected <see cref="TimeProvider"/>:
/// single catch-up after sleep/resume, no replay of multiple missed windows, and correct first-run
/// seeding (no spurious CatchUp).
/// </summary>
public sealed class CatchUpPolicyTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 6, 28, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void First_run_is_seeded_as_Initial_never_a_spurious_catchup()
    {
        var prev = _time.GetUtcNow().AddHours(-1); // most recent cron occurrence

        var decision = CatchUpPolicy.Decide(enabled: true, lastRunUtc: null, previousOccurrence: prev);

        decision.ShouldRun.ShouldBeTrue();
        decision.Trigger.ShouldBe(TriggerType.Initial);
        decision.Trigger.ShouldNotBe(TriggerType.CatchUp);
        decision.AdvanceTo.ShouldBe(prev);
    }

    [Fact]
    public void No_run_when_the_window_was_already_run()
    {
        var prev = _time.GetUtcNow().AddHours(-1);

        var decision = CatchUpPolicy.Decide(enabled: true, lastRunUtc: prev, previousOccurrence: prev);

        decision.ShouldRun.ShouldBeFalse();
    }

    [Fact]
    public void Missed_window_after_sleep_resume_runs_one_catchup()
    {
        var prev = _time.GetUtcNow().AddHours(-1);
        var lastRun = prev.AddHours(-7); // ran at the previous scheduled window

        var decision = CatchUpPolicy.Decide(enabled: true, lastRunUtc: lastRun, previousOccurrence: prev);

        decision.ShouldRun.ShouldBeTrue();
        decision.Trigger.ShouldBe(TriggerType.CatchUp);
        decision.AdvanceTo.ShouldBe(prev); // advance to most-recent, not the missed one
    }

    [Fact]
    public void Multiple_missed_windows_collapse_into_a_single_catchup()
    {
        var prev = _time.GetUtcNow().AddHours(-1);
        var lastRun = prev.AddDays(-5); // machine was off for days

        var decision = CatchUpPolicy.Decide(enabled: true, lastRunUtc: lastRun, previousOccurrence: prev);

        decision.ShouldRun.ShouldBeTrue();
        decision.Trigger.ShouldBe(TriggerType.CatchUp);
        // A single decision advancing straight to prev — never one run per missed window.
        decision.AdvanceTo.ShouldBe(prev);
    }

    [Fact]
    public void Disabled_schedule_never_runs()
    {
        var prev = _time.GetUtcNow().AddHours(-1);
        CatchUpPolicy.Decide(enabled: false, lastRunUtc: null, previousOccurrence: prev).ShouldRun.ShouldBeFalse();
    }

    [Fact]
    public void No_occurrence_means_no_run()
    {
        CatchUpPolicy.Decide(enabled: true, lastRunUtc: null, previousOccurrence: null).ShouldRun.ShouldBeFalse();
    }
}
