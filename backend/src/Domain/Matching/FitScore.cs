namespace JobOfferMatcher.Domain.Matching;

/// <summary>
/// The explicit what-matches / what-does-not breakdown (FR-025) plus the per-axis 0..1 scores that
/// produced the total. Persisted only as a cache tagged with the CV/weights version (never as fact).
/// </summary>
public sealed record FitBreakdown(
    IReadOnlyList<string> Matched,
    IReadOnlyList<string> Missing,
    double SkillsScore,
    double SeniorityScore,
    double WorkModeScore,
    double EmploymentScore,
    double SalaryScore);

/// <summary>A 0–100 transparent fit score (FR-023) with its <see cref="FitBreakdown"/>.</summary>
public sealed record FitScore(int Value, FitBreakdown Breakdown);
