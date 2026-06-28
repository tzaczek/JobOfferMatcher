namespace JobOfferMatcher.Domain.Salary;

/// <summary>Strategy for reducing a salary range to one comparable figure (research §7).</summary>
public enum RangePointStrategy
{
    Min,
    Midpoint,
    Max,
}

/// <summary>UI honesty signal for a normalized figure (research §7).</summary>
public enum NormalizationQuality
{
    Reported,
    Estimated,
    RoughEstimate,
}

/// <summary>
/// Editable, offline normalization settings (research §7): an FX table (no live API), assumed
/// hours/days, and the single documented B2B↔Permanent factor. Defaults are PLN-based. Stored as a
/// single editable row/JSON; never live-fetched (Principle IV).
/// </summary>
public sealed record SalaryNormalizationSettings
{
    public Currency BaseCurrency { get; init; } = Currency.Pln;
    public IReadOnlyDictionary<string, decimal> FxToBase { get; init; } = new Dictionary<string, decimal>
    {
        ["PLN"] = 1m,
        ["EUR"] = 4.30m,
        ["USD"] = 4.00m,
        ["GBP"] = 5.00m,
        ["CHF"] = 4.50m,
    };
    public decimal AssumedMonthlyHours { get; init; } = 168m;
    public decimal AssumedMonthlyWorkingDays { get; init; } = 21m;
    public decimal B2bToPermanentFactor { get; init; } = 0.85m;
    public EmploymentBasis CanonicalBasis { get; init; } = EmploymentBasis.Permanent;
    public RangePointStrategy RangeStrategy { get; init; } = RangePointStrategy.Midpoint;
    public DateOnly FxAsOf { get; init; } = new(2026, 6, 28);
    public string FxSource { get; init; } = "manual";
}
