namespace JobOfferMatcher.Domain.Salary;

/// <summary>
/// An amount in a specific currency. Pure value object (structural equality); reused as the
/// normalized-salary output (data-model §Salary). No FX is applied here — conversion lives in
/// <c>SalaryNormalizer</c> against the editable settings.
/// </summary>
public sealed record Money(decimal Amount, Currency Currency)
{
    public override string ToString() => $"{Amount:0.##} {Currency.Code}";
}
