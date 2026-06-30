using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>
/// The enrichment queue surface for the Claude Code worker (contracts/enrichment-api.md). The whole
/// group is <b>loopback-only</b> via a fail-closed filter — these endpoints serialize CV/offer text,
/// so loopback is the load-bearing PII control (Principle IV / ADR-4). The backend makes no AI call.
/// </summary>
internal static class EnrichmentEndpoints
{
    public sealed record RerunRequest(string? Scope);

    public static IEndpointRouteBuilder MapEnrichmentEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/enrichment").AddEndpointFilter<LoopbackOnlyFilter>();

        group.MapGet("/pending", async (int? limit, EnrichmentService service, CancellationToken ct) =>
            Results.Ok(await service.GetPendingWorkAsync(limit ?? 25, ct)));

        group.MapPost("/results", async (SubmitResultsRequest body, EnrichmentService service, CancellationToken ct) =>
            Results.Ok(await service.SubmitResultsAsync(body, ct)));

        group.MapGet("/status", async (EnrichmentService service, CancellationToken ct) =>
            Results.Ok(await service.GetStatusAsync(ct)));

        group.MapPost("/rerun", async (RerunRequest? body, EnrichmentService service, CancellationToken ct) =>
            Results.Ok(await service.TriggerRerunAsync(body?.Scope, ct)));

        return api;
    }
}

/// <summary>
/// Rejects any non-local (or null/unknown remote-IP) request to a loopback-only group with 403
/// (fail-closed). Guards both <c>/api/enrichment/*</c> and <c>/api/backup/*</c>. When
/// <c>Loopback:TrustPrivateNetwork</c> is set (container mode behind a host-loopback-bound port) it also
/// admits the Docker-gateway source that host-published traffic arrives from (see <see cref="LoopbackGuard"/>).
/// </summary>
internal sealed class LoopbackOnlyFilter(IConfiguration configuration) : IEndpointFilter
{
    private readonly bool _trustPrivateNetwork = configuration.GetValue("Loopback:TrustPrivateNetwork", false);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var remoteIp = context.HttpContext.Connection.RemoteIpAddress;
        if (!LoopbackGuard.IsAllowed(remoteIp, _trustPrivateNetwork))
        {
            return Results.Json(
                new { error = new { code = "LoopbackOnly", message = "This API is restricted to the local machine." } },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
