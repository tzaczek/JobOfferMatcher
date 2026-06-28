namespace JobOfferMatcher.Domain.Offers;

/// <summary>How a collected offer relates to what we already have (research §6, FR-012/013/014).</summary>
public enum OfferChangeKind
{
    New,
    Unchanged,
    Updated,
}

/// <summary>
/// Pure new/updated/unchanged decision. New-vs-seen is decided by identity existence
/// (a null existing fingerprint = new), never by source dates. A fingerprint-VERSION change is an
/// algorithm-only delta and is suppressed to Unchanged (data-model §Identity), so re-hashing the
/// catalog never mass-flags offers as updated.
/// </summary>
public static class OfferClassifier
{
    public static OfferChangeKind Classify(ContentFingerprint? existing, ContentFingerprint incoming)
    {
        if (existing is null)
        {
            return OfferChangeKind.New;
        }

        if (existing.Version != incoming.Version)
        {
            return OfferChangeKind.Unchanged;
        }

        return string.Equals(existing.Hash, incoming.Hash, StringComparison.Ordinal)
            ? OfferChangeKind.Unchanged
            : OfferChangeKind.Updated;
    }
}
