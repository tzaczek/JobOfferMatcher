using System.Net.Http.Json;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Persistence.Seed;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Infrastructure.Sources.LinkedIn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// Feature 008, US3 (T031, real Postgres): LinkedIn degradation never drops prior offers. A blocked pass
/// yields <see cref="ScanOutcome.Partial"/>/<see cref="IncompleteReason.ChallengeDetected"/>; a scan that
/// returns &lt; 50% of the previous complete count trips the orchestrator's sanity guard →
/// <see cref="ScanOutcome.Partial"/>/<see cref="IncompleteReason.LayoutChanged"/> with reconciliation
/// skipped, so live offers are not mass-marked unavailable (FR-015, SC-004).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LinkedInDegradationTests(PostgresFixture postgres)
{
    private static readonly string LinkedInSourceId = DatabaseSeeder.DefaultLinkedInSourceId.Value.ToString();

    [Fact]
    public async Task A_blocked_pass_records_partial_challenge_detected()
    {
        await postgres.ResetAsync();
        var linkedIn = new FakeLinkedInClient();
        linkedIn.SetRecommended(SourceFetchStatus.Blocked); // the recommended pass hits a checkpoint wall

        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient(), fakeLinkedInClient: linkedIn);
        var http = factory.CreateClient();

        await ScanAsync(http);

        var run = await LatestRunAsync(factory);
        run.Outcome.ShouldBe(ScanOutcome.Partial);
        run.IncompleteReason.ShouldBe(IncompleteReason.ChallengeDetected);
    }

    [Fact]
    public async Task A_thin_rescan_is_downgraded_to_layout_changed_and_keeps_prior_offers()
    {
        await postgres.ResetAsync();
        var linkedIn = new FakeLinkedInClient();
        linkedIn.SetRecommended(SourceFetchStatus.Ok, Cards(1, 10)); // 10 offers

        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient(), fakeLinkedInClient: linkedIn);
        var http = factory.CreateClient();

        await ScanAsync(http);
        (await OfferCountAsync(factory)).ShouldBe(10);

        // A suspiciously thin re-scan (2 < 50% of 10) — an anti-bot/layout break, not a real disappearance.
        linkedIn.SetRecommended(SourceFetchStatus.Ok, Cards(1, 2));
        await ScanAsync(http);

        var run = await LatestRunAsync(factory);
        run.Outcome.ShouldBe(ScanOutcome.Partial);
        run.IncompleteReason.ShouldBe(IncompleteReason.LayoutChanged);

        // The sanity guard skipped reconciliation → the 8 unseen offers are NOT mass-marked unavailable.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var offers = await db.Offers.ToListAsync();
        offers.Count.ShouldBe(10);
        offers.ShouldAllBe(o => o.Availability == AvailabilityStatus.Available);
    }

    private static LinkedInJobCard[] Cards(int start, int count) =>
        [.. Enumerable.Range(start, count).Select(i => FakeLinkedInClient.Card($"job-{i}"))];

    private static Task<HttpResponseMessage> ScanAsync(HttpClient http) =>
        http.PostAsJsonAsync("/api/scans/run", new { sourceIds = new[] { LinkedInSourceId } });

    private static async Task<int> OfferCountAsync(JobApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Offers.CountAsync();
    }

    private static async Task<ScanRun> LatestRunAsync(JobApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ScanRuns.OrderByDescending(r => r.StartedAt).FirstAsync();
    }
}
