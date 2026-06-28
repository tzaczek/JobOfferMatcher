using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Application.Offers;

/// <summary>
/// Apply a user-set offer status (FR-031). The transition is validated INSIDE the aggregate
/// (Principle III); on success a <see cref="OfferEventType.StatusChanged"/> event is appended
/// (append-only history). A dismissed offer never re-appears as new (SC-002).
/// </summary>
public sealed class SetUserOfferStatus(IOfferRepository offers, IUnitOfWork unitOfWork, TimeProvider time)
{
    public static readonly Error OfferNotFound = new("OfferNotFound", "Offer not found.");

    public async Task<Result> ExecuteAsync(OfferId offerId, UserOfferStatus status, CancellationToken ct = default)
    {
        var offer = await offers.GetByIdAsync(offerId, ct);
        if (offer is null)
        {
            return OfferNotFound;
        }

        var change = offer.ChangeUserStatus(status);
        if (change.IsFailure)
        {
            return change.Error;
        }

        var payload = $"{{\"status\":\"{status.ToString().ToLowerInvariant()}\"}}";
        await offers.AddEventAsync(OfferEvent.Create(offerId, time.GetUtcNow(), OfferEventType.StatusChanged, payload), ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
