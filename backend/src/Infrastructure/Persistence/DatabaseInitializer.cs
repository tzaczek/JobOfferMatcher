using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Domain.Applications;
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

        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        await DatabaseSeeder.SeedAsync(db, time, logger, ct);

        await BackfillEnrichmentAsync(
            db,
            scope.ServiceProvider.GetRequiredService<ICvFileStore>(),
            scope.ServiceProvider.GetRequiredService<ICvTextExtractor>(),
            time,
            logger,
            ct);

        await BackfillApplicationsAsync(db, time, logger, ct);
    }

    /// <summary>
    /// Idempotent no-data-loss backfill (ADR-3, SC-001/SC-007): ensure the default stages exist, then for
    /// every offer marked <c>Applied</c> that has no <c>application</c> row, create one at the first stage
    /// (<c>AppliedAt = offer.AppliedAt</c>) and migrate the legacy <c>offer.ApplicationNote</c> to the first
    /// journal entry (only when the journal is empty). Re-running only fills gaps. Runs at startup (in-place
    /// upgrade) AND — via <c>ApplicationBackfillRunner</c> → <c>RestoreService</c> — after an OLDER restore,
    /// because 003's restore <c>TRUNCATE</c>s the full HEAD table list, leaving the application tables empty.
    /// </summary>
    public static async Task BackfillApplicationsAsync(AppDbContext db, TimeProvider time, ILogger logger, CancellationToken ct = default)
    {
        // On the restore path the seed doesn't run, so ensure stages exist before placing applications.
        await DatabaseSeeder.SeedPipelineStagesAsync(db, time, logger, ct);

        var firstStage = await db.PipelineStages
            .OrderBy(s => s.Position).ThenBy(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (firstStage is null)
        {
            return; // No stages at all (a user deleted them and none were seeded) — nothing to place.
        }

        var withApplication = (await db.Applications.Select(a => a.OfferId).ToListAsync(ct)).ToHashSet();
        var missing = (await db.Offers.Where(o => o.Applied).ToListAsync(ct))
            .Where(o => !withApplication.Contains(o.Id))
            .ToList();

        var notesMigrated = 0;
        foreach (var offer in missing)
        {
            var now = time.GetUtcNow();
            await db.Applications.AddAsync(JobApplication.Create(offer.Id, firstStage.Id, offer.AppliedAt, now), ct);

            if (!string.IsNullOrWhiteSpace(offer.ApplicationNote)
                && !await db.ApplicationNotes.AnyAsync(n => n.OfferId == offer.Id, ct))
            {
                var note = ApplicationNote.Create(offer.Id, offer.ApplicationNote, offer.AppliedAt ?? now);
                if (note.IsSuccess)
                {
                    await db.ApplicationNotes.AddAsync(note.Value, ct);
                    notesMigrated++;
                }
            }
        }

        if (missing.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Application backfill: +{Applications} application(s) reconstructed; {Notes} legacy note(s) migrated.",
                missing.Count, notesMigrated);
        }
    }

    /// <summary>
    /// Idempotent backfill (FR-014, feature 006 SC-005/SC-006): a <c>Pending</c> <c>offer_enrichment</c>
    /// + <c>offer_fit</c> + <c>offer_affinity</c> row for every offer lacking one, and the byte-hash for
    /// any CV that has none yet. Re-running is safe — it only fills gaps. Makes each satellite row an
    /// invariant (one per offer); there is no "row absence = pending" path. Runs at <b>startup</b> and,
    /// via <c>EnrichmentBackfillRunner</c> → <c>RestoreService</c>, on an <b>older-backup restore</b>
    /// (003's restore <c>TRUNCATE</c>s the full HEAD table list, so a pre-006 backup leaves
    /// <c>offer_affinity</c> empty until this synthesises the rows). Offer bodies are NOT backfilled here
    /// (no startup network calls) — they fill in on the next scan.
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
        var withAffinity = (await db.OfferAffinities.Select(a => a.OfferId).ToListAsync(ct)).ToHashSet();

        var missingEnrichment = offerIds.Where(id => !withEnrichment.Contains(id)).ToList();
        var missingFit = offerIds.Where(id => !withFit.Contains(id)).ToList();
        var missingAffinity = offerIds.Where(id => !withAffinity.Contains(id)).ToList();

        foreach (var id in missingEnrichment)
        {
            await db.OfferEnrichments.AddAsync(OfferEnrichment.CreatePending(id), ct);
        }

        foreach (var id in missingFit)
        {
            await db.OfferFits.AddAsync(OfferFit.CreatePending(id), ct);
        }

        foreach (var id in missingAffinity)
        {
            await db.OfferAffinities.AddAsync(OfferAffinity.CreatePending(id), ct);
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

        if (missingEnrichment.Count > 0 || missingFit.Count > 0 || missingAffinity.Count > 0 || cvsNeedingHash.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Enrichment backfill: +{Enrichment} enrichment, +{Fit} fit, +{Affinity} affinity rows; {Cv} CV hash(es) computed.",
                missingEnrichment.Count, missingFit.Count, missingAffinity.Count, cvsNeedingHash.Count);
        }
    }
}
