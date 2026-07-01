using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Application.Applications;

/// <summary>Header projection for one application (join application + its offer) — the detail's top matter.</summary>
public sealed record ApplicationHeader(
    OfferId OfferId,
    string Title,
    string Company,
    PipelineStageId StageId,
    ApplicationStatus Status,
    ApplicationOutcome? Outcome,
    DateTimeOffset? AppliedAt,
    DateTimeOffset? ClosedAt);

/// <summary>One card on the pipeline board (FR-004/FR-009). Task/interview aggregates derived on read.</summary>
public sealed record ApplicationCard(
    OfferId OfferId,
    string Title,
    string Company,
    PipelineStageId StageId,
    ApplicationStatus Status,
    ApplicationOutcome? Outcome,
    DateTimeOffset? AppliedAt,
    int OutstandingTaskCount,
    int OverdueTaskCount,
    DateTimeOffset? NextInterviewAt);

/// <summary>One board column: a stage plus its active applications.</summary>
public sealed record ApplicationBoardStage(PipelineStageId Id, string Name, int Position, IReadOnlyList<ApplicationCard> Applications);

/// <summary>The whole board: active applications grouped by stage + a closed list (tagged by outcome).</summary>
public sealed record ApplicationBoard(IReadOnlyList<ApplicationBoardStage> Stages, IReadOnlyList<ApplicationCard> Closed);

/// <summary>
/// Persistence port for the <see cref="JobApplication"/> satellite and its five child tables (data-model
/// §3/§4). Single-row gets are tracked (for mutation); reads are no-tracking. The service owns the commit
/// via <c>IUnitOfWork</c>. Stage-change/close/reopen history is appended to the existing <c>offer_event</c>
/// log (via <c>IOfferRepository</c>); this port also reads those application events for the timeline.
/// </summary>
public interface IApplicationRepository
{
    // --- Aggregate root ---
    Task<JobApplication?> GetAsync(OfferId offerId, CancellationToken ct = default);
    Task<bool> ExistsAsync(OfferId offerId, CancellationToken ct = default);
    Task AddAsync(JobApplication application, CancellationToken ct = default);
    void Remove(JobApplication application);

    // --- Read models ---
    Task<ApplicationBoard> GetBoardAsync(DateTimeOffset now, CancellationToken ct = default);
    Task<ApplicationHeader?> GetHeaderAsync(OfferId offerId, CancellationToken ct = default);

    /// <summary>The application's stage-change/close/reopen <c>offer_event</c> rows (for the derived timeline).</summary>
    Task<IReadOnlyList<OfferEvent>> GetApplicationEventsAsync(OfferId offerId, CancellationToken ct = default);

    // --- Notes (append-only) ---
    Task AddNoteAsync(ApplicationNote note, CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationNote>> GetNotesAsync(OfferId offerId, CancellationToken ct = default);
    Task<bool> HasNotesAsync(OfferId offerId, CancellationToken ct = default);

    // --- Tasks ---
    Task AddTaskAsync(ApplicationTask task, CancellationToken ct = default);
    Task<ApplicationTask?> GetTaskAsync(OfferId offerId, ApplicationTaskId id, CancellationToken ct = default);
    void RemoveTask(ApplicationTask task);
    Task<IReadOnlyList<ApplicationTask>> GetTasksAsync(OfferId offerId, CancellationToken ct = default);

    // --- Documents ---
    Task AddDocumentAsync(ApplicationDocument document, CancellationToken ct = default);
    Task<ApplicationDocument?> GetDocumentAsync(OfferId offerId, ApplicationDocumentId id, CancellationToken ct = default);
    void RemoveDocument(ApplicationDocument document);
    Task<IReadOnlyList<ApplicationDocument>> GetDocumentsAsync(OfferId offerId, CancellationToken ct = default);

    // --- Communications ---
    Task AddCommunicationAsync(ApplicationCommunication communication, CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationCommunication>> GetCommunicationsAsync(OfferId offerId, CancellationToken ct = default);

    // --- Interviews ---
    Task AddInterviewAsync(ApplicationInterview interview, CancellationToken ct = default);
    Task<ApplicationInterview?> GetInterviewAsync(OfferId offerId, ApplicationInterviewId id, CancellationToken ct = default);
    void RemoveInterview(ApplicationInterview interview);
    Task<IReadOnlyList<ApplicationInterview>> GetInterviewsAsync(OfferId offerId, CancellationToken ct = default);

    /// <summary>
    /// True when the application has accumulated genuine interview-process history (tasks/docs/comms/
    /// interviews) — the clear-guard. The journal note is deliberately excluded so an applied-with-note
    /// offer stays un-markable exactly as before 005 (FR-016); clearing prefers closing only once real
    /// process history exists (ADR-5).
    /// </summary>
    Task<bool> HasHistoryAsync(OfferId offerId, CancellationToken ct = default);
}
