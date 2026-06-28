using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// Runs a scan (collect → upsert → observe → record). The SAME runner is called by the on-demand
/// API and the scheduler (research §3), so behavior is identical regardless of trigger.
/// </summary>
public interface IScanRunner
{
    Task<Result<ScanRunId>> RunAsync(ScanRequest request, CancellationToken ct = default);
}

/// <summary>What to scan and why. <c>SourceIds == null</c> means all enabled sources.</summary>
public sealed record ScanRequest(
    IReadOnlyList<SourceId>? SourceIds,
    TriggerType Trigger,
    DateTimeOffset? WindowUtc = null)
{
    public static ScanRequest Manual(IReadOnlyList<SourceId>? sourceIds = null) =>
        new(sourceIds, TriggerType.Manual);
}
