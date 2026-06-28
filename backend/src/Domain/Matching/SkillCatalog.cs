namespace JobOfferMatcher.Domain.Matching;

/// <summary>
/// The curated, editable skill catalog (research §4). Provides alias-exact lookup by any recognized
/// form; fuzzy matching is layered on in Infrastructure (FuzzySharp). The matching accuracy lever.
/// </summary>
public sealed class SkillCatalog
{
    private readonly Dictionary<string, SkillRef> _byForm;

    public SkillCatalog(IReadOnlyList<SkillRef> skills)
    {
        Skills = skills;
        _byForm = new Dictionary<string, SkillRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in skills)
        {
            foreach (var form in skill.AllForms())
            {
                _byForm.TryAdd(form, skill);
            }
        }
    }

    public IReadOnlyList<SkillRef> Skills { get; }

    /// <summary>Resolve a token to a canonical skill by exact form/alias match, or null.</summary>
    public SkillRef? MatchExact(string token) =>
        _byForm.TryGetValue(token.Trim().ToLowerInvariant(), out var skill) ? skill : null;
}
