using System.Globalization;
using System.Text;
using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Infrastructure.Sources.TheProtocol;

/// <summary>
/// Builds the theprotocol.it listing path+query from the saved search, shared by both transports
/// (HttpClient and the Playwright browser fetch). The category path is already percent-encoded
/// (e.g. <c>c%23;t</c>) and must NOT be re-encoded.
/// </summary>
internal static class TheProtocolRequest
{
    public static string BuildListPathAndQuery(TheProtocolOptions options, JobSourceSearch search, int page)
    {
        var path = search.Categories.Count > 0
            ? string.Join(",", search.Categories.Select(c => Uri.EscapeDataString(c).ToLowerInvariant() + ";t"))
            : options.DefaultSearchPath;

        var sb = new StringBuilder(options.ListPathPrefix);
        sb.Append(path); // already percent-encoded (e.g. "c%23;t"); do not re-encode
        sb.Append("?sort=").Append(Uri.EscapeDataString(options.Sort));
        sb.Append("&pageNumber=").Append(page.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
