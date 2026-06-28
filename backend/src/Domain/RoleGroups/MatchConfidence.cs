namespace JobOfferMatcher.Domain.RoleGroups;

/// <summary>
/// Cross-source grouping confidence 0..1 (data-model §Cross-source). Auto-merge only at ≥
/// <see cref="MergeThreshold"/>; below that, default to NOT merging (a missed merge is cheaper than
/// a false one — research §6.4).
/// </summary>
public readonly record struct MatchConfidence(double Value)
{
    public const double MergeThreshold = 0.85;

    public bool MeetsMergeThreshold => Value >= MergeThreshold;

    public static readonly MatchConfidence None = new(0);
}
