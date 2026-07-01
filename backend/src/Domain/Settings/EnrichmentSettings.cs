namespace JobOfferMatcher.Domain.Settings;

/// <summary>
/// The "Enrichment configuration" entity (data-model §2, FR-018). All word/skill caps are <b>soft</b>
/// — guidance to the Claude worker plus loose write-back validation; <see cref="RetryLimit"/> drives
/// the <c>Pending → Failed</c> transition. Stored on <see cref="AppSettings"/> as one jsonb column.
/// </summary>
public sealed record EnrichmentSettings
{
    public int OfferSummaryMaxWords { get; init; } = 60;
    public int CvSummaryMaxWords { get; init; } = 60;
    public int MaxKeySkills { get; init; } = 10;
    public int FitRationaleMaxWords { get; init; } = 30;

    /// <summary>
    /// Soft cap on the affinity rationale (feature 006). Additive field — no migration: the existing
    /// <c>enrichment</c> jsonb has no key for it, so existing rows deserialize this default.
    /// </summary>
    public int AffinityRationaleMaxWords { get; init; } = 30;
    public int RetryLimit { get; init; } = 3;

    public static EnrichmentSettings Default => new();
}
