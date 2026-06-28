namespace JobOfferMatcher.Domain.Matching;

/// <summary>Ordinal seniority ladder (data-model §Matching). The profile takes the MAX evidenced level.</summary>
public enum SeniorityLevel
{
    Unknown = 0,
    Intern,
    Junior,
    Mid,
    Senior,
    Lead,
    Principal,
    Architect,
}

public static class SeniorityLevels
{
    /// <summary>Map a source/CV seniority label to a level (tolerant of synonyms). Unknown if unrecognized.</summary>
    public static SeniorityLevel Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SeniorityLevel.Unknown;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "intern" or "internship" or "trainee" => SeniorityLevel.Intern,
            "junior" or "jr" => SeniorityLevel.Junior,
            "mid" or "middle" or "regular" => SeniorityLevel.Mid,
            "senior" or "sr" => SeniorityLevel.Senior,
            "lead" or "team lead" or "tech lead" => SeniorityLevel.Lead,
            "principal" or "staff" => SeniorityLevel.Principal,
            "architect" => SeniorityLevel.Architect,
            _ => SeniorityLevel.Unknown,
        };
    }
}
