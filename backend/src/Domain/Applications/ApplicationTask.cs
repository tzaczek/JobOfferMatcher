using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Applications;

/// <summary>
/// A to-do for an application (e.g. "prepare system-design", "send thank-you"), optionally with a due
/// date (data-model §3, FR-008). Mutable: complete/reopen/edit. <see cref="IsOverdue"/> is pure and
/// derived on read (past-due AND not completed) — never stored. Carries <see cref="OfferId"/>.
/// </summary>
public sealed class ApplicationTask
{
    /// <summary>Longest task title the Domain accepts.</summary>
    public const int MaxTitleLength = 300;

    public ApplicationTaskId Id { get; private set; }
    public OfferId OfferId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ApplicationTask()
    {
        // EF Core materialization.
    }

    public static Result<ApplicationTask> Create(OfferId offerId, string title, string? description, DateTimeOffset? dueAt, DateTimeOffset now)
    {
        var validated = ValidateTitle(title);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        return new ApplicationTask
        {
            Id = ApplicationTaskId.New(),
            OfferId = offerId,
            Title = validated.Value,
            Description = Clean(description),
            DueAt = dueAt,
            CompletedAt = null,
            CreatedAt = now,
        };
    }

    public Result Edit(string title, string? description, DateTimeOffset? dueAt)
    {
        var validated = ValidateTitle(title);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        Title = validated.Value;
        Description = Clean(description);
        DueAt = dueAt;
        return Result.Success();
    }

    /// <summary>Mark done (idempotent — a later completion keeps the first stamp).</summary>
    public void Complete(DateTimeOffset now) => CompletedAt ??= now;

    /// <summary>Re-open a completed task (back to outstanding).</summary>
    public void Reopen() => CompletedAt = null;

    /// <summary>Overdue = has a due date in the past AND is not completed (derived, never stored).</summary>
    public bool IsOverdue(DateTimeOffset now) => DueAt is { } due && CompletedAt is null && due < now;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static Result<string> ValidateTitle(string title)
    {
        var trimmed = string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
        if (trimmed.Length == 0)
        {
            return new Error("TaskTitleRequired", "A task title is required.");
        }

        if (trimmed.Length > MaxTitleLength)
        {
            return new Error("TaskTitleTooLong", $"A task title cannot exceed {MaxTitleLength} characters.");
        }

        return trimmed;
    }
}
