using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// The collection port every source implements (contracts/ijobsource-port.md), so sources are
/// added without redesign (FR-003) under one lightest-reliable-first / escalate-on-block model
/// (FR-040). Only Domain types cross the port.
/// </summary>
/// <remarks>
/// The contract sketches <c>IAsyncEnumerable&lt;CollectedOffer&gt;</c>; this realization streams via
/// an <paramref name="onOffer"/> callback so the adapter can ALSO return the pass
/// <see cref="CollectionResult"/> (outcome + count) — a stream alone cannot carry a terminal value.
/// The orchestrator upserts incrementally inside the callback. Adapters honor polite pacing internally.
/// </remarks>
public interface IJobSource
{
    SourceId Id { get; }
    SourceKind Kind { get; }

    Task<CollectionResult> CollectAsync(
        JobSourceSearch search,
        Func<CollectedOffer, CancellationToken, Task> onOffer,
        CancellationToken ct);
}
