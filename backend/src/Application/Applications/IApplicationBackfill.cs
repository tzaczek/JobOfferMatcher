namespace JobOfferMatcher.Application.Applications;

/// <summary>
/// Runs the idempotent no-data-loss backfill on the restore path (data-model §8, ADR-3): after an
/// <c>Older</c> backup is loaded, every applied offer that lacks an <c>application</c> row gets one at the
/// first stage (+ its legacy note as the first journal entry). Mirrors <c>IEnrichmentBackfill</c> so
/// <see cref="JobOfferMatcher.Application.Backup.RestoreService"/> can reconstruct applications the same
/// way it backfills enrichment — because 003's restore <c>TRUNCATE</c>s the full HEAD table list.
/// </summary>
public interface IApplicationBackfill
{
    Task RunAsync(CancellationToken ct = default);
}
