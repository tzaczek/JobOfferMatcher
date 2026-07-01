using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Tests.Applications;

/// <summary>
/// <see cref="ApplicationTask"/> unit tests (T047): <see cref="ApplicationTask.IsOverdue"/> is
/// (due &lt; now AND not completed); complete/reopen toggle outstanding.
/// </summary>
public sealed class ApplicationTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationTask NewTask(DateTimeOffset? dueAt) =>
        ApplicationTask.Create(OfferId.New(), "Prepare system design", null, dueAt, Now.AddDays(-1)).Value;

    [Fact]
    public void No_due_date_is_never_overdue()
    {
        NewTask(dueAt: null).IsOverdue(Now).ShouldBeFalse();
    }

    [Fact]
    public void Past_due_and_not_completed_is_overdue()
    {
        NewTask(Now.AddHours(-1)).IsOverdue(Now).ShouldBeTrue();
    }

    [Fact]
    public void Future_due_is_not_overdue()
    {
        NewTask(Now.AddHours(1)).IsOverdue(Now).ShouldBeFalse();
    }

    [Fact]
    public void Completed_past_due_is_not_overdue()
    {
        var task = NewTask(Now.AddHours(-1));

        task.Complete(Now);

        task.IsOverdue(Now).ShouldBeFalse();
        task.CompletedAt.ShouldBe(Now);
    }

    [Fact]
    public void Reopen_makes_a_completed_past_due_task_overdue_again()
    {
        var task = NewTask(Now.AddHours(-1));
        task.Complete(Now);

        task.Reopen();

        task.CompletedAt.ShouldBeNull();
        task.IsOverdue(Now).ShouldBeTrue();
    }

    [Fact]
    public void Create_rejects_a_blank_title()
    {
        var result = ApplicationTask.Create(OfferId.New(), "  ", null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("TaskTitleRequired");
    }
}
