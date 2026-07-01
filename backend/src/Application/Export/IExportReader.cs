namespace JobOfferMatcher.Application.Export;

/// <summary>
/// Read port for the export use case (Principle I — implemented in Infrastructure over EF Core).
/// Returns every collected offer with its captured fields, user status, and timestamps.
/// </summary>
public interface IExportReader
{
    Task<IReadOnlyList<OfferExport>> GetOffersAsync(CancellationToken ct = default);
}
