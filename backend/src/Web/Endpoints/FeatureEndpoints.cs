namespace JobOfferMatcher.Web.Endpoints;

/// <summary>
/// Single place that wires each feature's endpoint group into <c>/api</c>. Extended phase by
/// phase (offers, scans, sources, schedule, cv, settings, export) so the host pipeline stays
/// stable while features are added.
/// </summary>
internal static class FeatureEndpoints
{
    public static IEndpointRouteBuilder MapFeatureEndpoints(this IEndpointRouteBuilder api)
    {
        // Feature groups are added here as each phase implements them.
        api.MapOfferEndpoints();
        api.MapScanEndpoints();
        api.MapScheduleEndpoints();
        api.MapCvEndpoints();
        api.MapSettingsEndpoints();
        api.MapSourceEndpoints();
        api.MapExportEndpoints();
        return api;
    }
}
