using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>Unit tests (T047): range→midpoint, period→monthly, currency→base, B2B↔Permanent, quality, failures.</summary>
public sealed class SalaryNormalizerTests
{
    private static readonly SalaryNormalizationSettings Settings = new();

    private static SalaryBand Band(
        decimal? min = 18000,
        decimal? max = 22000,
        Currency? currency = null,
        SalaryPeriod? period = SalaryPeriod.Monthly,
        EmploymentBasis basis = EmploymentBasis.B2B,
        TaxTreatment tax = TaxTreatment.Net) => new()
    {
        AmountMin = min,
        AmountMax = max,
        Currency = currency ?? Currency.Pln,
        Period = period,
        Basis = basis,
        Tax = tax,
    };

    [Fact]
    public void Single_exact_pln_permanent_band_is_reported_with_no_assumptions()
    {
        var result = SalaryNormalizer.Normalize(
            Band(min: 15000, max: 15000, basis: EmploymentBasis.Permanent), Settings);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ComparableMonthly.Amount.ShouldBe(15000m);
        result.Value.Quality.ShouldBe(NormalizationQuality.Reported);
        result.Value.Assumptions.ShouldBeEmpty();
    }

    [Fact]
    public void Range_uses_midpoint_and_b2b_factor_with_estimated_quality()
    {
        var result = SalaryNormalizer.Normalize(Band(min: 18000, max: 22000, basis: EmploymentBasis.B2B), Settings);

        result.IsSuccess.ShouldBeTrue();
        // midpoint 20000 × 0.85 = 17000
        result.Value.ComparableMonthly.Amount.ShouldBe(17000m);
        result.Value.Quality.ShouldBe(NormalizationQuality.Estimated);
        result.Value.Assumptions.ShouldContain(a => a.Contains("midpoint"));
        result.Value.Assumptions.ShouldContain(a => a.Contains("B2B→Permanent"));
    }

    [Fact]
    public void Foreign_currency_is_converted_via_the_fx_table()
    {
        var result = SalaryNormalizer.Normalize(
            Band(min: 5000, max: 5000, currency: Currency.Eur, basis: EmploymentBasis.Permanent), Settings);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ComparableMonthly.Amount.ShouldBe(21500m); // 5000 × 4.30
        result.Value.ComparableMonthly.Currency.Code.ShouldBe("PLN");
        result.Value.Assumptions.ShouldContain(a => a.Contains("FX EUR→PLN"));
    }

    [Fact]
    public void Yearly_is_divided_to_monthly()
    {
        var result = SalaryNormalizer.Normalize(
            Band(min: 240000, max: 240000, period: SalaryPeriod.Yearly, basis: EmploymentBasis.Permanent), Settings);

        result.Value.ComparableMonthly.Amount.ShouldBe(20000m); // 240000 / 12
        result.Value.Assumptions.ShouldContain(a => a.Contains("yearly"));
    }

    [Fact]
    public void Unknown_basis_yields_rough_estimate()
    {
        var result = SalaryNormalizer.Normalize(
            Band(min: 10000, max: 10000, basis: EmploymentBasis.Unknown), Settings);

        result.Value.Quality.ShouldBe(NormalizationQuality.RoughEstimate);
    }

    [Fact]
    public void Missing_amount_period_or_currency_fail()
    {
        SalaryNormalizer.Normalize(Band(min: null, max: null), Settings).Error.ShouldBe(SalaryNormalizer.NoAmount);
        SalaryNormalizer.Normalize(Band(period: null), Settings).Error.ShouldBe(SalaryNormalizer.NoPeriod);
        SalaryNormalizer.Normalize(Band() with { Currency = null }, Settings).Error.ShouldBe(SalaryNormalizer.NoCurrency);
    }
}
