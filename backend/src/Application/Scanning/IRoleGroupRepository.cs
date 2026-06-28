using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.RoleGroups;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>Persistence port for cross-source <see cref="RoleGroup"/> clusters (FR-016).</summary>
public interface IRoleGroupRepository
{
    Task<IReadOnlyList<RoleGroup>> GetAllAsync(CancellationToken ct = default);
    Task<RoleGroup?> GetByIdAsync(RoleGroupId id, CancellationToken ct = default);
    Task AddAsync(RoleGroup group, CancellationToken ct = default);
}
