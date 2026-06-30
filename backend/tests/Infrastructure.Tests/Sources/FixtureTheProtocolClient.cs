using System.Text.Json;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Infrastructure.Sources.TheProtocol;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// A deterministic offline <see cref="ITheProtocolClient"/> that serves the recorded LIST fixture once
/// (page 1). The fixture is the <c>offersResponse</c> object (as embedded in the page's __NEXT_DATA__),
/// so collection runs with no live request and no HTML parsing (Principle V/VI).
/// </summary>
public sealed class FixtureTheProtocolClient : ITheProtocolClient
{
    private readonly JsonDocument _list = FixtureLoader.Load("theprotocol/list.json");

    public Task<(SourceFetchStatus Status, SourceListPage? Page)> FetchListAsync(JobSourceSearch search, int page, CancellationToken ct)
    {
        if (page != 1)
        {
            return Task.FromResult<(SourceFetchStatus, SourceListPage?)>((SourceFetchStatus.Ok, new SourceListPage([], 1)));
        }

        var root = _list.RootElement;
        var items = root.GetProperty("offers").EnumerateArray().Select(e => e.Clone()).ToList();
        var totalPages = root.GetProperty("page").GetProperty("count").GetInt32();
        return Task.FromResult<(SourceFetchStatus, SourceListPage?)>((SourceFetchStatus.Ok, new SourceListPage(items, totalPages)));
    }
}
