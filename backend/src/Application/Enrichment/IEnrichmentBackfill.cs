namespace JobOfferMatcher.Application.Enrichment;

/// <summary>
/// Runs the idempotent enrichment backfill (FR-014) on demand — the same gap-filling that startup
/// performs, exposed as a port so a restore of an <c>Older</c> backup can synthesise the <c>Pending</c>
/// satellite rows that a later migration introduced (003 ADR-2). Re-running is safe; it only fills gaps.
/// </summary>
public interface IEnrichmentBackfill
{
    Task RunAsync(CancellationToken ct = default);
}
