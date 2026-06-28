namespace JobOfferMatcher.Web.Endpoints;

/// <summary>
/// Maps the <c>/api</c> surface (contracts/rest-api.md). Each feature adds its own
/// <c>Map…Endpoints</c> extension on the returned group as it is implemented across phases.
/// </summary>
public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("Health");

        // Feature endpoint groups (offers, scans, sources, schedule, cv, settings, export)
        // are registered here as each phase implements them.
        api.MapFeatureEndpoints();

        return app;
    }
}
