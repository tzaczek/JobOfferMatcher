using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// A source-agnostic, normalized offer produced by an <see cref="IJobSource"/> adapter
/// (contracts/ijobsource-port.md). The adapter wraps the native id into <see cref="ExternalRef"/>
/// at the Infrastructure boundary — no raw source ids leak past it (Principle II). The orchestrator
/// computes the fingerprint from <see cref="Content"/> and upserts.
/// </summary>
public sealed record CollectedOffer(ExternalRef ExternalRef, OfferContent Content);
