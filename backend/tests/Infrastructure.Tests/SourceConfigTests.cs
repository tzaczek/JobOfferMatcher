using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Persistence.Seed;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Integration test (T060a, real Postgres): a source's search criteria edited via /api/sources are
/// honored by the next scan, and enable/disable toggles whether it is collected (FR-002/FR-003).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SourceConfigTests(PostgresFixture postgres)
{
    private static readonly string SourceId = DatabaseSeeder.DefaultJustJoinItSourceId.Value.ToString();

    [Fact]
    public async Task Edited_search_criteria_are_passed_to_the_next_scan()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("offer-a", 20000));
        var update = await http.PutAsJsonAsync($"/api/sources/{SourceId}", new
        {
            name = "justjoin.it",
            searchCriteria = new
            {
                categories = new[] { "12" },
                experienceLevels = new[] { "senior" },
                employmentTypes = new[] { "b2b" },
                workingTimes = new[] { "full_time" },
                withSalary = true,
                sortBy = "salary",
                orderBy = "DESC",
                workplaceKeep = new[] { "remote", "hybrid" },
            },
            requiresLogin = false,
        });
        update.EnsureSuccessStatusCode();

        await RunScanAsync(http);

        client.LastSearch.ShouldNotBeNull();
        client.LastSearch!.Categories.ShouldBe(["12"]); // the edited category reached the adapter
    }

    [Fact]
    public async Task Disabling_a_source_stops_it_being_collected()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("offer-a", 20000), ("offer-b", 18000));

        (await http.PostAsync($"/api/sources/{SourceId}/disable", null)).EnsureSuccessStatusCode();
        await RunScanAsync(http);
        (await CountOffersAsync(http)).ShouldBe(0); // disabled → nothing collected

        (await http.PostAsync($"/api/sources/{SourceId}/enable", null)).EnsureSuccessStatusCode();
        await RunScanAsync(http);
        (await CountOffersAsync(http)).ShouldBe(2); // enabled → collected
    }

    private static async Task RunScanAsync(HttpClient http)
    {
        var response = await http.PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<int> CountOffersAsync(HttpClient http)
    {
        var envelope = await http.GetFromJsonAsync<Envelope>("/api/offers?status=all&availability=all");
        return envelope!.Data.Count;
    }

    private sealed record Envelope(List<object> Data);
}
