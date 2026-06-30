using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Infrastructure.Cv;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Real-Postgres backfill test (T022, FR-014): the startup backfill idempotently creates a Pending
/// satellite per offer and computes the existing CV's input hash, so first-enablement counts are
/// correct (not 0).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EnrichmentBackfillTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Backfill_creates_pending_satellites_for_offers_lacking_them_and_is_idempotent()
    {
        await postgres.ResetAsync();
        await using var db = postgres.CreateContext();
        db.Offers.Add(LlmEnrichmentMigrationTests.SeedOffer("backfill-1"));
        db.Offers.Add(LlmEnrichmentMigrationTests.SeedOffer("backfill-2"));
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        (await db.OfferEnrichments.CountAsync()).ShouldBe(2);
        (await db.OfferFits.CountAsync()).ShouldBe(2);
        (await db.OfferEnrichments.AllAsync(e => e.State == EnrichmentState.Pending)).ShouldBeTrue();

        // Idempotent: a second pass adds nothing.
        await RunBackfillAsync(db);
        (await db.OfferEnrichments.CountAsync()).ShouldBe(2);
        (await db.OfferFits.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Backfill_computes_the_existing_cv_input_hash()
    {
        await postgres.ResetAsync();
        var tempPath = Path.Combine(Path.GetTempPath(), $"cv-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(tempPath, [1, 2, 3, 4, 5]);
        try
        {
            await using var db = postgres.CreateContext();
            // A pre-migration CV: profile pending, no input hash yet.
            db.CandidateCvs.Add(CandidateCv.Create(CvId.New(), Path.GetFileName(tempPath)));
            await db.SaveChangesAsync();

            await RunBackfillAsync(db, new FakeCvFileStore(Path.GetTempPath()));

            var reloaded = await db.CandidateCvs.AsNoTracking().SingleAsync();
            reloaded.EnrichmentInputHash.ShouldNotBeNull();
            reloaded.ProfileState.ShouldBe(CvProcessingState.Pending);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static Task RunBackfillAsync(AppDbContext db, ICvFileStore? fileStore = null) =>
        DatabaseInitializer.BackfillEnrichmentAsync(
            db,
            fileStore ?? new FakeCvFileStore(Path.GetTempPath()),
            new PdfPigCvTextExtractor(),
            TimeProvider.System,
            NullLogger.Instance);

    private sealed class FakeCvFileStore(string dir) : ICvFileStore
    {
        public Task<string> SaveAsync(CvId id, string originalFileName, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storedFileName) { }

        public string GetAbsolutePath(string storedFileName) => Path.Combine(dir, storedFileName);

        public IReadOnlyList<StoredCvFile> EnumerateAll() => [];

        public ICvDirectorySwap StageSwap(IReadOnlyList<CvFilePayload> files) => throw new NotSupportedException();
    }
}
