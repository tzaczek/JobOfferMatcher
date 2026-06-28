namespace JobOfferMatcher.Domain.Salary;

/// <summary>
/// One raw, as-published salary band (FR-008/010). Every numeric/categorical field is nullable
/// because sources often omit them — an offer with no disclosed salary is represented by an
/// EMPTY band list, never a band of zeros (data-model §Salary). Stored authoritatively; the
/// comparable figure is derived on demand by <c>SalaryNormalizer</c> (never persisted as fact).
/// </summary>
public sealed record SalaryBand
{
    public decimal? AmountMin { get; init; }
    public decimal? AmountMax { get; init; }
    public Currency? Currency { get; init; }
    public SalaryPeriod? Period { get; init; }
    public EmploymentBasis Basis { get; init; } = EmploymentBasis.Unknown;
    public TaxTreatment Tax { get; init; } = TaxTreatment.Unknown;

    /// <summary>True when this band carries enough to attempt normalization (amount + currency + period).</summary>
    public bool HasComparableInputs =>
        (AmountMin is not null || AmountMax is not null) && Currency is not null && Period is not null;
}
