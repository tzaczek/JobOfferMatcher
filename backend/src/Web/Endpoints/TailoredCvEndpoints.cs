using System.Text;
using JobOfferMatcher.Application.TailoredCvs;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>
/// The tailored-CV surface (contracts/tailored-cv-api.md) — two audiences, both loopback-only via the
/// fail-closed <see cref="LoopbackOnlyFilter"/> (the load-bearing PII control, Principle IV/ADR-2): UI
/// endpoints (the modal + the dedicated page) and <b>worker</b> endpoints drained by the <c>/tailor-cv</c>
/// Claude Code session. The backend makes no external AI call; the source-CV binary is delivered as a
/// filesystem path, never over HTTP, and the produced PDF is streamed only to the local user.
/// </summary>
internal static class TailoredCvEndpoints
{
    public static IEndpointRouteBuilder MapTailoredCvEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/tailored-cv").AddEndpointFilter<LoopbackOnlyFilter>();

        // ---- UI -----------------------------------------------------------------------------

        // List all tailored CVs (the dedicated page), newest first.
        group.MapGet("/", async (TailoredCvService service, CancellationToken ct) =>
            Results.Ok(new { data = await service.ListAsync(ct) }));

        // The prefilled modal contents (FR-013/FR-003); optional ?skills= recomposes the default prompt.
        group.MapGet("/offer/{offerId:guid}/draft", async (Guid offerId, string? skills, TailoredCvService service, CancellationToken ct) =>
            (await service.GetDraftAsync(OfferId.From(offerId), skills, ct)).ToHttp(draft => Results.Ok(draft)));

        // Create or regenerate (latest-only) → pending; bumps GenerationVersion. Defers through a restore.
        group.MapPost("/offer/{offerId:guid}", async (Guid offerId, GenerateTailoredCvRequest? body, TailoredCvService service, CancellationToken ct) =>
            (await service.GenerateAsync(OfferId.From(offerId), body, ct)).ToHttp(view => Results.Ok(view)));

        // The tailored CV for one offer (reopen from the offer).
        group.MapGet("/offer/{offerId:guid}", async (Guid offerId, TailoredCvService service, CancellationToken ct) =>
            (await service.GetAsync(OfferId.From(offerId), ct)).ToHttp(view => Results.Ok(view)));

        // Serve the produced HTML for in-app viewing (US1 "shown to me" — the modal iframe).
        group.MapGet("/offer/{offerId:guid}/preview", async (Guid offerId, TailoredCvService service, CancellationToken ct) =>
            (await service.GetPreviewHtmlAsync(OfferId.From(offerId), ct))
                .ToHttp(html => Results.Content(html, "text/html", Encoding.UTF8)));

        // Stream the produced PDF (US3) — the only place CV-derived bytes leave, over loopback to the owner.
        group.MapGet("/offer/{offerId:guid}/download", async (Guid offerId, TailoredCvService service, CancellationToken ct) =>
            (await service.GetDownloadAsync(OfferId.From(offerId), ct))
                .ToHttp(d => Results.File(d.AbsolutePath, "application/pdf", d.DownloadFileName)));

        // Remove the tailored CV (row + both files). Idempotent.
        group.MapDelete("/offer/{offerId:guid}", async (Guid offerId, TailoredCvService service, CancellationToken ct) =>
            (await service.DeleteAsync(OfferId.From(offerId), ct)).ToHttp(() => Results.NoContent()));

        // ---- Worker (drained by /tailor-cv) -------------------------------------------------

        group.MapGet("/pending", async (int? limit, TailoredCvService service, CancellationToken ct) =>
            Results.Ok(await service.GetPendingWorkAsync(limit ?? 10, ct)));

        group.MapPost("/results", async (TailoredCvSubmitRequest body, TailoredCvService service, CancellationToken ct) =>
            Results.Ok(await service.SubmitResultsAsync(body, ct)));

        return api;
    }
}
