using System.Text.Json;
using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Application.Applications;

/// <summary>Expected-failure codes for application tracking (map to HTTP at the Web boundary).</summary>
public static class ApplicationErrors
{
    public static readonly Error NotFound = new("ApplicationNotFound", "No application exists for this offer.");
    public static readonly Error UnknownStage = new("UnknownStage", "The target pipeline stage does not exist.");
    public static readonly Error InvalidOutcome = new("InvalidOutcome", "Unknown application outcome.");
    public static readonly Error InvalidDirection = new("InvalidCommunicationDirection", "Unknown communication direction.");
    public static readonly Error ConfirmationRequired = new("ConfirmationRequired", "Deleting an application is permanent — confirm to proceed.");
    public static readonly Error FileTooLarge = new("FileTooLarge", $"Attachments cannot exceed {ApplicationTrackingService.MaxDocumentBytes / (1024 * 1024)} MB.");
    public static readonly Error EmptyDocument = new("EmptyDocument", "The uploaded file is empty.");
    public static readonly Error DocumentNotFound = new("ApplicationDocumentNotFound", "The document was not found.");
    public static readonly Error TaskNotFound = new("ApplicationTaskNotFound", "The task was not found.");
    public static readonly Error InterviewNotFound = new("ApplicationInterviewNotFound", "The interview was not found.");
}

/// <summary>
/// The application-tracking use cases (contracts/applications-api.md): the pipeline board, one
/// application's detail + derived timeline, the lifecycle (move stage / close / reopen / permanent
/// delete), and the notes/tasks/documents/communications/interviews children. Stage-change/close/reopen
/// append to the existing append-only <c>offer_event</c> log (via <c>IOfferRepository</c>); the timeline
/// is a derived union (no timeline table). Every write path defers during a restore via
/// <see cref="MaintenanceGate"/> and the service owns the commit via <c>IUnitOfWork</c> (UI-local, no
/// external call — Principle IV).
/// </summary>
public sealed class ApplicationTrackingService(
    IApplicationRepository applications,
    IPipelineStageRepository stages,
    IOfferRepository offers,
    IApplicationDocumentFileStore documentStore,
    IUnitOfWork unitOfWork,
    MaintenanceGate maintenance,
    TimeProvider time)
{
    /// <summary>Attachment size cap (clarification #4): 50 MB = 52,428,800 bytes.</summary>
    public const long MaxDocumentBytes = 50L * 1024 * 1024;

    // --- Board + detail (read) ---

    public async Task<ApplicationBoardDto> GetBoardAsync(CancellationToken ct = default)
    {
        var board = await applications.GetBoardAsync(time.GetUtcNow(), ct);
        return new ApplicationBoardDto(
            board.Stages
                .Select(s => new ApplicationBoardStageDto(
                    s.Id.ToString(), s.Name, s.Position, s.Applications.Select(ToCardDto).ToList()))
                .ToList(),
            board.Closed.Select(ToCardDto).ToList());
    }

    public async Task<Result<ApplicationDetailDto>> GetAsync(OfferId offerId, CancellationToken ct = default)
    {
        var header = await applications.GetHeaderAsync(offerId, ct);
        if (header is null)
        {
            return ApplicationErrors.NotFound;
        }

        var now = time.GetUtcNow();
        var notes = await applications.GetNotesAsync(offerId, ct);
        var tasks = await applications.GetTasksAsync(offerId, ct);
        var documents = await applications.GetDocumentsAsync(offerId, ct);
        var communications = await applications.GetCommunicationsAsync(offerId, ct);
        var interviews = await applications.GetInterviewsAsync(offerId, ct);
        var timeline = await BuildTimelineAsync(offerId, notes, tasks, documents, communications, interviews, ct);

        return new ApplicationDetailDto(
            header.OfferId.ToString(),
            header.Title,
            header.Company,
            header.StageId.ToString(),
            Camel(header.Status.ToString()),
            header.Outcome is { } outcome ? Camel(outcome.ToString()) : null,
            header.AppliedAt,
            header.ClosedAt,
            timeline,
            notes.Select(ToNoteDto).ToList(),
            tasks.Select(t => ToTaskDto(t, now)).ToList(),
            documents.Select(ToDocumentDto).ToList(),
            communications.Select(ToCommunicationDto).ToList(),
            interviews.Select(i => ToInterviewDto(i, now)).ToList());
    }

    // --- Lifecycle (US1) ---

    public async Task<Result> MoveStageAsync(OfferId offerId, PipelineStageId stageId, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        var application = await applications.GetAsync(offerId, ct);
        if (application is null)
        {
            return ApplicationErrors.NotFound;
        }

        var stage = await stages.GetAsync(stageId, ct);
        if (stage is null)
        {
            return ApplicationErrors.UnknownStage;
        }

        var now = time.GetUtcNow();
        var moved = application.MoveToStage(stageId, now);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        var payload = JsonSerializer.Serialize(new { stage = stage.Name });
        await offers.AddEventAsync(OfferEvent.Create(offerId, now, OfferEventType.ApplicationStageChanged, payload), ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> CloseAsync(OfferId offerId, string outcomeRaw, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        if (!TryParseOutcome(outcomeRaw, out var outcome))
        {
            return ApplicationErrors.InvalidOutcome;
        }

        var application = await applications.GetAsync(offerId, ct);
        if (application is null)
        {
            return ApplicationErrors.NotFound;
        }

        var now = time.GetUtcNow();
        var closed = application.Close(outcome, now);
        if (closed.IsFailure)
        {
            return closed.Error;
        }

        var payload = JsonSerializer.Serialize(new { outcome = Camel(outcome.ToString()) });
        await offers.AddEventAsync(OfferEvent.Create(offerId, now, OfferEventType.ApplicationClosed, payload), ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ReopenAsync(OfferId offerId, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        var application = await applications.GetAsync(offerId, ct);
        if (application is null)
        {
            return ApplicationErrors.NotFound;
        }

        var now = time.GetUtcNow();
        var reopened = application.Reopen(now);
        if (reopened.IsFailure)
        {
            return reopened.Error;
        }

        await offers.AddEventAsync(OfferEvent.Create(offerId, now, OfferEventType.ApplicationReopened), ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Permanently delete the application subtree (R8). Also clears the offer's applied flag so the
    /// startup backfill won't resurrect it. Recoverable only from a prior backup — requires confirmation.
    /// </summary>
    public async Task<Result> DeleteAsync(OfferId offerId, bool confirm, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        if (!confirm)
        {
            return ApplicationErrors.ConfirmationRequired;
        }

        var application = await applications.GetAsync(offerId, ct);
        if (application is null)
        {
            return ApplicationErrors.NotFound;
        }

        // Capture the stored file names before the rows cascade away, so the flat files can be removed.
        var documents = await applications.GetDocumentsAsync(offerId, ct);

        applications.Remove(application); // FK cascade removes the whole child subtree.

        var offer = await offers.GetByIdAsync(offerId, ct);
        if (offer is not null && offer.ClearApplied())
        {
            await offers.AddEventAsync(OfferEvent.Create(offerId, time.GetUtcNow(), OfferEventType.ApplicationCleared), ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var document in documents)
        {
            documentStore.Delete(document.StoredFileName);
        }

        return Result.Success();
    }

    // --- Notes (US2) ---

    public async Task<Result<NoteDto>> AddNoteAsync(OfferId offerId, string body, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        if (!await applications.ExistsAsync(offerId, ct))
        {
            return ApplicationErrors.NotFound;
        }

        var note = ApplicationNote.Create(offerId, body, time.GetUtcNow());
        if (note.IsFailure)
        {
            return note.Error;
        }

        await applications.AddNoteAsync(note.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ToNoteDto(note.Value);
    }

    // --- Tasks (US3) ---

    public async Task<Result<TaskDto>> AddTaskAsync(OfferId offerId, string title, string? description, DateTimeOffset? dueAt, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        if (!await applications.ExistsAsync(offerId, ct))
        {
            return ApplicationErrors.NotFound;
        }

        var now = time.GetUtcNow();
        var task = ApplicationTask.Create(offerId, title, description, dueAt, now);
        if (task.IsFailure)
        {
            return task.Error;
        }

        await applications.AddTaskAsync(task.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ToTaskDto(task.Value, now);
    }

    /// <summary>
    /// Partial update. A non-null <c>Title</c> triggers a full field edit (title/description/dueAt replace);
    /// a non-null <c>Completed</c> toggles completion. "Mark done" (only <c>Completed</c>) keeps the other
    /// fields; "edit" (with <c>Title</c>) replaces them.
    /// </summary>
    public async Task<Result> UpdateTaskAsync(OfferId offerId, ApplicationTaskId taskId, UpdateTaskRequest request, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        var task = await applications.GetTaskAsync(offerId, taskId, ct);
        if (task is null)
        {
            return ApplicationErrors.TaskNotFound;
        }

        if (request.Title is not null)
        {
            var edited = task.Edit(request.Title, request.Description, request.DueAt);
            if (edited.IsFailure)
            {
                return edited.Error;
            }
        }

        if (request.Completed is { } completed)
        {
            if (completed)
            {
                task.Complete(time.GetUtcNow());
            }
            else
            {
                task.Reopen();
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveTaskAsync(OfferId offerId, ApplicationTaskId taskId, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        var task = await applications.GetTaskAsync(offerId, taskId, ct);
        if (task is null)
        {
            return ApplicationErrors.TaskNotFound;
        }

        applications.RemoveTask(task);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    // --- Documents (US4) ---

    public async Task<Result<DocumentDto>> AddDocumentAsync(
        OfferId offerId, string originalFileName, string? contentType, byte[] content, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        if (content.LongLength == 0)
        {
            return ApplicationErrors.EmptyDocument;
        }

        if (content.LongLength > MaxDocumentBytes)
        {
            return ApplicationErrors.FileTooLarge;
        }

        if (!await applications.ExistsAsync(offerId, ct))
        {
            return ApplicationErrors.NotFound;
        }

        var safeName = Path.GetFileName(originalFileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "attachment";
        }

        var id = ApplicationDocumentId.New();
        var storedName = await documentStore.SaveAsync(id, safeName, content, ct);
        var document = ApplicationDocument.Create(id, offerId, storedName, safeName, contentType, content.LongLength, time.GetUtcNow());
        await applications.AddDocumentAsync(document, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ToDocumentDto(document);
    }

    public async Task<Result<ApplicationDocumentDownload>> GetDocumentDownloadAsync(OfferId offerId, ApplicationDocumentId docId, CancellationToken ct = default)
    {
        var document = await applications.GetDocumentAsync(offerId, docId, ct);
        if (document is null)
        {
            return ApplicationErrors.DocumentNotFound;
        }

        var path = documentStore.GetAbsolutePath(document.StoredFileName);
        if (!File.Exists(path))
        {
            return ApplicationErrors.DocumentNotFound;
        }

        return new ApplicationDocumentDownload(path, document.OriginalFileName, document.ContentType ?? "application/octet-stream");
    }

    public async Task<Result> RemoveDocumentAsync(OfferId offerId, ApplicationDocumentId docId, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        var document = await applications.GetDocumentAsync(offerId, docId, ct);
        if (document is null)
        {
            return ApplicationErrors.DocumentNotFound;
        }

        applications.RemoveDocument(document);
        await unitOfWork.SaveChangesAsync(ct);
        documentStore.Delete(document.StoredFileName);
        return Result.Success();
    }

    // --- Communications + interviews (US5) ---

    public async Task<Result<CommunicationDto>> AddCommunicationAsync(
        OfferId offerId, DateTimeOffset occurredAt, string directionRaw, string channel, string summary, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        if (!TryParseDirection(directionRaw, out var direction))
        {
            return ApplicationErrors.InvalidDirection;
        }

        if (!await applications.ExistsAsync(offerId, ct))
        {
            return ApplicationErrors.NotFound;
        }

        var communication = ApplicationCommunication.Create(offerId, occurredAt, direction, channel, summary, time.GetUtcNow());
        if (communication.IsFailure)
        {
            return communication.Error;
        }

        await applications.AddCommunicationAsync(communication.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ToCommunicationDto(communication.Value);
    }

    public async Task<Result<InterviewDto>> AddInterviewAsync(
        OfferId offerId, string kind, DateTimeOffset? scheduledAt, string? interviewer, string? notes, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        if (!await applications.ExistsAsync(offerId, ct))
        {
            return ApplicationErrors.NotFound;
        }

        var now = time.GetUtcNow();
        var interview = ApplicationInterview.Create(offerId, kind, scheduledAt, interviewer, notes, now);
        if (interview.IsFailure)
        {
            return interview.Error;
        }

        await applications.AddInterviewAsync(interview.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ToInterviewDto(interview.Value, now);
    }

    /// <summary>Record an outcome / edit an interview. Null fields keep the existing value (partial).</summary>
    public async Task<Result> UpdateInterviewAsync(OfferId offerId, ApplicationInterviewId id, UpdateInterviewRequest request, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        var interview = await applications.GetInterviewAsync(offerId, id, ct);
        if (interview is null)
        {
            return ApplicationErrors.InterviewNotFound;
        }

        var kind = string.IsNullOrWhiteSpace(request.Kind) ? interview.Kind : request.Kind;
        var edited = interview.Edit(
            kind,
            request.ScheduledAt ?? interview.ScheduledAt,
            request.Interviewer ?? interview.Interviewer,
            request.Outcome ?? interview.Outcome,
            request.Notes ?? interview.Notes);
        if (edited.IsFailure)
        {
            return edited.Error;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveInterviewAsync(OfferId offerId, ApplicationInterviewId id, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        var interview = await applications.GetInterviewAsync(offerId, id, ct);
        if (interview is null)
        {
            return ApplicationErrors.InterviewNotFound;
        }

        applications.RemoveInterview(interview);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    // --- Derived timeline (FR-007 — a union, no timeline table) ---

    private async Task<IReadOnlyList<TimelineEntryDto>> BuildTimelineAsync(
        OfferId offerId,
        IReadOnlyList<ApplicationNote> notes,
        IReadOnlyList<ApplicationTask> tasks,
        IReadOnlyList<ApplicationDocument> documents,
        IReadOnlyList<ApplicationCommunication> communications,
        IReadOnlyList<ApplicationInterview> interviews,
        CancellationToken ct)
    {
        var events = await applications.GetApplicationEventsAsync(offerId, ct);
        var entries = new List<TimelineEntryDto>();

        foreach (var e in events)
        {
            var (kind, title, detail) = DescribeEvent(e);
            entries.Add(new TimelineEntryDto(e.OccurredAt, kind, title, detail));
        }

        foreach (var n in notes)
        {
            entries.Add(new TimelineEntryDto(n.CreatedAt, "note", "Note added", n.Body));
        }

        foreach (var t in tasks)
        {
            entries.Add(new TimelineEntryDto(t.CreatedAt, "task", $"Task added: {t.Title}", t.DueAt is { } due ? $"Due {due:yyyy-MM-dd}" : null));
            if (t.CompletedAt is { } completedAt)
            {
                entries.Add(new TimelineEntryDto(completedAt, "taskDone", $"Task completed: {t.Title}", null));
            }
        }

        foreach (var d in documents)
        {
            entries.Add(new TimelineEntryDto(d.AddedAt, "document", $"Document attached: {d.OriginalFileName}", null));
        }

        foreach (var c in communications)
        {
            entries.Add(new TimelineEntryDto(c.OccurredAt, "communication", $"{Camel(c.Direction.ToString())} · {c.Channel}", c.Summary));
        }

        foreach (var i in interviews)
        {
            entries.Add(new TimelineEntryDto(i.ScheduledAt ?? i.CreatedAt, "interview", $"Interview: {i.Kind}", i.Outcome));
        }

        return entries.OrderBy(e => e.OccurredAt).ToList();
    }

    private static (string Kind, string Title, string? Detail) DescribeEvent(OfferEvent e) => e.Type switch
    {
        OfferEventType.ApplicationStageChanged => ("stageChanged", ReadField(e.Payload, "stage") is { } stage ? $"Moved to {stage}" : "Stage changed", null),
        OfferEventType.ApplicationClosed => ("closed", ReadField(e.Payload, "outcome") is { } outcome ? $"Closed — {outcome}" : "Closed", null),
        OfferEventType.ApplicationReopened => ("reopened", "Reopened", null),
        _ => ("stageChanged", e.Type.ToString(), null),
    };

    private static string? ReadField(string? payload, string field)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty(field, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // --- Mapping + parsing ---

    private static ApplicationCardDto ToCardDto(ApplicationCard c) => new(
        c.OfferId.ToString(), c.Title, c.Company, c.StageId.ToString(), Camel(c.Status.ToString()),
        c.Outcome is { } outcome ? Camel(outcome.ToString()) : null,
        c.AppliedAt, c.OutstandingTaskCount, c.OverdueTaskCount, c.NextInterviewAt);

    private static NoteDto ToNoteDto(ApplicationNote n) => new(n.Id.ToString(), n.Body, n.CreatedAt);

    private static TaskDto ToTaskDto(ApplicationTask t, DateTimeOffset now) =>
        new(t.Id.ToString(), t.Title, t.Description, t.DueAt, t.CompletedAt, t.IsOverdue(now));

    private static DocumentDto ToDocumentDto(ApplicationDocument d) =>
        new(d.Id.ToString(), d.OriginalFileName, d.ContentType, d.SizeBytes, d.AddedAt);

    private static CommunicationDto ToCommunicationDto(ApplicationCommunication c) =>
        new(c.Id.ToString(), c.OccurredAt, Camel(c.Direction.ToString()), c.Channel, c.Summary);

    private static InterviewDto ToInterviewDto(ApplicationInterview i, DateTimeOffset now) =>
        new(i.Id.ToString(), i.Kind, i.ScheduledAt, i.Interviewer, i.Outcome, i.Notes, i.IsUpcoming(now));

    private static bool TryParseOutcome(string? raw, out ApplicationOutcome outcome) =>
        Enum.TryParse(raw, ignoreCase: true, out outcome) && Enum.IsDefined(outcome);

    private static bool TryParseDirection(string? raw, out CommunicationDirection direction) =>
        Enum.TryParse(raw, ignoreCase: true, out direction) && Enum.IsDefined(direction);

    private static string Camel(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
