using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Enrichment;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Infrastructure.Persistence;

/// <summary>
/// Port adapter over <see cref="DatabaseInitializer.BackfillEnrichmentAsync"/> so the (Application-layer)
/// restore can run the idempotent enrichment backfill for an <c>Older</c> backup without referencing EF
/// (003 T035). Startup keeps calling the static method directly — no behaviour change.
/// </summary>
public sealed class EnrichmentBackfillRunner(
    AppDbContext db,
    ICvFileStore fileStore,
    ICvTextExtractor extractor,
    TimeProvider time,
    ILogger<EnrichmentBackfillRunner> logger) : IEnrichmentBackfill
{
    public Task RunAsync(CancellationToken ct = default) =>
        DatabaseInitializer.BackfillEnrichmentAsync(db, fileStore, extractor, time, logger, ct);
}
