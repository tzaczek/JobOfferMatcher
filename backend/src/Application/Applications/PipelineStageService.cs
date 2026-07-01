using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Application.Applications;

/// <summary>
/// Manages the user-configurable pipeline stages (FR-019): list, create-at-end, rename, reorder, and
/// remove-with-reassign (never orphan an application — the load-bearing edge case). Writes defer during a
/// restore via <see cref="MaintenanceGate"/>; the service owns the commit via <c>IUnitOfWork</c>.
/// </summary>
public sealed class PipelineStageService(
    IPipelineStageRepository stages,
    IUnitOfWork unitOfWork,
    MaintenanceGate maintenance,
    TimeProvider time)
{
    public static readonly Error StageNotFound = new("PipelineStageNotFound", "The pipeline stage was not found.");
    public static readonly Error StageInUse = new("StageInUse", "This stage still holds applications — provide a stage to reassign them to before removing it.");
    public static readonly Error InvalidReassignTarget = new("InvalidReassignTarget", "The reassignment target stage was not found.");
    public static readonly Error CannotRemoveLastStage = new("CannotRemoveLastStage", "The pipeline must keep at least one stage.");
    public static readonly Error InvalidOrder = new("InvalidStageOrder", "The reorder list must contain exactly the existing stage ids, once each.");

    public async Task<IReadOnlyList<PipelineStageDto>> ListAsync(CancellationToken ct = default)
    {
        var list = await stages.ListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<Result<PipelineStageDto>> CreateAsync(string name, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        var existing = await stages.ListAsync(ct);
        var position = existing.Count == 0 ? 0 : existing.Max(s => s.Position) + 1;
        var created = PipelineStage.Create(name, position, time.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        await stages.AddAsync(created.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(created.Value);
    }

    public async Task<Result> RenameAsync(PipelineStageId id, string name, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        var stage = await stages.GetAsync(id, ct);
        if (stage is null)
        {
            return StageNotFound;
        }

        var renamed = stage.Rename(name);
        if (renamed.IsFailure)
        {
            return renamed.Error;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Reorder the whole pipeline by the full ordered id list (must be exactly the existing set).</summary>
    public async Task<Result> ReorderAsync(IReadOnlyList<PipelineStageId> orderedIds, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        var all = await stages.ListAsync(ct);
        var existingIds = all.Select(s => s.Id).ToHashSet();
        var provided = orderedIds.ToHashSet();
        if (orderedIds.Count != existingIds.Count || !provided.SetEquals(existingIds))
        {
            return InvalidOrder;
        }

        for (var position = 0; position < orderedIds.Count; position++)
        {
            var stage = await stages.GetAsync(orderedIds[position], ct);
            stage!.MoveTo(position);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Remove a stage. When it still holds applications, a valid <paramref name="reassignTo"/> is required
    /// (else <see cref="StageInUse"/>) and every application is moved there first. The last stage cannot be
    /// removed (keeps the "an applied offer always has a stage to sit in" invariant satisfiable).
    /// </summary>
    public async Task<Result> RemoveAsync(PipelineStageId id, PipelineStageId? reassignTo, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);
        var stage = await stages.GetAsync(id, ct);
        if (stage is null)
        {
            return StageNotFound;
        }

        var total = (await stages.ListAsync(ct)).Count;
        if (total <= 1)
        {
            return CannotRemoveLastStage;
        }

        var occupants = await stages.CountApplicationsInStageAsync(id, ct);
        if (occupants > 0)
        {
            if (reassignTo is not { } target || target == id)
            {
                return StageInUse;
            }

            if (await stages.GetAsync(target, ct) is null)
            {
                return InvalidReassignTarget;
            }

            await stages.ReassignApplicationsAsync(id, target, time.GetUtcNow(), ct);
        }

        stages.Remove(stage);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static PipelineStageDto ToDto(PipelineStage s) => new(s.Id.ToString(), s.Name, s.Position);
}
