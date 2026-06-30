using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Infrastructure.Sources.NoFluffJobs;

/// <summary>
/// Thin transport over the nofluffjobs.com posting-search endpoint. Separated from pagination/mapping
/// so the pagination + escalation logic is unit-tested offline against recorded fixtures (Principles V/VI).
/// </summary>
public interface INoFluffJobsClient
{
    Task<(SourceFetchStatus Status, SourceListPage? Page)> FetchListAsync(JobSourceSearch search, int page, CancellationToken ct);
}
