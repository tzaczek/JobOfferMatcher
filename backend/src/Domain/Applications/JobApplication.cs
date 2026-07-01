using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Applications;

/// <summary>
/// A first-class application the user drives through a configurable interview pipeline (data-model §3).
/// A satellite aggregate keyed by <see cref="OfferId"/> (1:1 with an offer, like <c>OfferFit</c> /
/// <c>tailored_cv</c>): the lean <c>Offer</c> root and its <c>Applied</c> flag are the entry gate; this
/// aggregate carries the lifecycle. <see cref="CurrentStageId"/> references a user-editable
/// <c>pipeline_stage</c>; <see cref="Status"/> + <see cref="Outcome"/> are a SEPARATE fixed dimension
/// (Principle III — invalid transitions are rejected in the aggregate via <see cref="Result"/>). Stage
/// changes / close / reopen are appended to the existing <c>offer_event</c> log by the service.
/// </summary>
public sealed class JobApplication
{
    public OfferId OfferId { get; private set; }
    public PipelineStageId CurrentStageId { get; private set; }
    public ApplicationStatus Status { get; private set; }
    public ApplicationOutcome? Outcome { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Expected-failure codes (map to HTTP at the Web boundary — ResultExtensions.StatusFor).
    public static readonly Error AlreadyClosed = new("ApplicationAlreadyClosed", "The application is already closed.");
    public static readonly Error ClosedCannotMove = new("ApplicationClosed", "Reopen the application before moving it between stages.");
    public static readonly Error NotClosed = new("ApplicationNotClosed", "Only a closed application can be reopened.");

    private JobApplication()
    {
        // EF Core materialization.
    }

    /// <summary>Create a new <see cref="ApplicationStatus.Active"/> application at the first pipeline stage.</summary>
    public static JobApplication Create(OfferId offerId, PipelineStageId firstStageId, DateTimeOffset? appliedAt, DateTimeOffset now) =>
        new()
        {
            OfferId = offerId,
            CurrentStageId = firstStageId,
            Status = ApplicationStatus.Active,
            Outcome = null,
            AppliedAt = appliedAt,
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>
    /// Move to another pipeline stage. Free movement while <see cref="ApplicationStatus.Active"/>; a
    /// closed application must be reopened first. The service confirms the stage exists before calling.
    /// </summary>
    public Result MoveToStage(PipelineStageId stageId, DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Active)
        {
            return ClosedCannotMove;
        }

        CurrentStageId = stageId;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Close the application with a fixed outcome. Rejects a second close.</summary>
    public Result Close(ApplicationOutcome outcome, DateTimeOffset now)
    {
        if (Status == ApplicationStatus.Closed)
        {
            return AlreadyClosed;
        }

        Status = ApplicationStatus.Closed;
        Outcome = outcome;
        ClosedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Reopen a closed application (stage retained); clears the outcome/closed-at. Rejects if active.</summary>
    public Result Reopen(DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Closed)
        {
            return NotClosed;
        }

        Status = ApplicationStatus.Active;
        Outcome = null;
        ClosedAt = null;
        UpdatedAt = now;
        return Result.Success();
    }
}
