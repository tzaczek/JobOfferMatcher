using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Applications;

/// <summary>
/// One interview in the process (data-model §3, FR-011): a free-text kind (phone screen, on-site…),
/// an optional scheduled time, interviewer, outcome, and notes. Mutable: record outcome / edit.
/// <see cref="IsUpcoming"/> is pure and derived (scheduled in the future). Carries <see cref="OfferId"/>.
/// </summary>
public sealed class ApplicationInterview
{
    public const int MaxKindLength = 80;
    public const int MaxInterviewerLength = 200;
    public const int MaxOutcomeLength = 2000;
    public const int MaxNotesLength = 4000;

    public ApplicationInterviewId Id { get; private set; }
    public OfferId OfferId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public DateTimeOffset? ScheduledAt { get; private set; }
    public string? Interviewer { get; private set; }
    public string? Outcome { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ApplicationInterview()
    {
        // EF Core materialization.
    }

    public static Result<ApplicationInterview> Create(
        OfferId offerId,
        string kind,
        DateTimeOffset? scheduledAt,
        string? interviewer,
        string? notes,
        DateTimeOffset now)
    {
        var validated = Validate(kind, interviewer, notes, outcome: null);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var (cleanKind, cleanInterviewer, cleanNotes, _) = validated.Value;
        return new ApplicationInterview
        {
            Id = ApplicationInterviewId.New(),
            OfferId = offerId,
            Kind = cleanKind,
            ScheduledAt = scheduledAt,
            Interviewer = cleanInterviewer,
            Outcome = null,
            Notes = cleanNotes,
            CreatedAt = now,
        };
    }

    /// <summary>Record the outcome / edit any field (used to add the outcome after the interview).</summary>
    public Result Edit(string kind, DateTimeOffset? scheduledAt, string? interviewer, string? outcome, string? notes)
    {
        var validated = Validate(kind, interviewer, notes, outcome);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var (cleanKind, cleanInterviewer, cleanNotes, cleanOutcome) = validated.Value;
        Kind = cleanKind;
        ScheduledAt = scheduledAt;
        Interviewer = cleanInterviewer;
        Outcome = cleanOutcome;
        Notes = cleanNotes;
        return Result.Success();
    }

    /// <summary>Upcoming = has a scheduled time in the future (derived, never stored).</summary>
    public bool IsUpcoming(DateTimeOffset now) => ScheduledAt is { } at && at > now;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static Result<(string Kind, string? Interviewer, string? Notes, string? Outcome)> Validate(
        string kind, string? interviewer, string? notes, string? outcome)
    {
        var trimmedKind = string.IsNullOrWhiteSpace(kind) ? string.Empty : kind.Trim();
        if (trimmedKind.Length == 0)
        {
            return new Error("InterviewKindRequired", "An interview kind is required.");
        }

        if (trimmedKind.Length > MaxKindLength)
        {
            return new Error("InterviewKindTooLong", $"An interview kind cannot exceed {MaxKindLength} characters.");
        }

        var cleanInterviewer = Clean(interviewer);
        if (cleanInterviewer is { Length: > MaxInterviewerLength })
        {
            return new Error("InterviewerTooLong", $"An interviewer name cannot exceed {MaxInterviewerLength} characters.");
        }

        var cleanOutcome = Clean(outcome);
        if (cleanOutcome is { Length: > MaxOutcomeLength })
        {
            return new Error("InterviewOutcomeTooLong", $"An interview outcome cannot exceed {MaxOutcomeLength} characters.");
        }

        var cleanNotes = Clean(notes);
        if (cleanNotes is { Length: > MaxNotesLength })
        {
            return new Error("InterviewNotesTooLong", $"Interview notes cannot exceed {MaxNotesLength} characters.");
        }

        return (trimmedKind, cleanInterviewer, cleanNotes, cleanOutcome);
    }
}
