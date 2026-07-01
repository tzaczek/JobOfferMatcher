using System.Net.Http.Json;
using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Infrastructure.Cv;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Tests.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static JobOfferMatcher.Infrastructure.Tests.EnrichmentFlow;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Affinity end-to-end on real Postgres (T024/T025/T040/T044): the ≥3 cold-start gate (insufficient),
/// pending→produced via the worker, the write-back stale guard, apply/un-apply → all-affinity-pending,
/// and the no-data-loss startup backfill (a Pending affinity row per offer, idempotent).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OfferAffinityFlowTests(PostgresFixture postgres)
{
    private const string AllOffers = "/api/offers?status=all&availability=all";

    [Fact]
    public async Task Affinity_is_insufficient_below_three_applied_offers()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            AddOfferWithSatellites(db, "aff-cold");
            AddAppliedOfferWithSatellites(db, "applied-1");
            AddAppliedOfferWithSatellites(db, "applied-2"); // only 2 applied → below the ≥3 gate
            await db.SaveChangesAsync();
        }

        var offers = await client.GetFromJsonAsync<OffersEnvelope>(AllOffers);
        offers!.Meta.HasAffinityBasis.ShouldBeFalse();
        offers.Meta.AppliedCount.ShouldBe(2);
        offers.Data.ShouldAllBe(o => o.AffinityState == "insufficient");
        offers.Data.ShouldAllBe(o => o.Affinity!.State == "insufficient" && o.Affinity.Score == null);
    }

    [Fact]
    public async Task Affinity_goes_pending_to_produced_via_the_worker()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var client = factory.CreateClient();

        Guid candidateId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var candidate = AddOfferWithSatellites(db, "aff-candidate");
            AddAppliedOfferWithSatellites(db, "applied-1");
            AddAppliedOfferWithSatellites(db, "applied-2");
            AddAppliedOfferWithSatellites(db, "applied-3"); // 3 applied → basis exists
            await db.SaveChangesAsync();
            candidateId = candidate.Id.Value;
        }

        // With a basis but no produced affinity yet, the read state is pending (never a fabricated score).
        var before = await client.GetFromJsonAsync<OffersEnvelope>(AllOffers);
        before!.Meta.HasAffinityBasis.ShouldBeTrue();
        before.Meta.AppliedCount.ShouldBe(3);
        var candidateBefore = before.Data.Single(o => o.OfferId == candidateId.ToString());
        candidateBefore.Affinity!.State.ShouldBe("pending");
        candidateBefore.Affinity.Score.ShouldBeNull();

        // The queue emits an offerAffinity item for the candidate; the basis excludes self (3 applied, candidate not applied ⇒ 3).
        var pending = await client.GetFromJsonAsync<PendingEnvelope>("/api/enrichment/pending?limit=50");
        pending!.Meta.HasAffinityBasis.ShouldBeTrue();
        var item = pending.Items.Single(i => i.Kind == "offerAffinity" && i.OfferId == candidateId);
        item.AppliedBasis!.Count.ShouldBe(3);

        var submit = await client.PostAsJsonAsync("/api/enrichment/results", new
        {
            results = new[]
            {
                new
                {
                    workItemId = item.WorkItemId,
                    kind = "offerAffinity",
                    inputsHash = item.InputsHash,
                    status = "produced",
                    score = 74,
                    resembles = new[] { "senior .NET", "remote" },
                    rationale = "Close to the roles you applied to.",
                },
            },
        });
        submit.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<OffersEnvelope>(AllOffers);
        var candidateAfter = after!.Data.Single(o => o.OfferId == candidateId.ToString());
        candidateAfter.Affinity!.State.ShouldBe("produced");
        candidateAfter.Affinity.Score.ShouldBe(74);
        candidateAfter.Affinity.Resembles.ShouldBe(["senior .NET", "remote"]);

        // Affinity is orthogonal to fit — fit stays absent (no produced CV profile) and unchanged (FR-016).
        candidateAfter.Fit.ShouldBeNull();
    }

    [Fact]
    public async Task Affinity_writeback_rejects_a_stale_hash()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var client = factory.CreateClient();

        Guid candidateId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var candidate = AddOfferWithSatellites(db, "aff-stale");
            AddAppliedOfferWithSatellites(db, "applied-1");
            AddAppliedOfferWithSatellites(db, "applied-2");
            AddAppliedOfferWithSatellites(db, "applied-3");
            await db.SaveChangesAsync();
            candidateId = candidate.Id.Value;
        }

        var submit = await client.PostAsJsonAsync("/api/enrichment/results", new
        {
            results = new[]
            {
                new
                {
                    workItemId = $"offer:{candidateId}:affinity",
                    kind = "offerAffinity",
                    inputsHash = "SHA256:1:not-current",
                    status = "produced",
                    score = 74,
                    resembles = new[] { "remote" },
                },
            },
        });
        submit.EnsureSuccessStatusCode();
        var envelope = await submit.Content.ReadFromJsonAsync<SubmitEnvelope>();
        envelope!.Results.Single().Outcome.ShouldBe("stale");

        var after = await client.GetFromJsonAsync<OffersEnvelope>(AllOffers);
        after!.Data.Single(o => o.OfferId == candidateId.ToString()).Affinity!.State.ShouldBe("pending");
    }

    [Fact]
    public async Task Applying_and_unapplying_repends_all_affinity_without_stale_values()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var client = factory.CreateClient();

        Guid candidateId;
        List<Guid> applyIds;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var candidate = AddOfferWithSatellites(db, "aff-repend");
            var a1 = AddOfferWithSatellites(db, "to-apply-1");
            var a2 = AddOfferWithSatellites(db, "to-apply-2");
            var a3 = AddOfferWithSatellites(db, "to-apply-3");
            var a4 = AddOfferWithSatellites(db, "to-apply-4");
            await db.SaveChangesAsync();
            candidateId = candidate.Id.Value;
            applyIds = [a1.Id.Value, a2.Id.Value, a3.Id.Value, a4.Id.Value];
        }

        // Apply four offers via the API (each apply re-arms all affinity through the hook).
        foreach (var id in applyIds)
        {
            (await client.PutAsJsonAsync($"/api/offers/{id}/application", new { })).EnsureSuccessStatusCode();
        }

        // Drain the candidate's affinity to produced.
        var pending = await client.GetFromJsonAsync<PendingEnvelope>("/api/enrichment/pending?limit=100");
        var item = pending!.Items.Single(i => i.Kind == "offerAffinity" && i.OfferId == candidateId);
        (await client.PostAsJsonAsync("/api/enrichment/results", new
        {
            results = new[]
            {
                new { workItemId = item.WorkItemId, kind = "offerAffinity", inputsHash = item.InputsHash, status = "produced", score = 80, resembles = new[] { "remote" } },
            },
        })).EnsureSuccessStatusCode();

        var produced = await client.GetFromJsonAsync<OffersEnvelope>(AllOffers);
        produced!.Data.Single(o => o.OfferId == candidateId.ToString()).Affinity!.State.ShouldBe("produced");

        // Un-apply ONE of the four (still ≥3 applied → basis valid) → the applied set changed → all affinity re-pends.
        (await client.DeleteAsync($"/api/offers/{applyIds[0]}/application")).EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<OffersEnvelope>(AllOffers);
        after!.Meta.HasAffinityBasis.ShouldBeTrue();
        after.Meta.AppliedCount.ShouldBe(3);
        var candidateAfter = after.Data.Single(o => o.OfferId == candidateId.ToString());
        // The previously produced score is GONE (re-pended) — never a stale value (SC-009).
        candidateAfter.Affinity!.State.ShouldBe("pending");
        candidateAfter.Affinity.Score.ShouldBeNull();
    }

    [Fact]
    public async Task Startup_backfill_creates_a_pending_affinity_row_per_offer_and_is_idempotent()
    {
        await postgres.ResetAsync();
        await using var db = postgres.CreateContext();
        // A pre-006 state: offers with no affinity rows.
        db.Offers.Add(SeedOffer("pre006-1"));
        db.Offers.Add(SeedOffer("pre006-2"));
        await db.SaveChangesAsync();
        (await db.OfferAffinities.CountAsync()).ShouldBe(0);

        await RunBackfillAsync(db);

        (await db.OfferAffinities.CountAsync()).ShouldBe(2);
        (await db.OfferAffinities.AllAsync(a => a.State == Domain.Enrichment.EnrichmentState.Pending)).ShouldBeTrue();

        // Idempotent: a second pass adds nothing.
        await RunBackfillAsync(db);
        (await db.OfferAffinities.CountAsync()).ShouldBe(2);
    }

    private static Task RunBackfillAsync(AppDbContext db) =>
        DatabaseInitializer.BackfillEnrichmentAsync(
            db,
            new BackfillFileStore(),
            new PdfPigCvTextExtractor(),
            TimeProvider.System,
            NullLogger.Instance);

    private sealed class BackfillFileStore : ICvFileStore
    {
        public Task<string> SaveAsync(CvId id, string originalFileName, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storedFileName) { }

        public string GetAbsolutePath(string storedFileName) => Path.Combine(Path.GetTempPath(), storedFileName);

        public IReadOnlyList<StoredCvFile> EnumerateAll() => [];

        public ICvDirectorySwap StageSwap(IReadOnlyList<CvFilePayload> files) => throw new NotSupportedException();
    }
}
