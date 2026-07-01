using JobOfferMatcher.Domain.TailoredCvs;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>
/// TailoredCvPrompt.BuildDefault unit tests (T006): emphasised skills appear in the visible prompt
/// (FR-004), the no-fabrication clause (FR-006) and the cv_versions two-column layout instruction
/// (FR-007) are present, and the composer is deterministic.
/// </summary>
public sealed class TailoredCvPromptTests
{
    private static readonly TailoredCvOfferView Offer = new("Senior .NET Engineer", "Acme", "Senior");

    [Fact]
    public void Emphasised_skills_appear_in_the_prompt()
    {
        var prompt = TailoredCvPrompt.BuildDefault(Offer, ["PostgreSQL", "EF Core"]);

        prompt.ShouldContain("PostgreSQL");
        prompt.ShouldContain("EF Core");
        prompt.ShouldContain("Senior .NET Engineer");
        prompt.ShouldContain("Acme");
    }

    [Fact]
    public void Prompt_carries_the_no_fabrication_rule()
    {
        var prompt = TailoredCvPrompt.BuildDefault(Offer, ["C#"]);

        prompt.ShouldContain("ONLY information");
        prompt.ShouldContain("NEVER invent");
    }

    [Fact]
    public void Prompt_carries_the_cv_versions_two_column_layout_instruction()
    {
        var prompt = TailoredCvPrompt.BuildDefault(Offer, ["C#"]);

        prompt.ShouldContain("cv_versions");
        prompt.ShouldContain("two-column");
        prompt.ShouldContain("A4");
        prompt.ShouldContain("self-contained HTML");
    }

    [Fact]
    public void Composer_is_deterministic()
    {
        var a = TailoredCvPrompt.BuildDefault(Offer, ["C#", "React"]);
        var b = TailoredCvPrompt.BuildDefault(Offer, ["C#", "React"]);

        a.ShouldBe(b);
    }

    [Fact]
    public void Empty_skills_still_produces_a_usable_prompt_without_a_bullet_list()
    {
        var prompt = TailoredCvPrompt.BuildDefault(Offer, []);

        prompt.ShouldContain("Senior .NET Engineer");
        prompt.ShouldContain("NEVER invent");
        prompt.ShouldNotContain("\n  - "); // no empty bullet list
    }
}
