using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.TailoredCvs;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static JobOfferMatcher.Infrastructure.Tests.TailoredCvs.TailoredCvFlow;

namespace JobOfferMatcher.Infrastructure.Tests.TailoredCvs;

/// <summary>
/// Tailored-CV end-to-end on real Postgres with a fake <c>IPdfRenderer</c> (T027 US1, T039 US3, T046 US4):
/// draft → generate → pending → results(HTML) → produced → preview/download; download 409 while not ready;
/// list/delete; and a tailored CV that survives its offer being marked unavailable (FR-018).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TailoredCvFlowTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Draft_generate_pending_results_produced_preview_round_trips_on_real_postgres()
    {
        await postgres.ResetAsync();
        var (apiFactory, cvDir) = NewFactory(postgres.ConnectionString);
        await using var _f = apiFactory;
        var host = WithFakeRenderer(apiFactory);
        var client = host.CreateClient();

        var offerId = await SeedOfferAndCvAsync(host, cvDir, "tcv-1");

        // Draft — server-composed prompt + emphasised-skills + the attached source CV.
        var draft = await client.GetFromJsonAsync<DraftDto>($"/api/tailored-cv/offer/{offerId}/draft");
        draft.ShouldNotBeNull();
        draft!.Prompt.ShouldNotBeNullOrWhiteSpace();
        draft.SourceCv.ShouldNotBeNull();
        draft.AllOfferSkills.ShouldNotBeEmpty();

        // Generate → pending, version 1.
        var gen = await client.PostAsJsonAsync(
            $"/api/tailored-cv/offer/{offerId}",
            new { prompt = draft.Prompt, emphasisedSkills = draft.EmphasisedSkills });
        gen.EnsureSuccessStatusCode();
        var view = await gen.Content.ReadFromJsonAsync<ViewDto>();
        view!.State.ShouldBe("pending");
        view.GenerationVersion.ShouldBe(1);

        // The worker drains the queue.
        var pending = await client.GetFromJsonAsync<PendingEnvelope>("/api/tailored-cv/pending");
        var item = pending!.Items.Single(i => i.OfferId == offerId);
        item.WorkItemId.ShouldBe($"tailored:{offerId}");
        item.GenerationVersion.ShouldBe(1);
        item.SourceCv.Path.ShouldNotBeNullOrEmpty();

        // Write back produced HTML — the backend renders the PDF (faked) and stores both files.
        const string html = "<!doctype html><html><body><h1>Tailored CV</h1><p>Real experience only.</p></body></html>";
        var submit = await client.PostAsJsonAsync("/api/tailored-cv/results", new
        {
            results = new[] { new { workItemId = item.WorkItemId, generationVersion = item.GenerationVersion, status = "produced", html } },
        });
        submit.EnsureSuccessStatusCode();
        var outcome = await submit.Content.ReadFromJsonAsync<SubmitEnvelope>();
        outcome!.Accepted.ShouldBe(1);
        outcome.Results.Single().Outcome.ShouldBe("produced");

        // View → produced + downloadable; jsonb round-trips.
        var produced = await client.GetFromJsonAsync<ViewDto>($"/api/tailored-cv/offer/{offerId}");
        produced!.State.ShouldBe("produced");
        produced.HasPdf.ShouldBeTrue();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.TailoredCvs.AsNoTracking().SingleAsync(t => t.OfferId == OfferId.From(offerId));
            row.State.ShouldBe(TailoredCvState.Produced);
            row.EmphasisedSkills.ShouldBe(draft.EmphasisedSkills); // jsonb round-trip
        }

        // Preview → the stored HTML, served inline.
        var preview = await client.GetAsync($"/api/tailored-cv/offer/{offerId}/preview");
        preview.EnsureSuccessStatusCode();
        preview.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        (await preview.Content.ReadAsStringAsync()).ShouldContain("Tailored CV");
    }

    [Fact]
    public async Task Download_streams_pdf_when_produced_and_409s_when_not_ready()
    {
        await postgres.ResetAsync();
        var (apiFactory, cvDir) = NewFactory(postgres.ConnectionString);
        await using var _f = apiFactory;
        var host = WithFakeRenderer(apiFactory);
        var client = host.CreateClient();

        var producedOffer = await SeedOfferAndCvAsync(host, cvDir, "dl-produced");
        var pendingOffer = await SeedOfferAsync(host, "dl-pending");

        await ProduceAsync(client, producedOffer);

        var download = await client.GetAsync($"/api/tailored-cv/offer/{producedOffer}/download");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await download.Content.ReadAsByteArrayAsync()).Length.ShouldBeGreaterThan(0);

        // A pending tailored CV (generate without a worker pass) is not downloadable.
        var draft = await client.GetFromJsonAsync<DraftDto>($"/api/tailored-cv/offer/{pendingOffer}/draft");
        (await client.PostAsJsonAsync($"/api/tailored-cv/offer/{pendingOffer}", new { prompt = draft!.Prompt, emphasisedSkills = draft.EmphasisedSkills }))
            .EnsureSuccessStatusCode();

        var notReady = await client.GetAsync($"/api/tailored-cv/offer/{pendingOffer}/download");
        notReady.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_returns_all_delete_removes_files_and_a_tailored_cv_survives_an_unavailable_offer()
    {
        await postgres.ResetAsync();
        var (apiFactory, cvDir) = NewFactory(postgres.ConnectionString);
        await using var _f = apiFactory;
        var host = WithFakeRenderer(apiFactory);
        var client = host.CreateClient();

        var offerA = await SeedOfferAndCvAsync(host, cvDir, "list-a");
        var offerB = await SeedOfferAsync(host, "list-b");

        await ProduceAsync(client, offerA);
        await ProduceAsync(client, offerB);

        var list = await client.GetFromJsonAsync<ListEnvelope>("/api/tailored-cv");
        list!.Data.Count.ShouldBe(2);

        // Mark offer A unavailable (delisted) — its tailored CV must persist (FR-018).
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var offer = await db.Offers.SingleAsync(o => o.Id == OfferId.From(offerA));
            offer.MarkUnavailable(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        (await client.GetAsync($"/api/tailored-cv/offer/{offerA}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Delete offer B's tailored CV → 204 + both files gone.
        (await client.DeleteAsync($"/api/tailored-cv/offer/{offerB}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        File.Exists(Path.Combine(cvDir, $"tailored-{offerB:N}.pdf")).ShouldBeFalse();
        File.Exists(Path.Combine(cvDir, $"tailored-{offerB:N}.html")).ShouldBeFalse();

        var after = await client.GetFromJsonAsync<ListEnvelope>("/api/tailored-cv");
        after!.Data.Count.ShouldBe(1);
        after.Data.Single().OfferId.ShouldBe(offerA);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static async Task<Guid> SeedOfferAndCvAsync(WebApplicationFactory<Program> host, string cvDir, string key)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var offer = EnrichmentFlow.AddOfferWithSatellites(db, key);
        // One CV is enough for the whole DB; add it only if none exists yet.
        if (!await db.CandidateCvs.AnyAsync())
        {
            AddReadableCvWithFile(db, cvDir);
        }

        await db.SaveChangesAsync();
        return offer.Id.Value;
    }

    private static async Task<Guid> SeedOfferAsync(WebApplicationFactory<Program> host, string key)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var offer = EnrichmentFlow.AddOfferWithSatellites(db, key);
        await db.SaveChangesAsync();
        return offer.Id.Value;
    }

    private static async Task ProduceAsync(HttpClient client, Guid offerId)
    {
        var draft = await client.GetFromJsonAsync<DraftDto>($"/api/tailored-cv/offer/{offerId}/draft");
        (await client.PostAsJsonAsync($"/api/tailored-cv/offer/{offerId}", new { prompt = draft!.Prompt, emphasisedSkills = draft.EmphasisedSkills }))
            .EnsureSuccessStatusCode();

        var pending = await client.GetFromJsonAsync<PendingEnvelope>("/api/tailored-cv/pending?limit=50");
        var item = pending!.Items.Single(i => i.OfferId == offerId);
        (await client.PostAsJsonAsync("/api/tailored-cv/results", new
        {
            results = new[]
            {
                new { workItemId = item.WorkItemId, generationVersion = item.GenerationVersion, status = "produced", html = $"<html><body>CV {offerId}</body></html>" },
            },
        })).EnsureSuccessStatusCode();
    }
}
