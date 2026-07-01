using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests.TailoredCvs;

/// <summary>
/// Loopback guard for the tailored-CV group (004 T028, Principle IV/ADR-2): the channel carries CV/offer
/// text + the produced PDF, so every <c>/api/tailored-cv/*</c> route rejects a non-loopback caller with
/// 403 <c>LoopbackOnly</c> (fail-closed on a null/unknown remote IP).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TailoredCvLoopbackTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Non_loopback_callers_are_rejected_on_every_tailored_cv_route()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(
            postgres.ConnectionString, new MutableJustJoinItClient(), simulateLoopback: false);
        var client = factory.CreateClient();
        var offerId = Guid.NewGuid();

        (await client.GetAsync("/api/tailored-cv")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/tailored-cv/pending")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/tailored-cv/offer/{offerId}/draft")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/tailored-cv/offer/{offerId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/tailored-cv/offer/{offerId}/preview")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/tailored-cv/offer/{offerId}/download")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var generate = await client.PostAsJsonAsync($"/api/tailored-cv/offer/{offerId}", new { prompt = "x", emphasisedSkills = Array.Empty<string>() });
        generate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var results = await client.PostAsJsonAsync("/api/tailored-cv/results", new { results = Array.Empty<object>() });
        results.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
