using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Domain.RoleGroups;

/// <summary>The fields the grouping gate compares (source-agnostic).</summary>
public sealed record GroupingCandidate(string Company, string Title, string? Location, WorkMode WorkMode);

/// <summary>
/// Conservative, deterministic cross-source grouping gate (research §6.4, FR-016): company EXACT
/// after normalization, title token-set ≥ 0.85, location compatible (same city or both remote/hybrid).
/// Below the merge threshold → no merge (default to separate). No ML.
/// </summary>
public static class RoleGroupMatcher
{
    public static MatchConfidence Evaluate(GroupingCandidate a, GroupingCandidate b)
    {
        if (!CompanyMatches(a.Company, b.Company) || !LocationCompatible(a, b))
        {
            return MatchConfidence.None;
        }

        var similarity = CompanyTitleNormalizer.TitleSimilarity(a.Title, b.Title);
        return similarity >= MatchConfidence.MergeThreshold ? new MatchConfidence(similarity) : MatchConfidence.None;
    }

    private static bool CompanyMatches(string a, string b)
    {
        var na = CompanyTitleNormalizer.NormalizeCompany(a);
        var nb = CompanyTitleNormalizer.NormalizeCompany(b);
        return na.Length > 0 && string.Equals(na, nb, StringComparison.Ordinal);
    }

    private static bool LocationCompatible(GroupingCandidate a, GroupingCandidate b)
    {
        var aRemoteish = a.WorkMode is WorkMode.Remote or WorkMode.Hybrid;
        var bRemoteish = b.WorkMode is WorkMode.Remote or WorkMode.Hybrid;
        if (aRemoteish && bRemoteish)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(a.Location) || string.IsNullOrWhiteSpace(b.Location))
        {
            return false;
        }

        return string.Equals(a.Location.Trim(), b.Location.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
