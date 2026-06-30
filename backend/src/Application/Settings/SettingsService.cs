using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Matching;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Domain.Settings;

namespace JobOfferMatcher.Application.Settings;

/// <summary>
/// Read/update salary-normalization, scoring weights ("guidance to Claude"), and enrichment settings.
/// Editing weights re-arms all fits (FR-007/SC-004) since weights feed the fit input hash. Weights
/// must sum to 100; enrichment caps must be positive (RetryLimit ≥ 1).
/// </summary>
public sealed class SettingsService(ISettingsRepository settings, IEnrichmentRepository enrichment, IUnitOfWork unitOfWork)
{
    public static readonly Error InvalidWeights = new("InvalidWeights", "Scoring weights must sum to 100.");
    public static readonly Error InvalidEnrichmentSettings =
        new("InvalidEnrichmentSettings", "All enrichment caps must be greater than 0 and the retry limit at least 1.");

    public Task<AppSettings> GetAsync(CancellationToken ct = default) => settings.GetAsync(ct);

    public async Task UpdateNormalizationAsync(SalaryNormalizationSettings normalization, CancellationToken ct = default)
    {
        var current = await settings.GetAsync(ct);
        current.UpdateNormalization(normalization);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<Result> UpdateWeightsAsync(ScoringWeights weights, CancellationToken ct = default)
    {
        if (weights.Total != 100)
        {
            return InvalidWeights;
        }

        var current = await settings.GetAsync(ct);
        current.UpdateWeights(weights);
        await unitOfWork.SaveChangesAsync(ct);

        // Weights are fit-input guidance ⇒ all fits pending until re-scored (FR-007/SC-004).
        await enrichment.InvalidateAllFitsAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateEnrichmentAsync(EnrichmentSettings enrichmentSettings, CancellationToken ct = default)
    {
        if (enrichmentSettings.OfferSummaryMaxWords <= 0
            || enrichmentSettings.CvSummaryMaxWords <= 0
            || enrichmentSettings.MaxKeySkills <= 0
            || enrichmentSettings.FitRationaleMaxWords <= 0
            || enrichmentSettings.RetryLimit < 1)
        {
            return InvalidEnrichmentSettings;
        }

        var current = await settings.GetAsync(ct);
        current.UpdateEnrichment(enrichmentSettings);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
