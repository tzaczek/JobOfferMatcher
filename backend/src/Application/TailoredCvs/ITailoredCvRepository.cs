using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.TailoredCvs;

namespace JobOfferMatcher.Application.TailoredCvs;

/// <summary>Opt-in tailored-CV totals by state (data-model §7) — drives the worker queue meta.</summary>
public sealed record TailoredCvCounts(int Pending, int Failed, int Produced);

/// <summary>
/// Persistence port for the <c>tailored_cv</c> satellite (data-model §7). The single-row get is
/// <b>tracked</b> (callers mutate + save via the unit of work); list / pending / count queries are
/// read-only (AsNoTracking). Rows exist only for offers the user chose to tailor for.
/// </summary>
public interface ITailoredCvRepository
{
    Task<TailoredCv?> GetByOfferAsync(OfferId offerId, CancellationToken ct = default);
    Task<IReadOnlyList<TailoredCv>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(TailoredCv tailoredCv, CancellationToken ct = default);
    void Remove(TailoredCv tailoredCv);
    Task<IReadOnlyList<TailoredCv>> GetPendingAsync(int limit, CancellationToken ct = default);
    Task<TailoredCvCounts> GetCountsAsync(CancellationToken ct = default);
}
