using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Domain.Matching;

/// <summary>The offer attributes the scorer needs (source-agnostic, derived at the boundary).</summary>
public sealed record ScoringInput(
    IReadOnlyList<string> RequiredSkills,
    IReadOnlyList<string> NiceToHaveSkills,
    SeniorityLevel Seniority,
    WorkMode WorkMode,
    IReadOnlyList<EmploymentBasis> EmploymentBases,
    decimal? NormalizedMonthly);

/// <summary>
/// Transparent 0–100 fit model (research §4): Skills 45 / Seniority 20 / WorkMode 12 / Employment 8
/// / Salary 15. Each axis yields a 0..1 value AND human reasons → the explicit matched/missing
/// breakdown (FR-025). Pure Domain. Ranking + graceful degradation formulas live here too.
/// </summary>
public static class Scorer
{
    public static FitScore Score(ScoringInput offer, CandidateProfile profile, ScoringWeights weights)
    {
        var matched = new List<string>();
        var missing = new List<string>();

        var skills = ScoreSkills(offer, profile, matched, missing);
        var seniority = ScoreSeniority(offer, profile, matched, missing);
        var workMode = ScoreWorkMode(offer, profile, matched, missing);
        var employment = ScoreEmployment(offer, profile, matched, missing);
        var salary = ScoreSalary(offer, profile, matched, missing);

        var total =
            (skills * weights.Skills)
            + (seniority * weights.Seniority)
            + (workMode * weights.WorkMode)
            + (employment * weights.Employment)
            + (salary * weights.Salary);

        var value = (int)Math.Round(total, MidpointRounding.AwayFromZero);
        value = Math.Clamp(value, 0, 100);

        return new FitScore(value, new FitBreakdown(matched, missing, skills, seniority, workMode, employment, salary));
    }

    /// <summary>Default sort with a CV present: fit-weighted with a salary tilt (0..100 each).</summary>
    public static double CombinedRank(int fitScore, double normalizedSalaryScore) =>
        (0.70 * fitScore) + (0.30 * normalizedSalaryScore);

    /// <summary>Graceful degradation with no readable CV (FR-026): salary + recency only (0..100 each).</summary>
    public static double DegradedRank(double normalizedSalaryScore, double recencyScore) =>
        (0.60 * normalizedSalaryScore) + (0.40 * recencyScore);

    private static double ScoreSkills(ScoringInput offer, CandidateProfile profile, List<string> matched, List<string> missing)
    {
        var forms = profile.SkillForms();

        var requiredMatched = 0;
        foreach (var skill in offer.RequiredSkills)
        {
            if (forms.Contains(skill.Trim().ToLowerInvariant()))
            {
                requiredMatched++;
                matched.Add(skill);
            }
            else
            {
                missing.Add(skill);
            }
        }

        var niceMatched = offer.NiceToHaveSkills.Count(s => forms.Contains(s.Trim().ToLowerInvariant()));
        foreach (var skill in offer.NiceToHaveSkills.Where(s => forms.Contains(s.Trim().ToLowerInvariant())))
        {
            matched.Add($"{skill} (nice-to-have)");
        }

        double? requiredFrac = offer.RequiredSkills.Count == 0 ? null : (double)requiredMatched / offer.RequiredSkills.Count;
        double? niceFrac = offer.NiceToHaveSkills.Count == 0 ? null : (double)niceMatched / offer.NiceToHaveSkills.Count;

        return (requiredFrac, niceFrac) switch
        {
            ({ } req, { } nice) => (0.8 * req) + (0.2 * nice),
            ({ } req, null) => req,
            (null, { } nice) => 0.5 + (0.3 * nice),
            _ => 0.5, // requirements unknown → neutral (FR-010)
        };
    }

    private static double ScoreSeniority(ScoringInput offer, CandidateProfile profile, List<string> matched, List<string> missing)
    {
        if (offer.Seniority == SeniorityLevel.Unknown)
        {
            return 0.6;
        }

        if (profile.Seniority == SeniorityLevel.Unknown)
        {
            missing.Add("seniority unknown in CV");
            return 0.4;
        }

        var diff = (int)profile.Seniority - (int)offer.Seniority;
        if (diff >= 0)
        {
            matched.Add($"seniority meets ({profile.Seniority} ≥ {offer.Seniority})");
            return 1.0;
        }

        if (diff == -1)
        {
            return 0.5;
        }

        missing.Add($"seniority below ({profile.Seniority} < {offer.Seniority})");
        return 0.1;
    }

    private static double ScoreWorkMode(ScoringInput offer, CandidateProfile profile, List<string> matched, List<string> missing)
    {
        if (profile.PreferredWorkModes.Count == 0)
        {
            return 0.5;
        }

        if (offer.WorkMode == WorkMode.Unknown)
        {
            return 0.5;
        }

        var mode = offer.WorkMode.ToString();
        if (profile.PreferredWorkModes.Any(p => string.Equals(p, mode, StringComparison.OrdinalIgnoreCase)))
        {
            matched.Add(mode.ToLowerInvariant());
            return 1.0;
        }

        missing.Add($"{mode.ToLowerInvariant()} (you prefer {string.Join("/", profile.PreferredWorkModes)})");
        return 0.2;
    }

    private static double ScoreEmployment(ScoringInput offer, CandidateProfile profile, List<string> matched, List<string> missing)
    {
        if (profile.PreferredEmployment.Count == 0)
        {
            return 0.5;
        }

        var bases = offer.EmploymentBases.Where(b => b != EmploymentBasis.Unknown).ToList();
        if (bases.Count == 0)
        {
            return 0.5;
        }

        var hit = bases.FirstOrDefault(b => profile.PreferredEmployment.Contains(b));
        if (hit != EmploymentBasis.Unknown && profile.PreferredEmployment.Contains(hit))
        {
            matched.Add(hit.ToString());
            return 1.0;
        }

        missing.Add($"employment {string.Join("/", bases)} (you prefer {string.Join("/", profile.PreferredEmployment)})");
        return 0.3;
    }

    private static double ScoreSalary(ScoringInput offer, CandidateProfile profile, List<string> matched, List<string> missing)
    {
        if (offer.NormalizedMonthly is not { } monthly || profile.SalaryExpectation is not { } expectation)
        {
            return 0.5; // not comparable → neutral, doesn't penalize
        }

        if (monthly >= expectation.Target)
        {
            matched.Add("salary meets target");
            return 1.0;
        }

        if (monthly >= expectation.Floor && expectation.Target > expectation.Floor)
        {
            matched.Add("salary in band");
            var frac = (double)((monthly - expectation.Floor) / (expectation.Target - expectation.Floor));
            return 0.5 + (0.5 * frac);
        }

        missing.Add("salary below your floor");
        return expectation.Floor > 0 ? Math.Clamp((double)(monthly / expectation.Floor) * 0.5, 0, 0.5) : 0.3;
    }
}
