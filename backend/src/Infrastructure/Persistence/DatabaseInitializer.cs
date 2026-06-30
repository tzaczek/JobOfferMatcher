using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Infrastructure.Persistence;

/// <summary>
/// Applies append-only EF migrations and seeds config at host startup (research §5; Principle IX:
/// <c>MigrateAsync</c>, never <c>EnsureCreated</c>). Then runs the idempotent enrichment backfill
/// (FR-014) so pre-existing offers/CV get their satellite rows + input hash on first enablement.
/// Called from the Web host's Program.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseInitializer));

        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync(ct);

        await DatabaseSeeder.SeedAsync(db, logger, ct);

        await BackfillEnrichmentAsync(
            db,
            scope.ServiceProvider.GetRequiredService<ICvFileStore>(),
            scope.ServiceProvider.GetRequiredService<ICvTextExtractor>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            logger,
            ct);
    }

    /// <summary>
    /// Idempotent backfill (FR-014): a <c>Pending</c> <c>offer_enrichment</c> + <c>offer_fit</c> row for
    /// every offer lacking one, and the byte-hash for any CV that has none yet. Re-running is safe — it
    /// only fills gaps. Makes the satellite row an invariant (one per offer); there is no "row absence
    /// = pending" path.
    /// </summary>
    public static async Task BackfillEnrichmentAsync(
        AppDbContext db,
        ICvFileStore fileStore,
        ICvTextExtractor extractor,
        TimeProvider time,
        ILogger logger,
        CancellationToken ct = default)
    {
        var offerIds = await db.Offers.Select(o => o.Id).ToListAsync(ct);
        var withEnrichment = (await db.OfferEnrichments.Select(e => e.OfferId).ToListAsync(ct)).ToHashSet();
        var withFit = (await db.OfferFits.Select(f => f.OfferId).ToListAsync(ct)).ToHashSet();

        var missingEnrichment = offerIds.Where(id => !withEnrichment.Contains(id)).ToList();
        var missingFit = offerIds.Where(id => !withFit.Contains(id)).ToList();

        foreach (var id in missingEnrichment)
        {
            await db.OfferEnrichments.AddAsync(OfferEnrichment.CreatePending(id), ct);
        }

        foreach (var id in missingFit)
        {
            await db.OfferFits.AddAsync(OfferFit.CreatePending(id), ct);
        }

        var cvsNeedingHash = await db.CandidateCvs.Where(c => c.EnrichmentInputHash == null).ToListAsync(ct);
        foreach (var cv in cvsNeedingHash)
        {
            var path = fileStore.GetAbsolutePath(cv.FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(path, ct);
            var hash = CvProfileInputs.Hash(bytes).Serialized;
            using var stream = new MemoryStream(bytes);
            var readable = extractor.ExtractText(stream).IsSuccess;
            cv.SetExtractionGauge(readable, hash, time.GetUtcNow());
        }

        if (missingEnrichment.Count > 0 || missingFit.Count > 0 || cvsNeedingHash.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Enrichment backfill: +{Enrichment} enrichment, +{Fit} fit rows; {Cv} CV hash(es) computed.",
                missingEnrichment.Count, missingFit.Count, cvsNeedingHash.Count);
        }
    }
}
