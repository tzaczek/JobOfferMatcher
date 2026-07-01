using JobOfferMatcher.Application.Export;
using JobOfferMatcher.Domain.Enrichment;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// Export read side (FR-037): projects every stored offer + its source name, status, and timestamps
/// into the portable <see cref="OfferExport"/> rows. Read-only; fit stays excluded, but the captured
/// body + the current produced affinity score ARE exported (006, SC-006 — with the same input-hash +
/// ≥3 guard as the read model, so a stale/insufficient score exports as null, never misleading).
/// </summary>
internal sealed class ExportReader(AppDbContext db) : IExportReader
{
    public async Task<IReadOnlyList<OfferExport>> GetOffersAsync(CancellationToken ct = default)
    {
        var sourceNames = await db.JobSources.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var offers = await db.Offers.AsNoTracking()
            .OrderByDescending(o => o.FirstSuggestedAt)
            .ToListAsync(ct);

        // Affinity export (006): the current PRODUCED score only, guarded by input hash + the ≥3 basis.
        var affinities = (await db.OfferAffinities.AsNoTracking().ToListAsync(ct)).ToDictionary(a => a.OfferId);
        var appliedBasis = offers.Where(o => o.Applied).Select(o => (o.Id, o.CurrentFingerprint.Hash)).ToList();
        var basisVersion = AppliedBasisInputs.Version(appliedBasis);

        int? AffinityScoreOf(Domain.Offers.Offer o)
        {
            if (basisVersion is null || !affinities.TryGetValue(o.Id, out var a) || a.State != EnrichmentState.Produced)
            {
                return null;
            }

            var offerEnrichHash = OfferEnrichmentInputs.Hash(o.CurrentFingerprint.Hash, o.Company, o.Location, o.DescriptionHtml);
            var currentHash = OfferAffinityInputs.Hash(offerEnrichHash, basisVersion).Serialized;
            return a.InputsHash == currentHash ? a.Score : null;
        }

        // Application tracking (005, FR-018): current stage/status/outcome + interview history per offer.
        var applications = (await db.Applications.AsNoTracking().ToListAsync(ct)).ToDictionary(a => a.OfferId);
        var stageNames = await db.PipelineStages.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var interviewsByOffer = (await db.ApplicationInterviews.AsNoTracking().ToListAsync(ct))
            .GroupBy(i => i.OfferId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<InterviewEventExport>)g
                    .OrderBy(i => i.ScheduledAt ?? i.CreatedAt)
                    .Select(i => new InterviewEventExport(i.Kind, i.ScheduledAt, i.Outcome))
                    .ToList());

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
            o.DescriptionHtml,
            AffinityScoreOf(o),
            o.CanonicalUrl,
            o.Availability == Domain.Offers.AvailabilityStatus.Available ? "available" : "no_longer_available",
            o.UserStatus.ToString().ToLowerInvariant(),
            o.Applied,
            o.AppliedAt,
            o.ApplicationNote,
            o.RoleGroupId?.Value,
            o.FirstSeenAt,
            o.FirstSuggestedAt,
            o.LastSeenAt,
            applications.TryGetValue(o.Id, out var app) && stageNames.TryGetValue(app.CurrentStageId, out var stageName) ? stageName : null,
            app?.Status.ToString().ToLowerInvariant(),
            app?.Outcome?.ToString().ToLowerInvariant(),
            interviewsByOffer.GetValueOrDefault(o.Id, [])))
            .ToList();
    }
}
