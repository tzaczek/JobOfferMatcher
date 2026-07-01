using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Tests.Applications;

/// <summary>
/// <see cref="JobApplication"/> state-machine unit tests (T026): free stage movement while active;
/// close requires an outcome and rejects a double-close; reopen requires a closed application.
/// </summary>
public sealed class JobApplicationTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private static JobApplication NewApplication(out PipelineStageId first)
    {
        first = PipelineStageId.New();
        return JobApplication.Create(OfferId.New(), first, At, At);
    }

    [Fact]
    public void Create_starts_active_at_the_first_stage_with_no_outcome()
    {
        var app = NewApplication(out var first);

        app.Status.ShouldBe(ApplicationStatus.Active);
        app.CurrentStageId.ShouldBe(first);
        app.Outcome.ShouldBeNull();
        app.ClosedAt.ShouldBeNull();
        app.AppliedAt.ShouldBe(At);
    }

    [Fact]
    public void MoveToStage_is_free_while_active()
    {
        var app = NewApplication(out _);
        var next = PipelineStageId.New();

        var result = app.MoveToStage(next, At.AddHours(1));

        result.IsSuccess.ShouldBeTrue();
        app.CurrentStageId.ShouldBe(next);
    }

    [Fact]
    public void MoveToStage_is_rejected_while_closed()
    {
        var app = NewApplication(out var first);
        app.Close(ApplicationOutcome.Rejected, At.AddHours(1));

        var result = app.MoveToStage(PipelineStageId.New(), At.AddHours(2));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ApplicationClosed");
        app.CurrentStageId.ShouldBe(first); // unchanged
    }

    [Fact]
    public void Close_sets_outcome_and_closed_at()
    {
        var app = NewApplication(out _);
        var closedAt = At.AddDays(3);

        var result = app.Close(ApplicationOutcome.Accepted, closedAt);

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Closed);
        app.Outcome.ShouldBe(ApplicationOutcome.Accepted);
        app.ClosedAt.ShouldBe(closedAt);
    }

    [Fact]
    public void Close_twice_is_rejected()
    {
        var app = NewApplication(out _);
        app.Close(ApplicationOutcome.Withdrawn, At.AddHours(1));

        var result = app.Close(ApplicationOutcome.Rejected, At.AddHours(2));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ApplicationAlreadyClosed");
        app.Outcome.ShouldBe(ApplicationOutcome.Withdrawn); // first outcome retained
    }

    [Fact]
    public void Reopen_restores_active_and_clears_outcome()
    {
        var app = NewApplication(out var first);
        app.Close(ApplicationOutcome.NoResponse, At.AddDays(1));

        var result = app.Reopen(At.AddDays(2));

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Active);
        app.Outcome.ShouldBeNull();
        app.ClosedAt.ShouldBeNull();
        app.CurrentStageId.ShouldBe(first); // stage retained across close/reopen
    }

    [Fact]
    public void Reopen_while_active_is_rejected()
    {
        var app = NewApplication(out _);

        var result = app.Reopen(At.AddHours(1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ApplicationNotClosed");
    }
}
