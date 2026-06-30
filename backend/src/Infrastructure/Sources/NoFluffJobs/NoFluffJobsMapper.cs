using System.Globalization;
using System.Text.Json;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Infrastructure.Sources.NoFluffJobs;

/// <summary>
/// Pure mapping from the nofluffjobs.com posting payload → source-agnostic <see cref="CollectedOffer"/>.
/// Isolated + unit-tested against recorded fixtures so an upstream change touches one class
/// (research §1 / accepted-risk ADR-2). No HTTP here. Identity is the per-offer <c>reference</c>
/// (stable across the multilocation expansion — many rows, one job).
/// </summary>
public static class NoFluffJobsMapper
{
    /// <summary>The stable cross-location identity of a posting (collapses the per-province expansion).</summary>
    public static string? GetReference(JsonElement item) =>
        GetString(item, "reference") is { Length: > 0 } reference ? reference : null;

    public static Result<CollectedOffer> MapListItem(JsonElement item, SourceId sourceId, string siteUrlTemplate)
    {
        if (GetReference(item) is not { } reference)
        {
            return new Error("MissingReference", "Posting has no reference.");
        }

        var externalRef = ExternalRef.Create(sourceId, reference, IdentityKind.NativeId);
        if (externalRef.IsFailure)
        {
            return externalRef.Error;
        }

        var urlSlug = GetString(item, "url") ?? reference;
        var bands = MapSalaryBands(item);

        var content = new OfferContent
        {
            Title = GetString(item, "title") ?? "(untitled)",
            Company = GetString(item, "name") ?? "(unknown)",
            SalaryBands = bands,
            Location = MapPrimaryLocation(item),
            WorkMode = MapWorkMode(item),
            EmploymentType = MapEmploymentTypeLabel(bands),
            Seniority = MapSeniority(item),
            RequiredSkills = MapRequirementTiles(item),
            NiceToHaveSkills = [],
            DescriptionHtml = null,
            CanonicalUrl = siteUrlTemplate.Replace("{url}", urlSlug, StringComparison.Ordinal),
            PublishedAt = GetEpochMs(item, "posted"),
            LastPublishedAt = GetEpochMs(item, "renewed"),
            ExpiredAt = null,
        };

        return new CollectedOffer(externalRef.Value, content);
    }

    private static IReadOnlyList<SalaryBand> MapSalaryBands(JsonElement item)
    {
        if (!item.TryGetProperty("salary", out var salary) || salary.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        // Hidden salary → no band at all (never a zero band) — FR-010.
        if (GetString(salary, "disclosedAt") is { } disclosed && !disclosed.Equals("VISIBLE", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var from = GetDecimal(salary, "from");
        var to = GetDecimal(salary, "to");
        var currencyCode = GetString(salary, "currency");
        if (from is null && to is null && currencyCode is null)
        {
            return [];
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

        return
        [
            new SalaryBand
            {
                AmountMin = from,
                AmountMax = to,
                Currency = currency,
                // The search is requested in a monthly period, so figures are monthly-equivalent.
                Period = SalaryPeriod.Monthly,
                Basis = MapBasis(GetString(salary, "type")),
                Tax = TaxTreatment.Unknown, // nofluffjobs LIST does not state gross/net.
            },
        ];
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
        if (item.TryGetProperty("location", out var location) && location.ValueKind == JsonValueKind.Object)
        {
            if (location.TryGetProperty("places", out var places) && places.ValueKind == JsonValueKind.Array)
            {
                foreach (var place in places.EnumerateArray())
                {
                    if (GetString(place, "city") is { Length: > 0 } city)
                    {
                        return city;
                    }
                }
            }

            if (location.TryGetProperty("fullyRemote", out var fr) && fr.ValueKind == JsonValueKind.True)
            {
                return "Remote";
            }
        }

        return null;
    }

    private static WorkMode MapWorkMode(JsonElement item)
    {
        if (!item.TryGetProperty("location", out var location) || location.ValueKind != JsonValueKind.Object)
        {
            return WorkMode.Unknown;
        }

        if (location.TryGetProperty("fullyRemote", out var fr) && fr.ValueKind == JsonValueKind.True)
        {
            return WorkMode.Remote;
        }

        // A hybrid hint (e.g. "2 office visits/month") distinguishes hybrid from full-office.
        if (GetString(location, "hybridDesc") is { Length: > 0 })
        {
            return WorkMode.Hybrid;
        }

        var hasPlace = location.TryGetProperty("places", out var places)
            && places.ValueKind == JsonValueKind.Array
            && places.GetArrayLength() > 0;

        return hasPlace ? WorkMode.Office : WorkMode.Unknown;
    }

    private static string? MapSeniority(JsonElement item)
    {
        if (item.TryGetProperty("seniority", out var seniority) && seniority.ValueKind == JsonValueKind.Array)
        {
            foreach (var level in seniority.EnumerateArray())
            {
                if (level.ValueKind == JsonValueKind.String && level.GetString() is { Length: > 0 } value)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> MapRequirementTiles(JsonElement item)
    {
        if (!item.TryGetProperty("tiles", out var tiles) || tiles.ValueKind != JsonValueKind.Object
            || !tiles.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var tile in values.EnumerateArray())
        {
            if (string.Equals(GetString(tile, "type"), "requirement", StringComparison.OrdinalIgnoreCase)
                && GetString(tile, "value") is { Length: > 0 } value)
            {
                result.Add(value.Trim());
            }
        }

        return result;
    }

    private static EmploymentBasis MapBasis(string? type) => type?.ToLowerInvariant() switch
    {
        "b2b" => EmploymentBasis.B2B,
        "permanent" => EmploymentBasis.Permanent,
        _ => EmploymentBasis.Unknown,
    };

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

    private static DateTimeOffset? GetEpochMs(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt64(out var ms))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }

        return null;
    }
}
