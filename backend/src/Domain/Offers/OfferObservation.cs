using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Offers;

/// <summary>
/// One append-only row per offer per scan it was seen in (data-model §Children of Offer). Drives
/// <c>LastSeen</c> and the disappearance reconciliation (offers absent from a Complete scan).
/// </summary>
public sealed class OfferObservation
{
    public OfferObservationId Id { get; private set; }
    public OfferId OfferId { get; private set; }
    public ScanRunId ScanRunId { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }
    public ContentFingerprint Fingerprint { get; private set; } = null!;

    private OfferObservation()
    {
        // EF Core materialization.
    }

    public static OfferObservation Create(
        OfferId offerId,
        ScanRunId scanRunId,
        DateTimeOffset observedAt,
        ContentFingerprint fingerprint) =>
        new()
        {
            Id = OfferObservationId.New(),
            OfferId = offerId,
            ScanRunId = scanRunId,
            ObservedAt = observedAt,
            Fingerprint = fingerprint,
        };
}
