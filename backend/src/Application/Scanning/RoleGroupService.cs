using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.RoleGroups;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>Apply a user "same / not same" correction to a cross-source group (FR-016).</summary>
public sealed class RoleGroupService(IRoleGroupRepository groups, IUnitOfWork unitOfWork)
{
    public static readonly Error RoleGroupNotFound = new("RoleGroupNotFound", "Role group not found.");

    public async Task<Result> SetOverrideAsync(RoleGroupId id, RoleGroupOverride userOverride, CancellationToken ct = default)
    {
        var group = await groups.GetByIdAsync(id, ct);
        if (group is null)
        {
            return RoleGroupNotFound;
        }

        group.SetOverride(userOverride);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
