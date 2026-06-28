using JobOfferMatcher.Domain.Common;

namespace JobOfferMatcher.Domain.Salary;

/// <summary>
/// Pure, offline best-effort salary normalizer (research §7): range→point, period→monthly,
/// currency→base via the editable FX table, basis→canonical via the one documented factor. Returns
/// a <see cref="Result"/> failure when a required input (amount/period/currency/FX) is missing —
/// never throws. The result is DERIVED, never stored as fact (FR-035).
/// </summary>
public static class SalaryNormalizer
{
    public static readonly Error NoAmount = new("NoSalaryAmount", "Salary band has no amount.");
    public static readonly Error NoPeriod = new("NoSalaryPeriod", "Salary band has no period.");
    public static readonly Error NoCurrency = new("NoSalaryCurrency", "Salary band has no currency.");
    public static readonly Error UnknownFx = new("UnknownFxRate", "No FX rate configured for the band's currency.");

    public static Result<NormalizedSalary> Normalize(SalaryBand band, SalaryNormalizationSettings settings)
    {
        var (amount, rangeAssumption) = PickAmount(band, settings.RangeStrategy);
        if (amount is null)
        {
            return NoAmount;
        }

        if (band.Period is null)
        {
            return NoPeriod;
        }

        if (band.Currency is null)
        {
            return NoCurrency;
        }

        if (!settings.FxToBase.TryGetValue(band.Currency.Code, out var fx))
        {
            return UnknownFx;
        }

        var assumptions = new List<string>();
        var quality = NormalizationQuality.Reported;
        var value = amount.Value;

        if (rangeAssumption is not null)
        {
            assumptions.Add(rangeAssumption);
            quality = NormalizationQuality.Estimated;
        }

        var (monthly, periodAssumption) = ToMonthly(value, band.Period.Value, settings);
        value = monthly;
        if (periodAssumption is not null)
        {
            assumptions.Add(periodAssumption);
            quality = NormalizationQuality.Estimated;
        }

        if (!string.Equals(band.Currency.Code, settings.BaseCurrency.Code, StringComparison.Ordinal))
        {
            value *= fx;
            assumptions.Add($"FX {band.Currency.Code}→{settings.BaseCurrency.Code} ×{fx} (as of {settings.FxAsOf:yyyy-MM-dd})");
            quality = NormalizationQuality.Estimated;
        }

        if (settings.CanonicalBasis == EmploymentBasis.Permanent && band.Basis == EmploymentBasis.B2B)
        {
            value *= settings.B2bToPermanentFactor;
            assumptions.Add($"B2B→Permanent-equivalent ×{settings.B2bToPermanentFactor}");
            quality = NormalizationQuality.Estimated;
        }
        else if (band.Basis == EmploymentBasis.Unknown)
        {
            assumptions.Add("Employment basis unknown — no basis adjustment applied");
            quality = NormalizationQuality.RoughEstimate;
        }

        var comparable = new Money(Math.Round(value, 0, MidpointRounding.AwayFromZero), settings.BaseCurrency);
        return new NormalizedSalary(comparable, settings.CanonicalBasis, quality, assumptions, band);
    }

    private static (decimal? Amount, string? Assumption) PickAmount(SalaryBand band, RangePointStrategy strategy)
    {
        if (band.AmountMin is { } min && band.AmountMax is { } max)
        {
            if (min == max)
            {
                return (min, null); // single exact figure — no range assumption
            }

            return strategy switch
            {
                RangePointStrategy.Min => (min, null),
                RangePointStrategy.Max => (max, null),
                _ => ((min + max) / 2m, $"midpoint {min:0}–{max:0} = {(min + max) / 2m:0}"),
            };
        }

        return (band.AmountMax ?? band.AmountMin, null);
    }

    private static (decimal Monthly, string? Assumption) ToMonthly(decimal amount, SalaryPeriod period, SalaryNormalizationSettings s) =>
        period switch
        {
            SalaryPeriod.Monthly => (amount, null),
            SalaryPeriod.Yearly => (amount / 12m, "yearly ÷12"),
            SalaryPeriod.Hourly => (amount * s.AssumedMonthlyHours, $"hourly ×{s.AssumedMonthlyHours}h/mo"),
            SalaryPeriod.Daily => (amount * s.AssumedMonthlyWorkingDays, $"daily ×{s.AssumedMonthlyWorkingDays}d/mo"),
            _ => (amount, null),
        };
}
