namespace JobOfferMatcher.Domain.Matching;

/// <summary>
/// Transparent fit-model weights (research §4), summing to 100. Editable from Settings. Each axis
/// contributes <c>axisScore (0..1) × weight</c> to the 0–100 fit score.
/// </summary>
public sealed record ScoringWeights(int Skills, int Seniority, int WorkMode, int Employment, int Salary)
{
    public static ScoringWeights Default { get; } = new(Skills: 45, Seniority: 20, WorkMode: 12, Employment: 8, Salary: 15);

    public int Total => Skills + Seniority + WorkMode + Employment + Salary;
}
