using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Integration test (T023, real Postgres): one scan collects + upserts offers, and GET /api/offers
/// returns them with details + a working canonical link (US1 acceptance). Requires Docker.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ScanCollectDisplayTests(PostgresFixture postgres)
{
    [Fact]
    public async Task One_scan_collects_and_offers_endpoint_returns_them_with_links()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new FixtureJustJoinItClient());
        var client = factory.CreateClient();

        var run = await client.PostAsJsonAsync("/api/scans/run", new RunBody(null));
        run.EnsureSuccessStatusCode();

        var offers = await client.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=available");

        offers.ShouldNotBeNull();
        // Office-only offer is filtered out client-side; 3 remote/hybrid offers remain.
        offers!.Data.Count.ShouldBe(3);

        var acme = offers.Data.Single(o => o.Title == "Senior .NET Engineer");
        acme.Company.ShouldBe("Acme Software");
        acme.CanonicalUrl.ShouldBe("https://justjoin.it/job-offer/senior-dotnet-engineer-acme-krakow");
        acme.SalaryBands.Count.ShouldBe(2);

        // Offer with hidden salary still appears, marked unknown (empty bands) — FR-010.
        var stealth = offers.Data.Single(o => o.Title == "Stealth .NET Role");
        stealth.SalaryBands.ShouldBeEmpty();
    }

    private sealed record RunBody(string[]? SourceIds);

    private sealed record OffersEnvelope(List<OfferItem> Data, MetaItem Meta);

    private sealed record OfferItem(string Title, string Company, string CanonicalUrl, List<BandItem> SalaryBands);

    private sealed record BandItem(decimal? Min, decimal? Max, string? Currency);

    private sealed record MetaItem(int Total, int New);
}
