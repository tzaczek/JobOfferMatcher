using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Application.Enrichment;

/// <summary>An offer joined with its two derived-cache satellites (for the pending-work projection + read model).</summary>
public sealed record OfferWorkRow(Offer Offer, OfferEnrichment Enrichment, OfferFit Fit);

/// <summary>
/// Eligibility-gated satellite counts (data-model §8). Fit counts are 0 when no current produced CV
/// profile exists, so an absent/pending/unreadable/failed profile never leaves ~180 fits "stuck
/// pending" and <c>pendingTotal</c> can reach 0 (SC-007).
/// </summary>
public sealed record SatelliteCounts(int PendingSummaries, int FailedSummaries, int PendingFits, int FailedFits);

/// <summary>
/// Persistence port for the two satellite tables (data-model §4/§5). Single-row gets are <b>tracked</b>
/// (callers mutate + save via the unit of work); the work-row and count queries are read-only. The
/// bulk re-arm/invalidate operations are immediate SQL (the eager invalidation hooks keep the
/// <c>state</c> column accurate, so they need no per-row hash compare).
/// </summary>
public interface IEnrichmentRepository
{
    Task<OfferEnrichment?> GetEnrichmentAsync(OfferId offerId, CancellationToken ct = default);
    Task<OfferFit?> GetFitAsync(OfferId offerId, CancellationToken ct = default);
    Task AddEnrichmentAsync(OfferEnrichment enrichment, CancellationToken ct = default);
    Task AddFitAsync(OfferFit fit, CancellationToken ct = default);

    /// <summary>All offers joined with their satellites (≈180 rows at single-user scale) for projection + counts.</summary>
    Task<IReadOnlyList<OfferWorkRow>> GetOfferWorkRowsAsync(CancellationToken ct = default);

    /// <summary>Eligibility-gated counts; pass <paramref name="countFits"/>=false when no produced CV profile exists.</summary>
    Task<SatelliteCounts> GetCountsAsync(bool countFits, CancellationToken ct = default);

    /// <summary>The most recent satellite <c>produced_at</c> (drives <c>lastResultAt</c>); null if none produced.</summary>
    Task<DateTimeOffset?> GetLastResultAtAsync(CancellationToken ct = default);

    /// <summary>Re-arm every fit row to Pending (CV/weights/preferences change → all fits pending — FR-007/SC-004).</summary>
    Task InvalidateAllFitsAsync(CancellationToken ct = default);

    /// <summary>Manual re-run, scope=failed: terminal Failed rows (both kinds) → Pending. Their hashes are current (eager hooks).</summary>
    Task RearmFailedAsync(CancellationToken ct = default);

    /// <summary>Manual re-run, scope=all: force a complete re-run — every satellite row → Pending.</summary>
    Task ForceAllPendingAsync(CancellationToken ct = default);
}
