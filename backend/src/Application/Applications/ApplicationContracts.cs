namespace JobOfferMatcher.Application.Applications;

// Wire contracts for /api/applications/* (contracts/applications-api.md). Records serialize camelCase
// at the boundary (JsonSerializerDefaults.Web). Enum-ish fields (status/outcome/direction/kind) are
// lowercase-first strings so the TS union types line up. All times are ISO-8601 UTC.

// --- Pipeline stage configuration (FR-019) ---
public sealed record PipelineStageDto(string Id, string Name, int Position);

public sealed record CreateStageRequest(string Name);
public sealed record RenameStageRequest(string Name);
public sealed record ReorderStagesRequest(IReadOnlyList<string> OrderedIds);

// --- Board (FR-004 / FR-009) ---
public sealed record ApplicationCardDto(
    string OfferId,
    string Title,
    string Company,
    string StageId,
    string Status,
    string? Outcome,
    DateTimeOffset? AppliedAt,
    int OutstandingTaskCount,
    int OverdueTaskCount,
    DateTimeOffset? NextInterviewAt);

public sealed record ApplicationBoardStageDto(string Id, string Name, int Position, IReadOnlyList<ApplicationCardDto> Applications);

public sealed record ApplicationBoardDto(IReadOnlyList<ApplicationBoardStageDto> Stages, IReadOnlyList<ApplicationCardDto> Closed);

// --- Detail + timeline (FR-002 / FR-003 / FR-007) ---
public sealed record TimelineEntryDto(DateTimeOffset OccurredAt, string Kind, string Title, string? Detail);

public sealed record NoteDto(string Id, string Body, DateTimeOffset CreatedAt);

public sealed record TaskDto(string Id, string Title, string? Description, DateTimeOffset? DueAt, DateTimeOffset? CompletedAt, bool Overdue);

public sealed record DocumentDto(string Id, string OriginalFileName, string? ContentType, long SizeBytes, DateTimeOffset AddedAt);

public sealed record CommunicationDto(string Id, DateTimeOffset OccurredAt, string Direction, string Channel, string Summary);

public sealed record InterviewDto(
    string Id,
    string Kind,
    DateTimeOffset? ScheduledAt,
    string? Interviewer,
    string? Outcome,
    string? Notes,
    bool Upcoming);

public sealed record ApplicationDetailDto(
    string OfferId,
    string Title,
    string Company,
    string StageId,
    string Status,
    string? Outcome,
    DateTimeOffset? AppliedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<TimelineEntryDto> Timeline,
    IReadOnlyList<NoteDto> Notes,
    IReadOnlyList<TaskDto> Tasks,
    IReadOnlyList<DocumentDto> Documents,
    IReadOnlyList<CommunicationDto> Communications,
    IReadOnlyList<InterviewDto> Interviews);

// --- Lifecycle requests ---
public sealed record MoveStageRequest(string StageId);
public sealed record CloseApplicationRequest(string Outcome);

// --- Notes / tasks / communications / interviews requests ---
public sealed record AddNoteRequest(string Body);
public sealed record AddTaskRequest(string Title, string? Description, DateTimeOffset? DueAt);
public sealed record UpdateTaskRequest(string? Title, string? Description, DateTimeOffset? DueAt, bool? Completed);
public sealed record AddCommunicationRequest(DateTimeOffset OccurredAt, string Direction, string Channel, string Summary);
public sealed record AddInterviewRequest(string Kind, DateTimeOffset? ScheduledAt, string? Interviewer, string? Notes);
public sealed record UpdateInterviewRequest(string? Kind, DateTimeOffset? ScheduledAt, string? Interviewer, string? Outcome, string? Notes);

/// <summary>An attachment ready to stream to the browser (absolute path + the user's original name).</summary>
public sealed record ApplicationDocumentDownload(string AbsolutePath, string DownloadFileName, string ContentType);
