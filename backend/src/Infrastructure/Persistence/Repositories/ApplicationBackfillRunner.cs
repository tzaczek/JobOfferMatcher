using JobOfferMatcher.Application.Applications;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// Port adapter that runs the idempotent no-data-loss application backfill on the restore path (ADR-3),
/// mirroring <c>EnrichmentBackfillRunner</c>. Delegates to the shared
/// <see cref="DatabaseInitializer.BackfillApplicationsAsync"/> so upgrade and older-restore reconstruct
/// applications identically. Registered scoped alongside <c>IEnrichmentBackfill</c>.
/// </summary>
public sealed class ApplicationBackfillRunner(
    AppDbContext db,
    TimeProvider time,
    ILogger<ApplicationBackfillRunner> logger) : IApplicationBackfill
{
    public Task RunAsync(CancellationToken ct = default) =>
        DatabaseInitializer.BackfillApplicationsAsync(db, time, logger, ct);
}
