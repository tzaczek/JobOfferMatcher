using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

internal sealed class ScanRunRepository(AppDbContext db) : IScanRunRepository
{
    public async Task AddAsync(ScanRun run, CancellationToken ct = default) =>
        await db.ScanRuns.AddAsync(run, ct);

    public Task<ScanRun?> GetByIdAsync(ScanRunId id, CancellationToken ct = default) =>
        db.ScanRuns.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<ScanRun>> GetRecentAsync(int take, CancellationToken ct = default) =>
        await db.ScanRuns.OrderByDescending(s => s.StartedAt).Take(take).ToListAsync(ct);

    public async Task<ScanRun?> GetLastCompleteForSourceAsync(SourceId sourceId, CancellationToken ct = default)
    {
        // source_ids is a jsonb column (not SQL-queryable by Contains) — filter in memory over the
        // recent complete runs. Low volume for a single user (research §3).
        var recentComplete = await db.ScanRuns
            .Where(s => s.Outcome == ScanOutcome.Complete)
            .OrderByDescending(s => s.StartedAt)
            .Take(200)
            .ToListAsync(ct);

        return recentComplete.FirstOrDefault(s => s.SourceIds.Contains(sourceId));
    }
}
