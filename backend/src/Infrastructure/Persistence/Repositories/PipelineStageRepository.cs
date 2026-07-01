using JobOfferMatcher.Application.Applications;
using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Common.Ids;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

/// <summary>EF adapter for the user-configurable pipeline stages (data-model §3). The service owns the commit.</summary>
internal sealed class PipelineStageRepository(AppDbContext db) : IPipelineStageRepository
{
    public async Task<IReadOnlyList<PipelineStage>> ListAsync(CancellationToken ct = default) =>
        await db.PipelineStages.AsNoTracking().OrderBy(s => s.Position).ThenBy(s => s.CreatedAt).ToListAsync(ct);

    public Task<PipelineStage?> GetAsync(PipelineStageId id, CancellationToken ct = default) =>
        db.PipelineStages.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task AddAsync(PipelineStage stage, CancellationToken ct = default) =>
        await db.PipelineStages.AddAsync(stage, ct);

    public void Remove(PipelineStage stage) => db.PipelineStages.Remove(stage);

    public Task<bool> AnyAsync(CancellationToken ct = default) => db.PipelineStages.AnyAsync(ct);

    public Task<int> CountApplicationsInStageAsync(PipelineStageId id, CancellationToken ct = default) =>
        db.Applications.CountAsync(a => a.CurrentStageId == id, ct);

    public Task ReassignApplicationsAsync(PipelineStageId from, PipelineStageId to, DateTimeOffset now, CancellationToken ct = default) =>
        db.Applications
            .Where(a => a.CurrentStageId == from)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(a => a.CurrentStageId, to)
                    .SetProperty(a => a.UpdatedAt, now),
                ct);
}
