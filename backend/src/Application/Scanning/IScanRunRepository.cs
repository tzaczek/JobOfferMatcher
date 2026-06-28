using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>Persistence port for immutable <see cref="ScanRun"/> records (FR-020).</summary>
public interface IScanRunRepository
{
    Task AddAsync(ScanRun run, CancellationToken ct = default);
    Task<ScanRun?> GetByIdAsync(ScanRunId id, CancellationToken ct = default);
    Task<IReadOnlyList<ScanRun>> GetRecentAsync(int take, CancellationToken ct = default);

    /// <summary>The most recent finished run for a source, used by the catch-up &amp; sanity-guard logic.</summary>
    Task<ScanRun?> GetLastCompleteForSourceAsync(SourceId sourceId, CancellationToken ct = default);
}
