using JobOfferMatcher.Application.TailoredCvs;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.TailoredCvs;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

internal sealed class TailoredCvRepository(AppDbContext db) : ITailoredCvRepository
{
    public Task<TailoredCv?> GetByOfferAsync(OfferId offerId, CancellationToken ct = default) =>
        db.TailoredCvs.FirstOrDefaultAsync(t => t.OfferId == offerId, ct);

    public async Task<IReadOnlyList<TailoredCv>> GetAllAsync(CancellationToken ct = default) =>
        await db.TailoredCvs.AsNoTracking().ToListAsync(ct);

    public async Task AddAsync(TailoredCv tailoredCv, CancellationToken ct = default) =>
        await db.TailoredCvs.AddAsync(tailoredCv, ct);

    public void Remove(TailoredCv tailoredCv) => db.TailoredCvs.Remove(tailoredCv);

    public async Task<IReadOnlyList<TailoredCv>> GetPendingAsync(int limit, CancellationToken ct = default) =>
        await db.TailoredCvs.AsNoTracking()
            .Where(t => t.State == TailoredCvState.Pending)
            .OrderBy(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<TailoredCvCounts> GetCountsAsync(CancellationToken ct = default)
    {
        var pending = await db.TailoredCvs.CountAsync(t => t.State == TailoredCvState.Pending, ct);
        var failed = await db.TailoredCvs.CountAsync(t => t.State == TailoredCvState.Failed, ct);
        var produced = await db.TailoredCvs.CountAsync(t => t.State == TailoredCvState.Produced, ct);
        return new TailoredCvCounts(pending, failed, produced);
    }
}
