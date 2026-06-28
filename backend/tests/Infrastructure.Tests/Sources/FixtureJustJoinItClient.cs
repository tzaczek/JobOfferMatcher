using System.Text.Json;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Sources.JustJoinIt;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// A deterministic offline <see cref="IJustJoinItClient"/> that serves the recorded LIST fixture
/// once (page from=0) — lets integration tests run a real scan with no live API call (Principle V/VI).
/// </summary>
public sealed class FixtureJustJoinItClient : IJustJoinItClient
{
    private readonly JsonDocument _list = FixtureLoader.Load("justjoinit/list.json");

    public Task<(FetchStatus Status, JustJoinItPage? Page)> FetchListAsync(JobSourceSearch search, int from, CancellationToken ct)
    {
        if (from != 0)
        {
            return Task.FromResult<(FetchStatus, JustJoinItPage?)>((FetchStatus.Ok, new JustJoinItPage([], 4, null)));
        }

        var items = _list.RootElement.GetProperty("data").EnumerateArray().Select(e => e.Clone()).ToList();
        var total = _list.RootElement.GetProperty("meta").GetProperty("totalItems").GetInt64();
        return Task.FromResult<(FetchStatus, JustJoinItPage?)>((FetchStatus.Ok, new JustJoinItPage(items, total, null)));
    }

    public Task<JsonElement?> FetchDetailAsync(string slug, CancellationToken ct) =>
        Task.FromResult<JsonElement?>(null);
}
