using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Offers;

/// <summary>Lifecycle event types on the append-only offer timeline (data-model §Children of Offer).</summary>
public enum OfferEventType
{
    FirstSeen,
    Surfaced,
    Updated,
    BecameUnavailable,
    Reappeared,
    StatusChanged,
    Applied,
    ApplicationCleared,
    // Application tracking (005) — stage moves / close / reopen append here (no migration: type is varchar(40)).
    ApplicationStageChanged,
    ApplicationClosed,
    ApplicationReopened,
}

/// <summary>
/// One immutable lifecycle event (FR-009/034). Answers "when first seen / suggested / changed /
/// became unavailable". Never updated or deleted.
/// </summary>
public sealed class OfferEvent
{
    public OfferEventId Id { get; private set; }
    public OfferId OfferId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public OfferEventType Type { get; private set; }
    public string? Payload { get; private set; }

    private OfferEvent()
    {
        // EF Core materialization.
    }

    public static OfferEvent Create(OfferId offerId, DateTimeOffset occurredAt, OfferEventType type, string? payload = null) =>
        new()
        {
            Id = OfferEventId.New(),
            OfferId = offerId,
            OccurredAt = occurredAt,
            Type = type,
            Payload = payload,
        };
}
