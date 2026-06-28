using JobOfferMatcher.Domain.Common;

namespace JobOfferMatcher.Domain.Salary;

/// <summary>
/// An ISO-4217 currency code (validated, 3-letter uppercase). Value object with structural
/// equality so it can key the editable FX table (research §7). Junk input → a failed result,
/// never an exception (Constitution Principle II).
/// </summary>
public sealed record Currency
{
    public static readonly Error UnknownCurrency = new("UnknownCurrency", "Currency code must be 3 ASCII letters (ISO-4217).");

    public string Code { get; }

    private Currency(string code) => Code = code;

    public static Result<Currency> Create(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return UnknownCurrency;
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(static c => c is >= 'A' and <= 'Z'))
        {
            return UnknownCurrency;
        }

        return new Currency(normalized);
    }

    // Common codes for tests/seeds. PLN is the normalization base (research §7).
    public static Currency Pln { get; } = new("PLN");
    public static Currency Eur { get; } = new("EUR");
    public static Currency Usd { get; } = new("USD");
    public static Currency Gbp { get; } = new("GBP");
    public static Currency Chf { get; } = new("CHF");

    public override string ToString() => Code;
}
