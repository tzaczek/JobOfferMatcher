using System.Text.Json;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Infrastructure.Sources.NoFluffJobs;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// A deterministic offline <see cref="INoFluffJobsClient"/> that serves the recorded LIST fixture once
/// (page 1) — lets tests run a real collection with no live API call (Principle V/VI). The fixture
/// keeps a multilocation cluster intact so reference-dedup is exercised end-to-end.
/// </summary>
public sealed class FixtureNoFluffJobsClient : INoFluffJobsClient
{
    private readonly JsonDocument _list = FixtureLoader.Load("nofluffjobs/list.json");

    public Task<(SourceFetchStatus Status, SourceListPage? Page)> FetchListAsync(JobSourceSearch search, int page, CancellationToken ct)
    {
        if (page != 1)
        {
            return Task.FromResult<(SourceFetchStatus, SourceListPage?)>((SourceFetchStatus.Ok, new SourceListPage([], 1)));
        }

        var root = _list.RootElement;
        var items = root.GetProperty("postings").EnumerateArray().Select(e => e.Clone()).ToList();
        var totalPages = root.TryGetProperty("totalPages", out var tp) ? tp.GetInt32() : 1;
        return Task.FromResult<(SourceFetchStatus, SourceListPage?)>((SourceFetchStatus.Ok, new SourceListPage(items, totalPages)));
    }
}
