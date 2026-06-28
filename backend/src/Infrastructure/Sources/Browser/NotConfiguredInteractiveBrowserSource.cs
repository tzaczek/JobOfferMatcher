using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Infrastructure.Sources.Browser;

/// <summary>
/// Stub <see cref="IJobSource"/> for <see cref="SourceKind.InteractiveBrowser"/> sources. Records a
/// clean Incomplete (LoginNotCompleted) instead of crashing — the real Playwright adapter is
/// deferred (T030 / FR-040). Keeps the orchestrator source-agnostic today.
/// </summary>
public sealed class NotConfiguredInteractiveBrowserSource(SourceId id, ILogger<NotConfiguredInteractiveBrowserSource> logger)
    : IJobSource
{
    public SourceId Id => id;
    public SourceKind Kind => SourceKind.InteractiveBrowser;

    public Task<CollectionResult> CollectAsync(
        JobSourceSearch search,
        Func<CollectedOffer, CancellationToken, Task> onOffer,
        CancellationToken ct)
    {
        logger.LogWarning(
            "Source {SourceId} requires interactive-browser collection, which is deferred — recording Incomplete.", id);
        return Task.FromResult(CollectionResult.Failed(IncompleteReason.LoginNotCompleted, 0));
    }
}
