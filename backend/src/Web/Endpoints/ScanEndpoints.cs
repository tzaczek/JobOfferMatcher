using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>Scan trigger + status polling (contracts/rest-api.md §Scans).</summary>
internal static class ScanEndpoints
{
    public sealed record RunScanRequest(string[]? SourceIds);

    public static IEndpointRouteBuilder MapScanEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/scans");

        group.MapPost("/run", async (RunScanRequest? body, IScanRunner runner, CancellationToken ct) =>
        {
            IReadOnlyList<SourceId>? sourceIds = null;
            if (body?.SourceIds is { Length: > 0 } raw)
            {
                var parsed = new List<SourceId>();
                foreach (var s in raw)
                {
                    if (!SourceId.TryParse(s, out var id))
                    {
                        return Results.BadRequest(new { error = new { code = "InvalidSourceId", message = $"'{s}' is not a valid source id." } });
                    }

                    parsed.Add(id);
                }

                sourceIds = parsed;
            }

            var result = await runner.RunAsync(new ScanRequest(sourceIds, TriggerType.Manual), ct);
            return result.ToHttp(id => Results.Ok(new { scanRunId = id.Value }));
        });

        group.MapGet("/", async (IScanRunRepository scanRuns, CancellationToken ct) =>
        {
            var runs = await scanRuns.GetRecentAsync(50, ct);
            return Results.Ok(new { data = runs.Select(ToSummary) });
        });

        group.MapGet("/{id}", async (string id, IScanRunRepository scanRuns, CancellationToken ct) =>
        {
            if (!ScanRunId.TryParse(id, out var scanRunId))
            {
                return Results.NotFound();
            }

            var run = await scanRuns.GetByIdAsync(scanRunId, ct);
            return run is null ? Results.NotFound() : Results.Ok(ToSummary(run));
        });

        group.MapGet("/{id}/status", async (string id, IScanRunRepository scanRuns, CancellationToken ct) =>
        {
            if (!ScanRunId.TryParse(id, out var scanRunId))
            {
                return Results.NotFound();
            }

            var run = await scanRuns.GetByIdAsync(scanRunId, ct);
            return run is null ? Results.NotFound() : Results.Ok(ToStatus(run));
        });

        return api;
    }

    private static object ToSummary(ScanRun run) => new
    {
        scanRunId = run.Id.Value,
        startedAt = run.StartedAt,
        finishedAt = run.FinishedAt,
        trigger = run.Trigger.ToString(),
        outcome = run.IsFinished ? run.Outcome.ToString().ToLowerInvariant() : null,
        counts = new
        {
            collected = run.Counts.Collected,
            @new = run.Counts.New,
            updated = run.Counts.Updated,
            unavailable = run.Counts.Unavailable,
            failed = run.Counts.Failed,
        },
        incompleteReason = run.IncompleteReason?.ToString(),
    };

    private static object ToStatus(ScanRun run)
    {
        var state = !run.IsFinished
            ? "running"
            : run.Outcome == ScanOutcome.Complete ? "completed" : "incomplete";

        return new
        {
            state,
            outcome = run.IsFinished ? run.Outcome.ToString().ToLowerInvariant() : null,
            counts = new
            {
                collected = run.Counts.Collected,
                @new = run.Counts.New,
                updated = run.Counts.Updated,
                unavailable = run.Counts.Unavailable,
                failed = run.Counts.Failed,
            },
            incompleteReason = run.IncompleteReason?.ToString(),
        };
    }
}
