using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Matching;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Domain.Settings;

namespace JobOfferMatcher.Application.Settings;

/// <summary>
/// Read/update salary-normalization settings + scoring weights (research §4/§7). Editing either
/// re-ranks the feed (the figures are derived on read). Weights must sum to 100.
/// </summary>
public sealed class SettingsService(ISettingsRepository settings, IUnitOfWork unitOfWork)
{
    public static readonly Error InvalidWeights = new("InvalidWeights", "Scoring weights must sum to 100.");

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
        return Result.Success();
    }
}
