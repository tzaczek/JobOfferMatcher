using JobOfferMatcher.Application.Scheduling;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>Scan schedule config (contracts/rest-api.md §Schedule, FR-019).</summary>
internal static class ScheduleEndpoints
{
    public sealed record UpdateScheduleRequest(string Cron, string TimeZone, bool Enabled);

    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/schedule");

        group.MapGet("/", async (ScheduleService schedule, CancellationToken ct) =>
        {
            var config = await schedule.GetAsync(ct);
            return Results.Ok(new
            {
                cron = config.Cron,
                timeZone = config.TimeZone,
                enabled = config.Enabled,
                lastRunUtc = config.LastRunUtc,
            });
        });

        group.MapPut("/", async (UpdateScheduleRequest body, ScheduleService schedule, CancellationToken ct) =>
        {
            var result = await schedule.UpdateAsync(body.Cron, body.TimeZone, body.Enabled, ct);
            return result.ToHttp(config => Results.Ok(new
            {
                cron = config.Cron,
                timeZone = config.TimeZone,
                enabled = config.Enabled,
                lastRunUtc = config.LastRunUtc,
            }));
        });

        return api;
    }
}
