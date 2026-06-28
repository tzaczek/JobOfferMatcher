using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.RoleGroups;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

internal sealed class RoleGroupRepository(AppDbContext db) : IRoleGroupRepository
{
    public async Task<IReadOnlyList<RoleGroup>> GetAllAsync(CancellationToken ct = default) =>
        await db.RoleGroups.ToListAsync(ct);

    public Task<RoleGroup?> GetByIdAsync(RoleGroupId id, CancellationToken ct = default) =>
        db.RoleGroups.FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task AddAsync(RoleGroup group, CancellationToken ct = default) =>
        await db.RoleGroups.AddAsync(group, ct);
}
