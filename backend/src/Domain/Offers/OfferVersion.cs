using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Offers;

/// <summary>
/// An immutable content snapshot captured each time an offer's Major-tier content changes
/// (FR-014/034). Together the versions form the offer's edit history. Never updated or deleted.
/// </summary>
public sealed class OfferVersion
{
    public OfferVersionId Id { get; private set; }
    public OfferId OfferId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public ChangeTier ChangeTier { get; private set; }
    public OfferContent Snapshot { get; private set; } = null!;
    public ContentFingerprint Fingerprint { get; private set; } = null!;

    private OfferVersion()
    {
        // EF Core materialization.
    }

    public static OfferVersion Create(
        OfferId offerId,
        DateTimeOffset createdAt,
        ChangeTier tier,
        OfferContent snapshot,
        ContentFingerprint fingerprint) =>
        new()
        {
            Id = OfferVersionId.New(),
            OfferId = offerId,
            CreatedAt = createdAt,
            ChangeTier = tier,
            Snapshot = snapshot,
            Fingerprint = fingerprint,
        };
}
