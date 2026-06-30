using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Tests.Sources;
using Microsoft.Extensions.DependencyInjection;
using static JobOfferMatcher.Infrastructure.Tests.EnrichmentFlow;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// US4 end-to-end on real Postgres (T051, SC-003/SC-004): with a produced CV profile a fit goes
/// pending → produced via the worker write-back; a weights change invalidates every fit back to
/// pending (never showing a stale or non-AI score — FR-005/FR-007).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OfferFitFlowTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Fit_goes_pending_to_produced_then_pending_again_on_weights_change()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            AddOfferWithSatellites(db, "fit-1");
            AddProducedCv(db);
            await db.SaveChangesAsync();
        }

        // Produced profile exists but the fit isn't matched yet ⇒ fit state is pending, no score (FR-005).
        var before = await client.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all");
        var pendingFit = before!.Data.Single();
        pendingFit.Fit.ShouldNotBeNull();
        pendingFit.Fit!.State.ShouldBe("pending");
        pendingFit.Fit.Score.ShouldBeNull();

        // Drain the fit work item.
        var pending = await client.GetFromJsonAsync<PendingEnvelope>("/api/enrichment/pending");
        var item = pending!.Items.Single(i => i.Kind == "offerFit");
        var submit = await client.PostAsJsonAsync("/api/enrichment/results", new
        {
            results = new[]
            {
                new
                {
                    workItemId = item.WorkItemId,
                    kind = "offerFit",
                    inputsHash = item.InputsHash,
                    status = "produced",
                    score = 82,
                    matched = new[] { "C#", ".NET" },
                    missing = new[] { "Kafka" },
                    rationale = "Strong backend overlap",
                },
            },
        });
        submit.EnsureSuccessStatusCode();

        var produced = await client.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all");
        var fit = produced!.Data.Single().Fit!;
        fit.State.ShouldBe("produced");
        fit.Score.ShouldBe(82);
        fit.Missing.ShouldBe(["Kafka"]);

        // Changing the weights invalidates every fit (SC-004): the score must disappear.
        var put = await client.PutAsJsonAsync("/api/settings/weights", new
        {
            skills = 60,
            seniority = 10,
            workMode = 10,
            employment = 10,
            salary = 10,
        });
        put.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all");
        var afterFit = after!.Data.Single().Fit!;
        afterFit.State.ShouldBe("pending");
        afterFit.Score.ShouldBeNull();

        var status = await client.GetFromJsonAsync<StatusDto>("/api/enrichment/status");
        status!.PendingFits.ShouldBe(1);
    }
}
