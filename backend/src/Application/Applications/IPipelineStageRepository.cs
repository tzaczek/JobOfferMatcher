using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Application.Applications;

/// <summary>
/// Persistence port for the user-configurable pipeline stages (data-model §3, FR-019). Adapters stage
/// changes; the <see cref="ApplicationTrackingService"/>/<see cref="PipelineStageService"/> own the commit
/// via <c>IUnitOfWork</c> (no <c>SaveChanges</c> in the repository — mirrors the 001–004 repos).
/// </summary>
public interface IPipelineStageRepository
{
    /// <summary>All stages ordered by position (the pipeline). Read-only.</summary>
    Task<IReadOnlyList<PipelineStage>> ListAsync(CancellationToken ct = default);

    /// <summary>A tracked stage (for rename/reorder/remove), or null if it doesn't exist.</summary>
    Task<PipelineStage?> GetAsync(PipelineStageId id, CancellationToken ct = default);

    Task AddAsync(PipelineStage stage, CancellationToken ct = default);

    void Remove(PipelineStage stage);

    /// <summary>True when at least one stage exists (drives seed-if-empty / the create-first invariant).</summary>
    Task<bool> AnyAsync(CancellationToken ct = default);

    /// <summary>How many applications currently sit in this stage (the remove-guard: never orphan).</summary>
    Task<int> CountApplicationsInStageAsync(PipelineStageId id, CancellationToken ct = default);

    /// <summary>Move every application in <paramref name="from"/> to <paramref name="to"/> (reassign-before-remove).</summary>
    Task ReassignApplicationsAsync(PipelineStageId from, PipelineStageId to, DateTimeOffset now, CancellationToken ct = default);
}
