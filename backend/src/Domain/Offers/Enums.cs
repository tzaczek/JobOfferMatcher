namespace JobOfferMatcher.Domain.Offers;

/// <summary>How an offer's stable identity is derived (data-model §Identity). Prefer the native id.</summary>
public enum IdentityKind
{
    NativeId,
    Slug,
    CanonicalUrl,
    FallbackHash,
}

/// <summary>Where the role can be performed. <see cref="Unknown"/> is kept + flagged, never dropped (FR-010).</summary>
public enum WorkMode
{
    Unknown = 0,
    Office,
    Remote,
    Hybrid,
}

/// <summary>Availability lifecycle (data-model §Offer). Reconciled only after a Complete scan (FR-015).</summary>
public enum AvailabilityStatus
{
    Available,
    NoLongerAvailable,
}

/// <summary>
/// User-set disposition (FR-031), orthogonal to availability and new-vs-seen. Append-only events
/// drive it; a <see cref="Dismissed"/> offer never re-appears as new (SC-002).
/// </summary>
public enum UserOfferStatus
{
    New,
    Viewed,
    Interested,
    Dismissed,
}

/// <summary>Which tier of content changed (data-model §Identity). Major flags "updated"; Minor is silent.</summary>
public enum ChangeTier
{
    Major,
    Minor,
}
