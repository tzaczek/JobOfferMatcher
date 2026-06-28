namespace JobOfferMatcher.Domain.Salary;

/// <summary>
/// A derived, comparable monthly figure (research §7) — NEVER persisted as a captured fact
/// (FR-035). Carries the quality chip + an ordered assumptions audit trail for UI honesty, and the
/// raw <see cref="Source"/> band it came from.
/// </summary>
public sealed record NormalizedSalary(
    Money ComparableMonthly,
    EmploymentBasis NormalizedToBasis,
    NormalizationQuality Quality,
    IReadOnlyList<string> Assumptions,
    SalaryBand Source)
{
    public SalaryPeriod NormalizedToPeriod => SalaryPeriod.Monthly;
}
