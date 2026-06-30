using System.Text.Json;

namespace JobOfferMatcher.Infrastructure.Sources.TheProtocol;

/// <summary>
/// Extracts theprotocol's server-rendered offer list from the page's
/// <c>&lt;script id="__NEXT_DATA__"&gt;</c> JSON blob (<c>props.pageProps.offersResponse</c>).
/// Next.js escapes <c>&lt;</c> as <c><</c> inside that blob, so a raw <c>&lt;/script&gt;</c>
/// never appears in the JSON and the closing-tag scan is safe. Isolated so the one HTML-parsing step
/// is unit-tested directly while the mapper/pagination tests stay on pure JSON fixtures.
/// </summary>
public static class NextDataExtractor
{
    private const string OpenTag = "id=\"__NEXT_DATA__\"";
    private const string CloseTag = "</script>";

    /// <summary>
    /// Returns the <c>offersResponse</c> object (with <c>page</c> + <c>offers</c>), or null when the
    /// page lacks it (e.g. an anti-bot interstitial). The element is cloned to outlive the document.
    /// </summary>
    public static JsonElement? ExtractOffersResponse(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        var tagIndex = html.IndexOf(OpenTag, StringComparison.Ordinal);
        if (tagIndex < 0)
        {
            return null;
        }

        var jsonStart = html.IndexOf('>', tagIndex);
        if (jsonStart < 0)
        {
            return null;
        }

        jsonStart++; // move past '>'
        var jsonEnd = html.IndexOf(CloseTag, jsonStart, StringComparison.Ordinal);
        if (jsonEnd < 0)
        {
            return null;
        }

        var json = html[jsonStart..jsonEnd];

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("props", out var props)
                && props.TryGetProperty("pageProps", out var pageProps)
                && pageProps.TryGetProperty("offersResponse", out var offersResponse))
            {
                return offersResponse.Clone();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
