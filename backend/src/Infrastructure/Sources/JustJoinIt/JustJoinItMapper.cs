using System.Globalization;
using System.Text.Json;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Infrastructure.Sources.JustJoinIt;

/// <summary>
/// Pure mapping from the justjoin.it payload → source-agnostic <see cref="CollectedOffer"/>
/// (contracts/justjoinit-payload.md). Isolated + unit-tested against recorded fixtures so an
/// upstream change touches one class (research §1 / accepted-risk ADR-2). No HTTP here.
/// </summary>
public static class JustJoinItMapper
{
    /// <summary>Map one LIST item. All core fields (skills, salary, work mode, dates) are in LIST.</summary>
    public static Result<CollectedOffer> MapListItem(JsonElement item, SourceId sourceId, string siteUrlTemplate)
    {
        if (!item.TryGetProperty("guid", out var guidProp) || guidProp.GetString() is not { Length: > 0 } guid)
        {
            return new Error("MissingGuid", "List item has no guid.");
        }

        var externalRef = ExternalRef.Create(sourceId, guid, IdentityKind.NativeId);
        if (externalRef.IsFailure)
        {
            return externalRef.Error;
        }

        var slug = GetString(item, "slug") ?? guid;
        var bands = MapSalaryBands(item);

        var content = new OfferContent
        {
            Title = GetString(item, "title") ?? "(untitled)",
            Company = GetString(item, "companyName") ?? "(unknown)",
            SalaryBands = bands,
            Location = MapPrimaryLocation(item),
            WorkMode = MapWorkMode(GetString(item, "workplaceType")),
            EmploymentType = MapEmploymentTypeLabel(bands),
            Seniority = GetString(item, "experienceLevel"),
            RequiredSkills = MapSkills(item, "requiredSkills"),
            NiceToHaveSkills = MapSkills(item, "niceToHaveSkills"),
            DescriptionHtml = null, // enriched from DETAIL when needed
            CanonicalUrl = siteUrlTemplate.Replace("{slug}", slug, StringComparison.Ordinal),
            PublishedAt = GetDate(item, "publishedAt"),
            LastPublishedAt = GetDate(item, "lastPublishedAt"),
            ExpiredAt = GetDate(item, "expiredAt"),
        };

        return new CollectedOffer(externalRef.Value, content);
    }

    /// <summary>Enrich an already-mapped offer with the DETAIL <c>body</c> (Minor tier, sanitized later).</summary>
    public static CollectedOffer WithDescription(CollectedOffer offer, JsonElement detail)
    {
        var body = GetString(detail, "body");
        if (string.IsNullOrWhiteSpace(body))
        {
            return offer;
        }

        return offer with { Content = offer.Content with { DescriptionHtml = body } };
    }

    /// <summary>True when the per-offer workplace value is in the keep-set; UNKNOWN is kept + flagged.</summary>
    public static bool MatchesWorkplaceKeep(string? workplaceType, IReadOnlyList<string> keep, out bool unknown)
    {
        unknown = MapWorkMode(workplaceType) == WorkMode.Unknown;
        if (keep.Count == 0 || unknown)
        {
            // Never silently drop an unrecognized value (contracts/justjoinit-payload.md).
            return true;
        }

        return keep.Any(k => string.Equals(k, workplaceType, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<SalaryBand> MapSalaryBands(JsonElement item)
    {
        if (!item.TryGetProperty("employmentTypes", out var types) || types.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var bands = new List<SalaryBand>();
        foreach (var entry in types.EnumerateArray())
        {
            var from = GetDecimal(entry, "from");
            var to = GetDecimal(entry, "to");
            var currencyCode = GetString(entry, "currency");

            // Hidden salary = no band at all (never a zero band) — FR-010.
            if (from is null && to is null && currencyCode is null)
            {
                continue;
            }

            Currency? currency = null;
            if (currencyCode is not null)
            {
                var parsed = Currency.Create(currencyCode);
                if (parsed.IsSuccess)
                {
                    currency = parsed.Value;
                }
            }

            bands.Add(new SalaryBand
            {
                AmountMin = from,
                AmountMax = to,
                Currency = currency,
                Period = MapPeriod(GetString(entry, "unit")),
                Basis = MapBasis(GetString(entry, "type")),
                Tax = MapTax(entry),
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

    private static string? MapPrimaryLocation(JsonElement item)
    {
        if (item.TryGetProperty("multilocation", out var locs) && locs.ValueKind == JsonValueKind.Array)
        {
            foreach (var loc in locs.EnumerateArray())
            {
                if (GetString(loc, "city") is { Length: > 0 } city)
                {
                    return city;
                }
            }
        }

        return GetString(item, "city");
    }

    private static IReadOnlyList<string> MapSkills(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var skills) || skills.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var skill in skills.EnumerateArray())
        {
            // Skills may be plain strings or { name: "..." } objects across payload versions.
            var name = skill.ValueKind == JsonValueKind.String ? skill.GetString() : GetString(skill, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                result.Add(name.Trim());
            }
        }

        return result;
    }

    private static WorkMode MapWorkMode(string? value) => value?.ToLowerInvariant() switch
    {
        "office" => WorkMode.Office,
        "remote" => WorkMode.Remote,
        "hybrid" => WorkMode.Hybrid,
        _ => WorkMode.Unknown,
    };

    private static SalaryPeriod? MapPeriod(string? unit) => unit?.ToLowerInvariant() switch
    {
        "hour" or "hourly" => SalaryPeriod.Hourly,
        "day" or "daily" => SalaryPeriod.Daily,
        "month" or "monthly" => SalaryPeriod.Monthly,
        "year" or "yearly" => SalaryPeriod.Yearly,
        _ => null,
    };

    private static EmploymentBasis MapBasis(string? type) => type?.ToLowerInvariant() switch
    {
        "b2b" => EmploymentBasis.B2B,
        "permanent" => EmploymentBasis.Permanent,
        _ => EmploymentBasis.Unknown,
    };

    private static TaxTreatment MapTax(JsonElement entry)
    {
        if (entry.TryGetProperty("gross", out var gross) && gross.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return gross.GetBoolean() ? TaxTreatment.Gross : TaxTreatment.Net;
        }

        return TaxTreatment.Unknown;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var prop)
        && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
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
