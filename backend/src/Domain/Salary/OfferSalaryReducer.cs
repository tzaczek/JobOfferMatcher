namespace JobOfferMatcher.Domain.Salary;

/// <summary>
/// Reduces an offer's (possibly multiple) raw bands to one comparable figure (research §7):
/// normalize each band, then prefer the band matching the candidate's preferred basis if set, else
/// the MAX across successfully-normalized bands. Returns null when nothing can be normalized.
/// </summary>
public static class OfferSalaryReducer
{
    public static NormalizedSalary? BestComparable(
        IReadOnlyList<SalaryBand> bands,
        SalaryNormalizationSettings settings,
        EmploymentBasis? preferredBasis)
    {
        var normalized = bands
            .Select(b => SalaryNormalizer.Normalize(b, settings))
            .Where(r => r.IsSuccess)
            .Select(r => r.Value)
            .ToList();

        if (normalized.Count == 0)
        {
            return null;
        }

        if (preferredBasis is { } preferred)
        {
            var match = normalized.FirstOrDefault(n => n.Source.Basis == preferred);
            if (match is not null)
            {
                return match;
            }
        }

        return normalized.OrderByDescending(n => n.ComparableMonthly.Amount).First();
    }
}
