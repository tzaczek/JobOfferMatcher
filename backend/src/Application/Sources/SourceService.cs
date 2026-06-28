using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Application.Sources;

/// <summary>
/// Manage configured sources WITHOUT code changes (FR-002/003): list, create, edit search criteria,
/// and enable/disable. The next scan honors the edits.
/// </summary>
public sealed class SourceService(IJobSourceRepository sources, IUnitOfWork unitOfWork)
{
    public static readonly Error SourceNotFound = new("SourceNotFound", "Source not found.");

    public Task<IReadOnlyList<JobSource>> ListAsync(CancellationToken ct = default) => sources.GetAllAsync(ct);

    public async Task<Result<JobSource>> CreateAsync(
        string name, SourceKind kind, JobSourceSearch search, bool requiresLogin, CancellationToken ct = default)
    {
        var created = JobSource.Create(SourceId.New(), name, kind, search, requiresLogin);
        if (created.IsFailure)
        {
            return created.Error;
        }

        await sources.AddAsync(created.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return created;
    }

    public async Task<Result<JobSource>> UpdateAsync(
        SourceId id, string name, JobSourceSearch search, bool requiresLogin, CancellationToken ct = default)
    {
        var source = await sources.GetByIdAsync(id, ct);
        if (source is null)
        {
            return SourceNotFound;
        }

        var rename = source.Rename(name);
        if (rename.IsFailure)
        {
            return rename.Error;
        }

        source.UpdateSearch(search);
        source.SetRequiresLogin(requiresLogin);
        await unitOfWork.SaveChangesAsync(ct);
        return source;
    }

    public async Task<Result> SetEnabledAsync(SourceId id, bool enabled, CancellationToken ct = default)
    {
        var source = await sources.GetByIdAsync(id, ct);
        if (source is null)
        {
            return SourceNotFound;
        }

        if (enabled)
        {
            source.Enable();
        }
        else
        {
            source.Disable();
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
