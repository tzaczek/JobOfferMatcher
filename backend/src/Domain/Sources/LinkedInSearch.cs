using JobOfferMatcher.Domain.Common;

namespace JobOfferMatcher.Domain.Sources;

/// <summary>
/// One saved LinkedIn keyword search (feature 008, US2), stored inside the LinkedIn source's
/// <see cref="JobSourceSearch.LinkedInSearches"/> jsonb list. Each search is an independent collection
/// pass whose fields map to the LinkedIn search-results URL params (contracts/linkedin-source.md §7).
/// Immutable value object with structural equality; <see cref="Keywords"/> is required and non-blank.
/// </summary>
public sealed record LinkedInSearch
{
    /// <summary>Free-text keywords (LinkedIn <c>keywords=</c>). Required, non-blank — see <see cref="Create"/>.</summary>
    public required string Keywords { get; init; }

    /// <summary>Human-readable location label (display only; the machine filter is <see cref="GeoId"/>).</summary>
    public string? Location { get; init; }

    /// <summary>LinkedIn <c>geoId=</c> (e.g. <c>90009828</c>) — the machine-readable location filter.</summary>
    public string? GeoId { get; init; }

    /// <summary>LinkedIn <c>distance=</c> in miles (e.g. <c>50</c>).</summary>
    public int? Distance { get; init; }

    /// <summary>LinkedIn <c>f_TPR=</c> recency window (e.g. <c>r1296000</c> = last 15 days).</summary>
    public string? Recency { get; init; }

    /// <summary>
    /// Create a validated search. <see cref="Keywords"/> must be non-blank (a search with no keywords is
    /// meaningless and would collect the whole board). Other fields are optional.
    /// </summary>
    public static Result<LinkedInSearch> Create(
        string? keywords,
        string? location = null,
        string? geoId = null,
        int? distance = null,
        string? recency = null)
    {
        if (string.IsNullOrWhiteSpace(keywords))
        {
            return new Error("InvalidLinkedInSearch", "A LinkedIn saved search requires non-blank keywords.");
        }

        return new LinkedInSearch
        {
            Keywords = keywords.Trim(),
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            GeoId = string.IsNullOrWhiteSpace(geoId) ? null : geoId.Trim(),
            Distance = distance,
            Recency = string.IsNullOrWhiteSpace(recency) ? null : recency.Trim(),
        };
    }
}
