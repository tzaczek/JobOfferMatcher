using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Infrastructure.Sources.Generic;

/// <summary>
/// Scaffold for a SECOND <see cref="SourceKind.DirectApi"/> source (FR-003). It implements the same
/// <see cref="IJobSource"/> port as justjoin.it, proving the orchestrator + ranking + feed are
/// source-agnostic (no justjoin.it specifics leak past the adapter). A real second source supplies
/// its own client + mapper here; until then it collects nothing (a clean Complete with zero offers).
/// </summary>
public sealed class GenericDirectApiSource(SourceId id, ILogger<GenericDirectApiSource> logger) : IJobSource
{
    public SourceId Id => id;
    public SourceKind Kind => SourceKind.DirectApi;

    public Task<CollectionResult> CollectAsync(
        JobSourceSearch search,
        Func<CollectedOffer, CancellationToken, Task> onOffer,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Source {SourceId} uses the generic DirectApi scaffold — implement its client+mapper to collect offers.", id);
        return Task.FromResult(CollectionResult.Complete(0));
    }
}
