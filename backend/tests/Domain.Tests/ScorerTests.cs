using JobOfferMatcher.Domain.Matching;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>Unit tests (T048): the Scorer produces 0–100 + matched/missing for a high-fit and a low-fit case.</summary>
public sealed class ScorerTests
{
    private static readonly CandidateProfile Candidate = new()
    {
        Skills =
        [
            new SkillRef("csharp", "C#", ["c sharp"]),
            new SkillRef("dotnet", ".NET", ["dotnet"]),
            new SkillRef("azure", "Azure", []),
        ],
        Seniority = SeniorityLevel.Senior,
        SalaryExpectation = new SalaryExpectation(Floor: 15000, Target: 18000),
        PreferredWorkModes = ["remote"],
        PreferredEmployment = [EmploymentBasis.B2B],
    };

    [Fact]
    public void Weights_sum_to_100()
    {
        ScoringWeights.Default.Total.ShouldBe(100);
    }

    [Fact]
    public void Worked_example_A_is_a_high_fit_with_no_gaps()
    {
        var offer = new ScoringInput(
            RequiredSkills: ["C#", ".NET", "Azure"],
            NiceToHaveSkills: [],
            Seniority: SeniorityLevel.Senior,
            WorkMode: WorkMode.Remote,
            EmploymentBases: [EmploymentBasis.B2B],
            NormalizedMonthly: 20000);

        var fit = Scorer.Score(offer, Candidate, ScoringWeights.Default);

        fit.Value.ShouldBeGreaterThanOrEqualTo(95);
        fit.Breakdown.Missing.ShouldBeEmpty();
        fit.Breakdown.Matched.ShouldContain("C#");
        fit.Breakdown.Matched.ShouldContain(m => m.Contains("seniority meets"));
        fit.Breakdown.Matched.ShouldContain(m => m.Contains("salary meets target"));
        fit.Breakdown.Matched.ShouldContain("remote");
    }

    [Fact]
    public void Worked_example_B_is_a_low_fit_with_explicit_gaps()
    {
        var offer = new ScoringInput(
            RequiredSkills: ["C#", "Java", "Kotlin"],
            NiceToHaveSkills: [],
            Seniority: SeniorityLevel.Lead,
            WorkMode: WorkMode.Hybrid,
            EmploymentBases: [EmploymentBasis.Permanent],
            NormalizedMonthly: 14000);

        var fit = Scorer.Score(offer, Candidate, ScoringWeights.Default);

        fit.Value.ShouldBeInRange(30, 45); // ≈37
        fit.Breakdown.Missing.ShouldContain("Java");
        fit.Breakdown.Missing.ShouldContain("Kotlin");
        fit.Breakdown.Matched.ShouldContain("C#"); // the one skill they do have
    }

    [Fact]
    public void Score_is_bounded_0_to_100()
    {
        var empty = new ScoringInput([], [], SeniorityLevel.Unknown, WorkMode.Unknown, [], null);
        var fit = Scorer.Score(empty, new CandidateProfile(), ScoringWeights.Default);
        fit.Value.ShouldBeInRange(0, 100);
    }
}
