using System.Net;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Infrastructure.Sources.LinkedIn;

/// <summary>
/// Pure helpers for the LinkedIn adapter (feature 008): build a keyword-search URL from a saved
/// <see cref="LinkedInSearch"/> (US2) and infer a <see cref="WorkMode"/> from a card's location text.
/// Isolated + unit-tested so a URL-shape change touches one class.
/// </summary>
internal static class LinkedInUrlBuilder
{
    /// <summary>
    /// Fill the <c>SearchUrlTemplate</c> placeholders from a saved search. Keywords are URL-encoded;
    /// absent geoId/distance/recency collapse to empty (LinkedIn ignores empty params).
    /// </summary>
    public static string BuildSearchUrl(string template, LinkedInSearch search) =>
        template
            .Replace("{keywords}", WebUtility.UrlEncode(search.Keywords), StringComparison.Ordinal)
            .Replace("{geoId}", WebUtility.UrlEncode(search.GeoId ?? string.Empty), StringComparison.Ordinal)
            .Replace("{distance}", search.Distance?.ToString() ?? string.Empty, StringComparison.Ordinal)
            .Replace("{recency}", WebUtility.UrlEncode(search.Recency ?? string.Empty), StringComparison.Ordinal);

    /// <summary>
    /// LinkedIn shows the work mode inline in the card's location metadata (e.g. "Kraków (Remote)").
    /// Map that text to a <see cref="WorkMode"/>; unrecognized → <see cref="WorkMode.Unknown"/> (kept + flagged).
    /// </summary>
    public static WorkMode WorkModeFrom(string? locationText)
    {
        if (string.IsNullOrWhiteSpace(locationText))
        {
            return WorkMode.Unknown;
        }

        var text = locationText.ToLowerInvariant();
        if (text.Contains("remote", StringComparison.Ordinal))
        {
            return WorkMode.Remote;
        }

        if (text.Contains("hybrid", StringComparison.Ordinal))
        {
            return WorkMode.Hybrid;
        }

        if (text.Contains("on-site", StringComparison.Ordinal) || text.Contains("onsite", StringComparison.Ordinal))
        {
            return WorkMode.Office;
        }

        return WorkMode.Unknown;
    }
}
