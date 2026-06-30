using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Infrastructure.Sources.TheProtocol;

/// <summary>
/// Thin transport over the theprotocol.it listing page. Separated from pagination/mapping so the
/// pagination + escalation logic is unit-tested offline against recorded fixtures (Principles V/VI).
/// </summary>
public interface ITheProtocolClient
{
    Task<(SourceFetchStatus Status, SourceListPage? Page)> FetchListAsync(JobSourceSearch search, int page, CancellationToken ct);
}
