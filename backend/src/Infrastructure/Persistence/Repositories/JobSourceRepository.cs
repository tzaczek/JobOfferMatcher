using JobOfferMatcher.Application.Sources;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

internal sealed class JobSourceRepository(AppDbContext db) : IJobSourceRepository
{
    public async Task<IReadOnlyList<JobSource>> GetAllAsync(CancellationToken ct = default) =>
        await db.JobSources.OrderBy(s => s.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<JobSource>> GetEnabledAsync(CancellationToken ct = default) =>
        await db.JobSources.Where(s => s.Enabled).OrderBy(s => s.Name).ToListAsync(ct);

    public Task<JobSource?> GetByIdAsync(SourceId id, CancellationToken ct = default) =>
        db.JobSources.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task AddAsync(JobSource source, CancellationToken ct = default) =>
        await db.JobSources.AddAsync(source, ct);
}
