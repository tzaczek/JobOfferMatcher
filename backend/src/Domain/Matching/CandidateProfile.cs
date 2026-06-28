using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Domain.Matching;

/// <summary>The user's salary expectation (from config, not the CV — research §4).</summary>
public sealed record SalaryExpectation(decimal Floor, decimal Target);

/// <summary>
/// The candidate profile derived from CV(s) + user config (data-model §Matching). Skills/seniority
/// come from the CV; salary expectation + preferences come from user config.
/// </summary>
public sealed record CandidateProfile
{
    public IReadOnlyList<SkillRef> Skills { get; init; } = [];
    public SeniorityLevel Seniority { get; init; } = SeniorityLevel.Unknown;
    public SalaryExpectation? SalaryExpectation { get; init; }
    public IReadOnlyList<string> PreferredWorkModes { get; init; } = [];
    public IReadOnlyList<EmploymentBasis> PreferredEmployment { get; init; } = [];

    /// <summary>Lower-cased set of every recognized form of every profile skill (for fast matching).</summary>
    public ISet<string> SkillForms() =>
        Skills.SelectMany(s => s.AllForms()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool HasSkills => Skills.Count > 0;
}
