namespace JobOfferMatcher.Domain.Scans;

/// <summary>Per-run result tallies recorded on the immutable <see cref="ScanRun"/> (FR-020).</summary>
public sealed record ScanCounts(int Collected, int New, int Updated, int Unavailable, int Failed)
{
    public static ScanCounts Zero { get; } = new(0, 0, 0, 0, 0);
}
