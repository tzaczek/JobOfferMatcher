using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

internal sealed class OfferRepository(AppDbContext db) : IOfferRepository
{
    public Task<Offer?> GetByExternalRefAsync(SourceId sourceId, string nativeKey, CancellationToken ct = default) =>
        db.Offers.FirstOrDefaultAsync(
            o => o.ExternalRef.SourceId == sourceId && o.ExternalRef.NativeKey == nativeKey, ct);

    public async Task<IReadOnlyList<Offer>> GetActiveBySourceAsync(SourceId sourceId, CancellationToken ct = default) =>
        await db.Offers
            .Where(o => o.ExternalRef.SourceId == sourceId && o.Availability == AvailabilityStatus.Available)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Offer>> GetAllActiveAsync(CancellationToken ct = default) =>
        await db.Offers.Where(o => o.Availability == AvailabilityStatus.Available).ToListAsync(ct);

    public Task<Offer?> GetByIdAsync(OfferId id, CancellationToken ct = default) =>
        db.Offers.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task AddAsync(Offer offer, CancellationToken ct = default) =>
        await db.Offers.AddAsync(offer, ct);

    public async Task AddObservationAsync(OfferObservation observation, CancellationToken ct = default) =>
        await db.OfferObservations.AddAsync(observation, ct);

    public async Task AddVersionAsync(OfferVersion version, CancellationToken ct = default) =>
        await db.OfferVersions.AddAsync(version, ct);

    public async Task AddEventAsync(OfferEvent offerEvent, CancellationToken ct = default) =>
        await db.OfferEvents.AddAsync(offerEvent, ct);
}
