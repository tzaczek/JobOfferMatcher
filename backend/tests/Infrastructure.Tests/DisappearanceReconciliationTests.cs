using System.Net.Http.Json;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Infrastructure.Tests.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Integration test (T038, real Postgres): disappearance reconciliation runs only on a Complete
/// scan; the &lt;50% sanity guard downgrades to Partial; a reappeared offer flips back to available.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DisappearanceReconciliationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Absent_offer_becomes_unavailable_on_complete_scan_then_reappears()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("offer-a", 22000), ("offer-b", 20000), ("offer-c", 18000));
        await RunScanAsync(http);
        (await AvailabilityAsync("offer-c")).ShouldBe(AvailabilityStatus.Available);

        // C disappears (2 of 3 remain → 2 ≥ 50% → reconcile).
        client.SetOffers(("offer-a", 22000), ("offer-b", 20000));
        await RunScanAsync(http);
        (await AvailabilityAsync("offer-c")).ShouldBe(AvailabilityStatus.NoLongerAvailable);
        (await AvailabilityAsync("offer-a")).ShouldBe(AvailabilityStatus.Available);

        // C returns → reappears.
        client.SetOffers(("offer-a", 22000), ("offer-b", 20000), ("offer-c", 18000));
        await RunScanAsync(http);
        (await AvailabilityAsync("offer-c")).ShouldBe(AvailabilityStatus.Available);
    }

    [Fact]
    public async Task Sanity_guard_downgrades_to_partial_and_does_not_mass_kill_offers()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("g-a", 22000), ("g-b", 20000), ("g-c", 18000), ("g-d", 16000));
        await RunScanAsync(http);

        // A catastrophic drop to 1/4 (< 50%) → Partial, no reconciliation.
        client.SetOffers(("g-a", 22000));
        var scanRunId = await RunScanAsync(http);

        (await OutcomeAsync(scanRunId)).ShouldBe(ScanOutcome.Partial);
        // The other three offers must NOT be falsely killed.
        (await AvailabilityAsync("g-b")).ShouldBe(AvailabilityStatus.Available);
        (await AvailabilityAsync("g-d")).ShouldBe(AvailabilityStatus.Available);
    }

    private static async Task<string> RunScanAsync(HttpClient http)
    {
        var response = await http.PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RunResult>();
        return body!.ScanRunId;
    }

    private async Task<AvailabilityStatus> AvailabilityAsync(string nativeKey)
    {
        await using var db = postgres.CreateContext();
        var offer = await db.Offers.FirstAsync(o => o.ExternalRef.NativeKey == nativeKey);
        return offer.Availability;
    }

    private async Task<ScanOutcome> OutcomeAsync(string scanRunId)
    {
        await using var db = postgres.CreateContext();
        var id = Domain.Common.Ids.ScanRunId.From(Guid.Parse(scanRunId));
        var run = await db.ScanRuns.FirstAsync(r => r.Id == id);
        return run.Outcome;
    }

    private sealed record RunResult(string ScanRunId);
}
