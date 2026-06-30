using JobOfferMatcher.Domain.Matching;
using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Domain.Settings;

/// <summary>
/// Single-row editable settings (data-model §Settings): salary normalization, scoring weights, and
/// the user's profile preferences. Editing normalization/weights re-ranks (derived recompute).
/// </summary>
public sealed class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    public SalaryNormalizationSettings Normalization { get; private set; } = new();
    public ScoringWeights Weights { get; private set; } = ScoringWeights.Default;
    public ProfilePreferences Preferences { get; private set; } = new();
    public EnrichmentSettings Enrichment { get; private set; } = new();

    private AppSettings()
    {
        // EF Core materialization.
    }

    public static AppSettings CreateDefault() => new();

    public void UpdateNormalization(SalaryNormalizationSettings normalization) => Normalization = normalization;

    public void UpdateWeights(ScoringWeights weights) => Weights = weights;

    public void UpdatePreferences(ProfilePreferences preferences) => Preferences = preferences;

    public void UpdateEnrichment(EnrichmentSettings enrichment) => Enrichment = enrichment;
}
