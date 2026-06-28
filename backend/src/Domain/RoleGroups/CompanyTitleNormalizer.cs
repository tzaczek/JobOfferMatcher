using System.Text.RegularExpressions;

namespace JobOfferMatcher.Domain.RoleGroups;

/// <summary>
/// Normalizes company names + tokenizes titles for conservative cross-source grouping (research §6.4):
/// strip legal suffixes (<c>sp. z o.o.</c>/<c>S.A.</c>/<c>Ltd</c>/<c>GmbH</c>/<c>Inc</c>…) so the same
/// employer matches across sources. Pure Domain (no FuzzySharp); title similarity is a token-set ratio.
/// </summary>
public static partial class CompanyTitleNormalizer
{
    private static readonly string[] LegalSuffixes =
    [
        "sp. z o.o.", "sp. z o. o.", "spółka z o.o.", "s.a.", "s. a.", "sp.k.",
        "ltd.", "ltd", "limited", "gmbh", "inc.", "inc", "llc", "co.", "corp.", "corp", "s.r.o.", "b.v.",
    ];

    public static string NormalizeCompany(string company)
    {
        var value = company.Trim().ToLowerInvariant();
        foreach (var suffix in LegalSuffixes)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                value = value[..^suffix.Length];
            }
        }

        value = PunctuationRegex().Replace(value, " ");
        return WhitespaceRegex().Replace(value, " ").Trim();
    }

    public static IReadOnlySet<string> TitleTokens(string title) =>
        TokenRegex().Matches(title.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => t.Length >= 2)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Token-set similarity 0..1: shared tokens over the larger title's token count.</summary>
    public static double TitleSimilarity(string a, string b)
    {
        var ta = TitleTokens(a);
        var tb = TitleTokens(b);
        if (ta.Count == 0 || tb.Count == 0)
        {
            return 0;
        }

        var shared = ta.Count(tb.Contains);
        return (double)shared / Math.Max(ta.Count, tb.Count);
    }

    [GeneratedRegex(@"[^a-z0-9+#.\s]")]
    private static partial Regex PunctuationRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[a-z0-9][a-z0-9+#.]*")]
    private static partial Regex TokenRegex();
}
