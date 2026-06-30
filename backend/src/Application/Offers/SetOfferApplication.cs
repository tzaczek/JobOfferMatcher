using System.Text.Json;
using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Application.Offers;

/// <summary>
/// Mark/clear the user's "applied" flag on an offer, with an optional date and note. The flag is
/// orthogonal to <see cref="UserOfferStatus"/> (an offer can be "interested" AND "applied"); the
/// transition + note validation live INSIDE the aggregate (Principle III). Each change appends an
/// append-only <see cref="OfferEventType.Applied"/> / <see cref="OfferEventType.ApplicationCleared"/>
/// event so the offer timeline stays complete (FR-009/034).
/// </summary>
public sealed class SetOfferApplication(IOfferRepository offers, IUnitOfWork unitOfWork, TimeProvider time)
{
    public static readonly Error OfferNotFound = new("OfferNotFound", "Offer not found.");

    /// <summary>Mark the offer applied (or re-mark to edit its date/note). Idempotent on the flag.</summary>
    public async Task<Result> MarkAppliedAsync(OfferId offerId, DateTimeOffset? appliedAt, string? note, CancellationToken ct = default)
    {
        var offer = await offers.GetByIdAsync(offerId, ct);
        if (offer is null)
        {
            return OfferNotFound;
        }

        var applied = offer.MarkApplied(appliedAt, note);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        var payload = JsonSerializer.Serialize(new
        {
            appliedAt = offer.AppliedAt,
            note = offer.ApplicationNote,
        });
        await offers.AddEventAsync(OfferEvent.Create(offerId, time.GetUtcNow(), OfferEventType.Applied, payload), ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Clear the applied flag and its optional date/note. Idempotent: clearing an offer that was never
    /// applied succeeds without appending a (misleading) <see cref="OfferEventType.ApplicationCleared"/>
    /// event to the append-only timeline.
    /// </summary>
    public async Task<Result> ClearAsync(OfferId offerId, CancellationToken ct = default)
    {
        var offer = await offers.GetByIdAsync(offerId, ct);
        if (offer is null)
        {
            return OfferNotFound;
        }

        if (offer.ClearApplied())
        {
            await offers.AddEventAsync(OfferEvent.Create(offerId, time.GetUtcNow(), OfferEventType.ApplicationCleared), ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
