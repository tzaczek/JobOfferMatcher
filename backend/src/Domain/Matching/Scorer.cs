namespace JobOfferMatcher.Domain.Matching;

/// <summary>
/// Pure feed-ordering helpers (research §4). The transparent non-AI <c>Score</c> producer and its
/// <c>ScoringInput</c>/<c>FitScore</c> types were removed (ADR-2 / FR-005): fit is now produced
/// solely by the Claude worker and persisted as <see cref="JobOfferMatcher.Domain.Enrichment.OfferFit"/>.
/// Only the ranking math survives here — it orders the feed, it is <b>never a displayed fit</b>.
/// </summary>
public static class Scorer
{
    /// <summary>Default sort with a produced AI fit present: fit-weighted with a salary tilt (0..100 each).</summary>
    public static double CombinedRank(int fitScore, double normalizedSalaryScore) =>
        (0.70 * fitScore) + (0.30 * normalizedSalaryScore);

    /// <summary>Graceful degradation with no produced fit (FR-026): salary + recency only (0..100 each).</summary>
    public static double DegradedRank(double normalizedSalaryScore, double recencyScore) =>
        (0.60 * normalizedSalaryScore) + (0.40 * recencyScore);
}
