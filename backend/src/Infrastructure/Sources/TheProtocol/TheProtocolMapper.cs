using System.Globalization;
using System.Text.Json;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Infrastructure.Sources.TheProtocol;

/// <summary>
/// Pure mapping from a theprotocol.it offer (from <c>__NEXT_DATA__.offersResponse.offers</c>) →
/// source-agnostic <see cref="CollectedOffer"/>. Isolated + unit-tested against recorded fixtures
/// (research §1 / accepted-risk ADR-2). No HTTP here. Identity is the offer <c>id</c> (a stable GUID).
/// Localized labels (work mode, salary period, gross/net) arrive in the OFFER's own language — Polish
/// OR English regardless of request headers — so every lookup maps BOTH spellings.
/// </summary>
public static class TheProtocolMapper
{
    /// <summary>The stable cross-scan identity of an offer (its GUID).</summary>
    public static string? GetId(JsonElement item) =>
        GetString(item, "id") is { Length: > 0 } id ? id : null;

    public static Result<CollectedOffer> MapListItem(JsonElement item, SourceId sourceId, string offerUrlTemplate)
    {
        if (GetId(item) is not { } id)
        {
            return new Error("MissingId", "Offer has no id.");
        }

        var externalRef = ExternalRef.Create(sourceId, id, IdentityKind.NativeId);
        if (externalRef.IsFailure)
        {
            return externalRef.Error;
        }

        var urlName = GetString(item, "offerUrlName") ?? id;
        var bands = MapSalaryBands(item);
        var workMode = MapWorkMode(FirstString(item, "workModes"));

        var content = new OfferContent
        {
            Title = GetString(item, "title") ?? "(untitled)",
            Company = GetString(item, "employer") ?? "(unknown)",
            SalaryBands = bands,
            Location = MapPrimaryLocation(item, workMode),
            WorkMode = workMode,
            EmploymentType = MapEmploymentTypeLabel(bands),
            Seniority = MapSeniority(item),
            RequiredSkills = MapStringArray(item, "technologies"),
            NiceToHaveSkills = [],
            DescriptionHtml = null,
            CanonicalUrl = offerUrlTemplate.Replace("{offerUrlName}", urlName, StringComparison.Ordinal),
            PublishedAt = GetDate(item, "publicationDateUtc"),
            LastPublishedAt = null,
            ExpiredAt = null,
        };

        return new CollectedOffer(externalRef.Value, content);
    }

    private static IReadOnlyList<SalaryBand> MapSalaryBands(JsonElement item)
    {
        if (!item.TryGetProperty("typesOfContracts", out var contracts) || contracts.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var bands = new List<SalaryBand>();
        foreach (var contract in contracts.EnumerateArray())
        {
            if (!contract.TryGetProperty("salary", out var salary) || salary.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var from = GetDecimal(salary, "from");
            var to = GetDecimal(salary, "to");
            var currency = MapCurrency(GetString(salary, "currencySymbol"));

            if (from is null && to is null && currency is null)
            {
                continue; // hidden / empty → no band (never a zero band) — FR-010.
            }

            bands.Add(new SalaryBand
            {
                AmountMin = from,
                AmountMax = to,
                Currency = currency,
                Period = MapPeriod(GetTimeUnit(salary)),
                Basis = MapBasis(GetInt(contract, "id")),
                Tax = MapTax(GetString(salary, "kindName")),
            });
        }

        return bands;
    }

    private static string? MapEmploymentTypeLabel(IReadOnlyList<SalaryBand> bands)
    {
        var bases = bands
            .Select(b => b.Basis)
            .Where(b => b != EmploymentBasis.Unknown)
            .Distinct()
            .Select(b => b.ToString().ToLowerInvariant())
            .ToList();

        return bases.Count == 0 ? null : string.Join(", ", bases);
    }

    private static string? MapPrimaryLocation(JsonElement item, WorkMode workMode)
    {
        if (item.TryGetProperty("workplace", out var workplaces) && workplaces.ValueKind == JsonValueKind.Array)
        {
            foreach (var place in workplaces.EnumerateArray())
            {
                if (GetString(place, "city") is { Length: > 0 } city)
                {
                    return city;
                }

                if (GetString(place, "location") is { Length: > 0 } location)
                {
                    return location;
                }
            }
        }

        return workMode == WorkMode.Remote ? "Remote" : null;
    }

    private static string? MapSeniority(JsonElement item)
    {
        if (item.TryGetProperty("positionLevels", out var levels) && levels.ValueKind == JsonValueKind.Array)
        {
            foreach (var level in levels.EnumerateArray())
            {
                if (GetString(level, "value") is { Length: > 0 } value)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> MapStringArray(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } value)
            {
                result.Add(value.Trim());
            }
        }

        return result;
    }

    /// <summary>Work mode arrives in the offer's own language (e.g. "remote"/"zdalna").</summary>
    private static WorkMode MapWorkMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "remote" or "zdalna" or "zdalnie" => WorkMode.Remote,
        "hybrid" or "hybrydowa" or "hybrydowo" => WorkMode.Hybrid,
        "full office" or "office" or "stacjonarna" or "stacjonarnie" => WorkMode.Office,
        _ => WorkMode.Unknown,
    };

    /// <summary>Contract id → basis (theprotocol: 0 = umowa o pracę, 3 = B2B; others not salary-mapped here).</summary>
    private static EmploymentBasis MapBasis(int? contractId) => contractId switch
    {
        0 => EmploymentBasis.Permanent,
        3 => EmploymentBasis.B2B,
        _ => EmploymentBasis.Unknown,
    };

    /// <summary>kindName arrives localized: "gross"/"brutto" vs "net (+ VAT)"/"netto (+ VAT)".</summary>
    private static TaxTreatment MapTax(string? kindName)
    {
        if (kindName is null)
        {
            return TaxTreatment.Unknown;
        }

        var k = kindName.ToLowerInvariant();
        if (k.Contains("brutto", StringComparison.Ordinal) || k.Contains("gross", StringComparison.Ordinal))
        {
            return TaxTreatment.Gross;
        }

        if (k.Contains("net", StringComparison.Ordinal)) // covers "net (+ VAT)" and "netto (+ VAT)"
        {
            return TaxTreatment.Net;
        }

        return TaxTreatment.Unknown;
    }

    /// <summary>timeUnit.longForm arrives localized: "monthly"/"miesięcznie", "hourly"/"godzinowo", etc.</summary>
    private static SalaryPeriod? MapPeriod(string? unit) => unit?.Trim().ToLowerInvariant() switch
    {
        "hourly" or "godzinowo" => SalaryPeriod.Hourly,
        "daily" or "dziennie" => SalaryPeriod.Daily,
        "monthly" or "miesięcznie" => SalaryPeriod.Monthly,
        "yearly" or "annually" or "rocznie" => SalaryPeriod.Yearly,
        _ => null,
    };

    private static Currency? MapCurrency(string? symbol)
    {
        var code = symbol?.Trim() switch
        {
            "zł" or "PLN" => "PLN",
            "€" or "EUR" => "EUR",
            "$" or "USD" => "USD",
            "£" or "GBP" => "GBP",
            "CHF" => "CHF",
            "Kč" or "CZK" => "CZK",
            _ => null,
        };

        if (code is null)
        {
            return null;
        }

        var parsed = Currency.Create(code);
        return parsed.IsSuccess ? parsed.Value : null;
    }

    private static string? GetTimeUnit(JsonElement salary) =>
        salary.TryGetProperty("timeUnit", out var unit) && unit.ValueKind == JsonValueKind.Object
            ? GetString(unit, "longForm")
            : null;

    private static string? FirstString(JsonElement item, string property)
    {
        if (item.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in arr.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } value)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var prop)
        && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var prop)
        && prop.ValueKind == JsonValueKind.Number
        && prop.TryGetInt32(out var value)
            ? value
            : null;

    private static decimal? GetDecimal(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    private static DateTimeOffset? GetDate(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var prop)
            && prop.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(prop.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
        {
            return date;
        }

        return null;
    }
}
