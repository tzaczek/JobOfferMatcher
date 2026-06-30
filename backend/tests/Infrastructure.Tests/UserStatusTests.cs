using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Integration test (T038a, real Postgres): a user status persists across scans, and a Dismissed
/// offer never re-appears as new (FR-031 / SC-002).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UserStatusTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Dismissed_offer_persists_and_never_reappears_as_new_across_scans()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("keep-me", 22000), ("dismiss-me", 20000));
        await RunScanAsync(http);

        var offers = await GetOffersAsync(http, "all");
        var toDismiss = offers.Single(o => o.Title == "Role dismiss-me");

        var dismiss = await http.PostAsJsonAsync($"/api/offers/{toDismiss.OfferId}/status", new { status = "dismissed" });
        dismiss.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Re-scan: the dismissed offer is still present + unchanged.
        await RunScanAsync(http);

        var newOffers = await GetOffersAsync(http, "new");
        newOffers.ShouldNotContain(o => o.Title == "Role dismiss-me"); // never re-new (SC-002)
        newOffers.ShouldContain(o => o.Title == "Role keep-me");

        var dismissed = await GetOffersAsync(http, "dismissed");
        dismissed.ShouldContain(o => o.Title == "Role dismiss-me"); // status persisted
    }

    [Fact]
    public async Task Dismissed_offer_is_hidden_from_the_active_feed_and_returns_on_restore()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("keep-me", 22000), ("dismiss-me", 20000));
        await RunScanAsync(http);

        var toDismiss = (await GetOffersAsync(http, "all")).Single(o => o.Title == "Role dismiss-me");
        var dismiss = await http.PostAsJsonAsync($"/api/offers/{toDismiss.OfferId}/status", new { status = "dismissed" });
        dismiss.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The default feed (active) hides the dismissed offer; the other one stays.
        var active = await GetOffersAsync(http, "active");
        active.ShouldNotContain(o => o.Title == "Role dismiss-me");
        active.ShouldContain(o => o.Title == "Role keep-me");

        // Restore (→ viewed) lifts it back into the active feed, but never back to "new" (SC-002).
        var restore = await http.PostAsJsonAsync($"/api/offers/{toDismiss.OfferId}/status", new { status = "viewed" });
        restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var restored = (await GetOffersAsync(http, "active")).Single(o => o.Title == "Role dismiss-me");
        restored.UserStatus.ShouldBe("viewed");
        restored.IsNew.ShouldBeFalse();
        (await GetOffersAsync(http, "new")).ShouldNotContain(o => o.Title == "Role dismiss-me");
    }

    [Fact]
    public async Task Status_cannot_be_set_back_to_new()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("only", 22000));
        await RunScanAsync(http);
        var offer = (await GetOffersAsync(http, "all")).Single();

        var response = await http.PostAsJsonAsync($"/api/offers/{offer.OfferId}/status", new { status = "new" });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task RunScanAsync(HttpClient http)
    {
        var response = await http.PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<List<OfferItem>> GetOffersAsync(HttpClient http, string status)
    {
        var envelope = await http.GetFromJsonAsync<OffersEnvelope>($"/api/offers?status={status}&availability=all");
        return envelope!.Data;
    }

    private sealed record OffersEnvelope(List<OfferItem> Data);

    private sealed record OfferItem(string OfferId, string Title, bool IsNew, string UserStatus);
}
