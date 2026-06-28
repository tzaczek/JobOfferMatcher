using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Domain.Settings;

/// <summary>
/// The user-set parts of the profile that don't come from the CV (research §4): salary expectation
/// and work-mode/employment preferences. Editable from the CV/Settings screen.
/// </summary>
public sealed record ProfilePreferences
{
    public decimal? SalaryFloor { get; init; }
    public decimal? SalaryTarget { get; init; }
    public IReadOnlyList<string> PreferredWorkModes { get; init; } = [];
    public IReadOnlyList<EmploymentBasis> PreferredEmployment { get; init; } = [];
}
