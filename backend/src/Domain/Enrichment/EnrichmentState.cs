namespace JobOfferMatcher.Domain.Enrichment;

/// <summary>
/// Lifecycle of an AI-derived offer output (offer summary AND offer fit) — data-model §1.
/// <see cref="Pending"/> is the default at creation and after invalidation; <see cref="Failed"/> is
/// terminal until inputs change or a manual re-run re-arms it. The worker is the sole producer:
/// un-produced items render as "pending", never a non-AI fallback (FR-005).
/// </summary>
public enum EnrichmentState
{
    Pending,
    Produced,
    Failed,
}
