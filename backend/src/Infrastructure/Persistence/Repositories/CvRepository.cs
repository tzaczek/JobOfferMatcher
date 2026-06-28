using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

internal sealed class CvRepository(AppDbContext db) : ICvRepository
{
    public async Task<IReadOnlyList<CandidateCv>> GetAllAsync(CancellationToken ct = default) =>
        await db.CandidateCvs.OrderBy(c => c.FileName).ToListAsync(ct);

    public async Task<IReadOnlyList<CandidateCv>> GetReadableAsync(CancellationToken ct = default) =>
        await db.CandidateCvs.Where(c => c.IsReadable).ToListAsync(ct);

    public Task<CandidateCv?> GetByIdAsync(CvId id, CancellationToken ct = default) =>
        db.CandidateCvs.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(CandidateCv cv, CancellationToken ct = default) =>
        await db.CandidateCvs.AddAsync(cv, ct);

    public void Remove(CandidateCv cv) => db.CandidateCvs.Remove(cv);
}
