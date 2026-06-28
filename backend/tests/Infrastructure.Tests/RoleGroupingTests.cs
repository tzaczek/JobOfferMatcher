using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Integration test (T062/T064, real Postgres): two offers for the same role collapse into one
/// role-grouped feed entry on scan (FR-016), and a user "not same" override splits them back apart
/// (the persisted override wins over the heuristic).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RoleGroupingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Same_role_offers_collapse_into_one_entry_and_override_splits_them()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        // Two listings for the SAME role (same company + title, compatible remote location).
        client.SetDetailedOffers(
            ("role-a", "Acme Software", "Senior .NET Engineer", "Remote", "remote"),
            ("role-b", "Acme Software", "Senior .NET Engineer", "Remote", "remote"));

        await RunScanAsync(http);

        var grouped = await GetOffersAsync(http);
        grouped.Count.ShouldBe(1); // collapsed to one entry per group (FR-016)
        var entry = grouped.Single();
        entry.RoleGroupId.ShouldNotBeNull();
        entry.GroupMembers.Count.ShouldBe(1); // the other member is listed under the entry

        // The user says "not same" → the persisted override wins; the group no longer collapses.
        var ovr = await http.PostAsJsonAsync(
            $"/api/role-groups/{entry.RoleGroupId}/override", new { @override = "notSame" });
        ovr.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var split = await GetOffersAsync(http);
        split.Count.ShouldBe(2); // override beats the heuristic (FR-016)
    }

    [Fact]
    public async Task Unrelated_offers_are_not_grouped()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        // Both remote (so neither is dropped by the workplace filter) but a different
        // company AND title — the gate must still refuse to merge them.
        client.SetDetailedOffers(
            ("x-1", "Acme Software", "Senior .NET Engineer", "Remote", "remote"),
            ("x-2", "Globex", "Junior Frontend React Developer", "Remote", "remote"));

        await RunScanAsync(http);

        var offers = await GetOffersAsync(http);
        offers.Count.ShouldBe(2); // different company/title → no merge (conservative default)
        offers.ShouldAllBe(o => o.GroupMembers.Count == 0);
    }

    private static async Task RunScanAsync(HttpClient http)
    {
        var response = await http.PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<List<OfferItem>> GetOffersAsync(HttpClient http)
    {
        var envelope = await http.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all");
        return envelope!.Data;
    }

    private sealed record OffersEnvelope(List<OfferItem> Data);

    private sealed record OfferItem(string OfferId, string? RoleGroupId, string Title, List<GroupMember> GroupMembers);

    private sealed record GroupMember(string OfferId, string SourceName, string CanonicalUrl);
}
