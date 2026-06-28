namespace JobOfferMatcher.Domain.Matching;

/// <summary>A canonical skill from the editable catalog (data-model §Matching).</summary>
public sealed record SkillRef(string CanonicalId, string DisplayName, IReadOnlyList<string> Aliases)
{
    /// <summary>All recognized spellings (display name + aliases), lower-cased, for matching.</summary>
    public IEnumerable<string> AllForms() =>
        Aliases.Append(DisplayName).Append(CanonicalId).Select(a => a.Trim().ToLowerInvariant());
}
