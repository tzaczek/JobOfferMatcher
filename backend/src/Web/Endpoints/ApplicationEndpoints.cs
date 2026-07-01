using JobOfferMatcher.Application.Applications;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>
/// Wires the UI-local application-tracking API under <c>/api/applications</c> (contracts/applications-api.md).
/// Same access model as <c>/api/offers</c> (single-user localhost) — deliberately NO loopback-only filter:
/// this is a UI surface, not the worker/backup channel. Covers stage configuration, the pipeline board,
/// an application's detail + timeline, the lifecycle (move/close/reopen/delete), and the
/// notes/tasks/documents/communications/interviews children.
/// </summary>
internal static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/applications");

        MapStageEndpoints(group);
        MapBoardAndLifecycleEndpoints(group);
        MapNoteEndpoints(group);
        MapTaskEndpoints(group);
        MapDocumentEndpoints(group);
        MapCommunicationAndInterviewEndpoints(group);

        return api;
    }

    // --- Pipeline stage configuration (FR-019) ---
    private static void MapStageEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/stages", async (PipelineStageService stages, CancellationToken ct) =>
            Results.Ok(await stages.ListAsync(ct)));

        group.MapPost("/stages", async (CreateStageRequest body, PipelineStageService stages, CancellationToken ct) =>
            (await stages.CreateAsync(body.Name, ct))
                .ToHttp(dto => Results.Created($"/api/applications/stages/{dto.Id}", dto)));

        group.MapPut("/stages/order", async (ReorderStagesRequest body, PipelineStageService stages, CancellationToken ct) =>
        {
            var ids = new List<PipelineStageId>(body.OrderedIds.Count);
            foreach (var raw in body.OrderedIds)
            {
                if (!PipelineStageId.TryParse(raw, out var id))
                {
                    return Results.BadRequest(new { error = new { code = "InvalidStageId", message = $"'{raw}' is not a valid stage id." } });
                }

                ids.Add(id);
            }

            return (await stages.ReorderAsync(ids, ct)).ToHttp(Results.NoContent);
        });

        group.MapPut("/stages/{id:guid}", async (Guid id, RenameStageRequest body, PipelineStageService stages, CancellationToken ct) =>
            (await stages.RenameAsync(PipelineStageId.From(id), body.Name, ct)).ToHttp(Results.NoContent));

        group.MapDelete("/stages/{id:guid}", async (Guid id, string? reassignTo, PipelineStageService stages, CancellationToken ct) =>
        {
            PipelineStageId? target = null;
            if (!string.IsNullOrWhiteSpace(reassignTo))
            {
                if (!PipelineStageId.TryParse(reassignTo, out var parsed))
                {
                    return Results.BadRequest(new { error = new { code = "InvalidStageId", message = $"'{reassignTo}' is not a valid stage id." } });
                }

                target = parsed;
            }

            return (await stages.RemoveAsync(PipelineStageId.From(id), target, ct)).ToHttp(Results.NoContent);
        });
    }

    // --- Board + detail + lifecycle (FR-002/003/004/007/009) ---
    private static void MapBoardAndLifecycleEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/", async (ApplicationTrackingService service, CancellationToken ct) =>
            Results.Ok(await service.GetBoardAsync(ct)));

        group.MapGet("/{offerId:guid}", async (Guid offerId, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.GetAsync(OfferId.From(offerId), ct)).ToHttp(Results.Ok));

        group.MapPost("/{offerId:guid}/stage", async (Guid offerId, MoveStageRequest body, ApplicationTrackingService service, CancellationToken ct) =>
        {
            if (!PipelineStageId.TryParse(body.StageId, out var stageId))
            {
                return Results.BadRequest(new { error = new { code = "InvalidStageId", message = $"'{body.StageId}' is not a valid stage id." } });
            }

            return (await service.MoveStageAsync(OfferId.From(offerId), stageId, ct)).ToHttp(Results.NoContent);
        });

        group.MapPost("/{offerId:guid}/close", async (Guid offerId, CloseApplicationRequest body, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.CloseAsync(OfferId.From(offerId), body.Outcome, ct)).ToHttp(Results.NoContent));

        group.MapPost("/{offerId:guid}/reopen", async (Guid offerId, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.ReopenAsync(OfferId.From(offerId), ct)).ToHttp(Results.NoContent));

        group.MapDelete("/{offerId:guid}", async (Guid offerId, bool? confirm, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.DeleteAsync(OfferId.From(offerId), confirm ?? false, ct)).ToHttp(Results.NoContent));
    }

    // --- Notes (FR-006 — append-only) ---
    private static void MapNoteEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/{offerId:guid}/notes", async (Guid offerId, AddNoteRequest body, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.AddNoteAsync(OfferId.From(offerId), body.Body, ct))
                .ToHttp(dto => Results.Created($"/api/applications/{offerId}", dto)));
    }

    // --- Tasks (FR-008 — overdue surfaced) ---
    private static void MapTaskEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/{offerId:guid}/tasks", async (Guid offerId, AddTaskRequest body, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.AddTaskAsync(OfferId.From(offerId), body.Title, body.Description, body.DueAt, ct))
                .ToHttp(dto => Results.Created($"/api/applications/{offerId}", dto)));

        group.MapPut("/{offerId:guid}/tasks/{taskId:guid}", async (Guid offerId, Guid taskId, UpdateTaskRequest body, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.UpdateTaskAsync(OfferId.From(offerId), ApplicationTaskId.From(taskId), body, ct)).ToHttp(Results.NoContent));

        group.MapDelete("/{offerId:guid}/tasks/{taskId:guid}", async (Guid offerId, Guid taskId, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.RemoveTaskAsync(OfferId.From(offerId), ApplicationTaskId.From(taskId), ct)).ToHttp(Results.NoContent));
    }

    // --- Documents (FR-010 — any type, ~50 MB, local) ---
    private static void MapDocumentEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/{offerId:guid}/documents", async (Guid offerId, IFormFile? file, ApplicationTrackingService service, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = new { code = "EmptyDocument", message = "No file was uploaded." } });
            }

            if (file.Length > ApplicationTrackingService.MaxDocumentBytes)
            {
                return ApplicationErrors.FileTooLarge.ToProblem(); // 413 without buffering the oversized file.
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var result = await service.AddDocumentAsync(OfferId.From(offerId), file.FileName, file.ContentType, ms.ToArray(), ct);
            return result.ToHttp(dto => Results.Created($"/api/applications/{offerId}/documents/{dto.Id}", dto));
        }).DisableAntiforgery();

        group.MapGet("/{offerId:guid}/documents/{docId:guid}/download", async (Guid offerId, Guid docId, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.GetDocumentDownloadAsync(OfferId.From(offerId), ApplicationDocumentId.From(docId), ct))
                .ToHttp(d => Results.File(d.AbsolutePath, d.ContentType, d.DownloadFileName)));

        group.MapDelete("/{offerId:guid}/documents/{docId:guid}", async (Guid offerId, Guid docId, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.RemoveDocumentAsync(OfferId.From(offerId), ApplicationDocumentId.From(docId), ct)).ToHttp(Results.NoContent));
    }

    // --- Communications & interviews (FR-011) ---
    private static void MapCommunicationAndInterviewEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/{offerId:guid}/communications", async (Guid offerId, AddCommunicationRequest body, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.AddCommunicationAsync(OfferId.From(offerId), body.OccurredAt, body.Direction, body.Channel, body.Summary, ct))
                .ToHttp(dto => Results.Created($"/api/applications/{offerId}", dto)));

        group.MapPost("/{offerId:guid}/interviews", async (Guid offerId, AddInterviewRequest body, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.AddInterviewAsync(OfferId.From(offerId), body.Kind, body.ScheduledAt, body.Interviewer, body.Notes, ct))
                .ToHttp(dto => Results.Created($"/api/applications/{offerId}", dto)));

        group.MapPut("/{offerId:guid}/interviews/{interviewId:guid}", async (Guid offerId, Guid interviewId, UpdateInterviewRequest body, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.UpdateInterviewAsync(OfferId.From(offerId), ApplicationInterviewId.From(interviewId), body, ct)).ToHttp(Results.NoContent));

        group.MapDelete("/{offerId:guid}/interviews/{interviewId:guid}", async (Guid offerId, Guid interviewId, ApplicationTrackingService service, CancellationToken ct) =>
            (await service.RemoveInterviewAsync(OfferId.From(offerId), ApplicationInterviewId.From(interviewId), ct)).ToHttp(Results.NoContent));
    }
}
