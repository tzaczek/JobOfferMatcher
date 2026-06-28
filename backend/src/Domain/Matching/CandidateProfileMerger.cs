using JobOfferMatcher.Domain.Settings;

namespace JobOfferMatcher.Domain.Matching;

/// <summary>
/// Builds the effective <see cref="CandidateProfile"/> used for scoring: union the skills across all
/// readable CVs, take the MAX evidenced seniority (data-model §CandidateCv), and graft on the
/// user-set salary expectation + preferences (research §4).
/// </summary>
public static class CandidateProfileMerger
{
    public static CandidateProfile Merge(IEnumerable<CandidateProfile> cvProfiles, ProfilePreferences preferences)
    {
        var skills = cvProfiles
            .SelectMany(p => p.Skills)
            .GroupBy(s => s.CanonicalId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var seniority = cvProfiles
            .Select(p => p.Seniority)
            .DefaultIfEmpty(SeniorityLevel.Unknown)
            .Max();

        SalaryExpectation? expectation = preferences is { SalaryFloor: { } floor, SalaryTarget: { } target }
            ? new SalaryExpectation(floor, target)
            : null;

        return new CandidateProfile
        {
            Skills = skills,
            Seniority = seniority,
            SalaryExpectation = expectation,
            PreferredWorkModes = preferences.PreferredWorkModes,
            PreferredEmployment = preferences.PreferredEmployment,
        };
    }
}
