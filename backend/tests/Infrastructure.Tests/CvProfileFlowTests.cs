using System.Net.Http.Json;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Tests.Sources;
using Microsoft.Extensions.DependencyInjection;
using static JobOfferMatcher.Infrastructure.Tests.EnrichmentFlow;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// US3 end-to-end on real Postgres (T042, SC-002): a pending CV becomes a produced profile after the
/// worker write-back; an image-only/garbled CV is recorded <c>unreadable</c> (no crash, counted
/// separately from failed).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CvProfileFlowTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Pending_cv_becomes_a_produced_profile()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cv = CandidateCv.Create(CvId.New(), "cv.pdf");
            cv.SetExtractionGauge(true, "SHA256:1:bytes", DateTimeOffset.UtcNow);
            db.CandidateCvs.Add(cv);
            await db.SaveChangesAsync();
        }

        var pending = await client.GetFromJsonAsync<PendingEnvelope>("/api/enrichment/pending");
        var item = pending!.Items.Single(i => i.Kind == "cvProfile");

        var submit = await client.PostAsJsonAsync("/api/enrichment/results", new
        {
            results = new[]
            {
                new
                {
                    workItemId = item.WorkItemId,
                    kind = "cvProfile",
                    inputsHash = item.InputsHash,
                    status = "produced",
                    skills = new[] { "C#", "PostgreSQL" },
                    seniority = "Senior",
                    summary = "Backend engineer focused on .NET.",
                },
            },
        });
        submit.EnsureSuccessStatusCode();

        var cvs = await client.GetFromJsonAsync<CvEnvelope>("/api/cv");
        var produced = cvs!.Data.Single();
        produced.State.ShouldBe("produced");
        produced.Seniority.ShouldBe("Senior");
        produced.Skills.ShouldBe(["C#", "PostgreSQL"]);
        produced.Summary.ShouldBe("Backend engineer focused on .NET.");
    }

    [Fact]
    public async Task Image_only_cv_is_recorded_unreadable()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cv = CandidateCv.Create(CvId.New(), "scan.pdf");
            cv.SetExtractionGauge(false, "SHA256:1:imagebytes", DateTimeOffset.UtcNow); // image-only gauge
            db.CandidateCvs.Add(cv);
            await db.SaveChangesAsync();
        }

        var pending = await client.GetFromJsonAsync<PendingEnvelope>("/api/enrichment/pending");
        var item = pending!.Items.Single(i => i.Kind == "cvProfile");

        var submit = await client.PostAsJsonAsync("/api/enrichment/results", new
        {
            results = new[]
            {
                new { workItemId = item.WorkItemId, kind = "cvProfile", inputsHash = item.InputsHash, status = "unreadable" },
            },
        });
        submit.EnsureSuccessStatusCode();
        var result = await submit.Content.ReadFromJsonAsync<SubmitEnvelope>();
        result!.Results.Single().Outcome.ShouldBe("unreadable");

        var cvs = await client.GetFromJsonAsync<CvEnvelope>("/api/cv");
        cvs!.Data.Single().State.ShouldBe("unreadable");

        // Unreadable counts as neither pending nor failed (SC-002): the queue can reach 0.
        var status = await client.GetFromJsonAsync<StatusDto>("/api/enrichment/status");
        status!.PendingProfiles.ShouldBe(0);
        status.FailedTotal.ShouldBe(0);
    }
}
