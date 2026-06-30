using System.Net.Http.Json;
using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Tests.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static JobOfferMatcher.Infrastructure.Tests.EnrichmentFlow;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// US5 end-to-end on real Postgres (T058, FR-009/FR-014/SC-007): the startup backfill makes the pending
/// count correct for pre-existing offers; a full <c>/enrich</c> pass drains <c>pendingTotal</c> to 0; and
/// <c>/rerun all</c> forces a fresh full re-run.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EnrichmentRerunFlowTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Backfill_makes_counts_correct_then_a_pass_drains_to_zero_and_rerun_rearms()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var client = factory.CreateClient();

        // Pre-existing offers WITHOUT satellites (as if collected before the feature was enabled).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Offers.Add(SeedOffer("rerun-1"));
            db.Offers.Add(SeedOffer("rerun-2"));
            db.Offers.Add(SeedOffer("rerun-3"));
            await db.SaveChangesAsync();
        }

        // Before the backfill, there are no satellite rows ⇒ the count is (incorrectly) 0.
        var preBackfill = await client.GetFromJsonAsync<StatusDto>("/api/enrichment/status");
        preBackfill!.PendingTotal.ShouldBe(0);

        // Run the idempotent startup backfill (FR-014).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await DatabaseInitializer.BackfillEnrichmentAsync(
                db,
                scope.ServiceProvider.GetRequiredService<ICvFileStore>(),
                scope.ServiceProvider.GetRequiredService<ICvTextExtractor>(),
                TimeProvider.System,
                NullLogger.Instance);
        }

        // Now the pending count reflects every offer (no CV ⇒ summaries only, no fits).
        var afterBackfill = await client.GetFromJsonAsync<StatusDto>("/api/enrichment/status");
        afterBackfill!.PendingSummaries.ShouldBe(3);
        afterBackfill.PendingFits.ShouldBe(0);
        afterBackfill.PendingTotal.ShouldBe(3);

        // A full worker pass produces every pending summary.
        await DrainSummariesAsync(client);

        var drained = await client.GetFromJsonAsync<StatusDto>("/api/enrichment/status");
        drained!.PendingTotal.ShouldBe(0); // SC-007

        // /rerun all forces a fresh full re-run (FR-009).
        var rerun = await client.PostAsJsonAsync("/api/enrichment/rerun", new RerunBody("all"));
        rerun.EnsureSuccessStatusCode();
        var afterRerun = await rerun.Content.ReadFromJsonAsync<StatusDto>();
        afterRerun!.PendingSummaries.ShouldBe(3);
        afterRerun.PendingTotal.ShouldBe(3);
    }

    private static async Task DrainSummariesAsync(HttpClient client)
    {
        var pending = await client.GetFromJsonAsync<PendingEnvelope>("/api/enrichment/pending?limit=100");
        var results = pending!.Items
            .Where(i => i.Kind == "offerSummary")
            .Select(i => new
            {
                workItemId = i.WorkItemId,
                kind = "offerSummary",
                inputsHash = i.InputsHash,
                status = "produced",
                summary = "A concise summary.",
                keySkills = new[] { "C#" },
            })
            .ToArray();

        var submit = await client.PostAsJsonAsync("/api/enrichment/results", new { results });
        submit.EnsureSuccessStatusCode();
    }
}
