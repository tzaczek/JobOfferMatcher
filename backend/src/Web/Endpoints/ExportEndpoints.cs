using JobOfferMatcher.Application.Export;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>Data export (FR-037, contracts/rest-api.md §Export) — streams a portable file.</summary>
internal static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/export", async (string? format, ExportService export, CancellationToken ct) =>
        {
            var chosen = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
                ? ExportFormat.Csv
                : ExportFormat.Json;

            var file = await export.ExportAsync(chosen, ct);
            return Results.File(file.Content, file.ContentType, file.FileName);
        });

        return api;
    }
}
