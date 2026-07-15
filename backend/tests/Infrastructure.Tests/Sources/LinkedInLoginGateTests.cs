using System.Net.Http.Json;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Persistence.Seed;
using JobOfferMatcher.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// Feature 008, US3 (T030, real Postgres): an unattended (Scheduled) scan whose session is invalid must
/// record <see cref="ScanOutcome.Failed"/> / <see cref="IncompleteReason.LoginNotCompleted"/>, attempt
/// <b>no</b> interactive login (no window), and — because a non-Complete scan never reconciles — retain
/// the LinkedIn offers a prior manual scan collected (FR-015, SC-004).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LinkedInLoginGateTests(PostgresFixture postgres)
{
    private static readonly string LinkedInSourceId = DatabaseSeeder.DefaultLinkedInSourceId.Value.ToString();

    [Fact]
    public async Task Unattended_scan_without_a_session_fails_login_and_retains_prior_offers()
    {
        await postgres.ResetAsync();
        var linkedIn = new FakeLinkedInClient();
        linkedIn.SetRecommended(SourceFetchStatus.Ok, FakeLinkedInClient.Card("A"), FakeLinkedInClient.Card("B"));

        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient(), fakeLinkedInClient: linkedIn);
        var http = factory.CreateClient();

        // Manual scan (attended login succeeds) collects two LinkedIn offers.
        (await http.PostAsJsonAsync("/api/scans/run", new { sourceIds = new[] { LinkedInSourceId } })).EnsureSuccessStatusCode();
        await AssertOffersAsync(factory, expected: 2, allAvailable: true);

        // Now the session is invalid and only an interactive login could re-establish it.
        linkedIn.LoginResult = interactive => interactive;

        // An unattended (Scheduled) run: no window, no hang → Failed/LoginNotCompleted.
        ScanRunId scheduledRun;
        using (var scope = factory.Services.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IScanRunner>();
            var result = await runner.RunAsync(new ScanRequest([DatabaseSeeder.DefaultLinkedInSourceId], TriggerType.Scheduled));
            result.IsSuccess.ShouldBeTrue();
            scheduledRun = result.Value;
        }

        linkedIn.LastInteractive.ShouldBe(false); // never attempted an interactive login (unattended)

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = await db.ScanRuns.SingleAsync(r => r.Id == scheduledRun);
            run.Outcome.ShouldBe(ScanOutcome.Failed);
            run.IncompleteReason.ShouldBe(IncompleteReason.LoginNotCompleted);
        }

        // The prior offers are untouched — a Failed scan never reconciles disappearances (SC-004).
        await AssertOffersAsync(factory, expected: 2, allAvailable: true);
    }

    private static async Task AssertOffersAsync(JobApiFactory factory, int expected, bool allAvailable)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var offers = await db.Offers.ToListAsync();
        offers.Count.ShouldBe(expected);
        if (allAvailable)
        {
            offers.ShouldAllBe(o => o.Availability == AvailabilityStatus.Available);
        }
    }
}
