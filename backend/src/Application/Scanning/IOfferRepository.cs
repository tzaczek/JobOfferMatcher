using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// Persistence port for the <see cref="Offer"/> aggregate and its append-only observations.
/// Tracked entities returned here are mutated in place; <c>IUnitOfWork.SaveChangesAsync</c> persists.
/// </summary>
public interface IOfferRepository
{
    Task<Offer?> GetByExternalRefAsync(SourceId sourceId, string nativeKey, CancellationToken ct = default);
    Task<IReadOnlyList<Offer>> GetActiveBySourceAsync(SourceId sourceId, CancellationToken ct = default);
    Task<IReadOnlyList<Offer>> GetAllActiveAsync(CancellationToken ct = default);
    Task<Offer?> GetByIdAsync(OfferId id, CancellationToken ct = default);
    Task AddAsync(Offer offer, CancellationToken ct = default);
    Task AddObservationAsync(OfferObservation observation, CancellationToken ct = default);
    Task AddVersionAsync(OfferVersion version, CancellationToken ct = default);
    Task AddEventAsync(OfferEvent offerEvent, CancellationToken ct = default);
}
