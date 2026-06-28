using JobOfferMatcher.Application.Export;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// Export read side (FR-037): projects every stored offer + its source name, status, and timestamps
/// into the portable <see cref="OfferExport"/> rows. Read-only; derived figures are not exported.
/// </summary>
internal sealed class ExportReader(AppDbContext db) : IExportReader
{
    public async Task<IReadOnlyList<OfferExport>> GetOffersAsync(CancellationToken ct = default)
    {
        var sourceNames = await db.JobSources.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var offers = await db.Offers.AsNoTracking()
            .OrderByDescending(o => o.FirstSuggestedAt)
            .ToListAsync(ct);

        return offers.Select(o => new OfferExport(
            o.Id.Value,
            sourceNames.GetValueOrDefault(o.SourceId, "source"),
            o.Title,
            o.Company,
            o.Location,
            o.WorkMode.ToString(),
            o.EmploymentType,
            o.Seniority,
            [.. o.RequiredSkills],
            [.. o.NiceToHaveSkills],
            [.. o.SalaryBands.Select(b => new SalaryBandExport(
                b.AmountMin,
                b.AmountMax,
                b.Currency?.Code,
                b.Period?.ToString().ToLowerInvariant(),
                b.Basis.ToString().ToLowerInvariant(),
                b.Tax.ToString().ToLowerInvariant()))],
            o.CanonicalUrl,
            o.Availability == Domain.Offers.AvailabilityStatus.Available ? "available" : "no_longer_available",
            o.UserStatus.ToString().ToLowerInvariant(),
            o.RoleGroupId?.Value,
            o.FirstSeenAt,
            o.FirstSuggestedAt,
            o.LastSeenAt))
            .ToList();
    }
}
