using System.Text.Json;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobOfferMatcher.Infrastructure.Sources.TheProtocol;

/// <summary>
/// <see cref="IJobSource"/> adapter for theprotocol.it. Pagination (dedup by offer <c>id</c>, terminate
/// on page count / zero-new, hard cap) and the workplace filter live in <see cref="PagedCollector"/>
/// and this class; the 403/429/challenge → Partial/ChallengeDetected escalation is shared (FR-040).
/// The offer list comes from the page's <c>__NEXT_DATA__</c> via the client.
/// </summary>
public sealed class TheProtocolSource(
    SourceId id,
    ITheProtocolClient client,
    IOptions<TheProtocolOptions> options,
    ILogger<TheProtocolSource> logger) : IJobSource
{
    private readonly TheProtocolOptions _options = options.Value;

    public SourceId Id => id;
    public SourceKind Kind => SourceKind.DirectApi;

    public Task<CollectionResult> CollectAsync(
        JobSourceSearch search,
        Func<CollectedOffer, CancellationToken, Task> onOffer,
        CancellationToken ct) =>
        PagedCollector.CollectAsync(
            sourceName: "theprotocol",
            maxPages: _options.MaxPages,
            fetchPage: (page, token) => client.FetchListAsync(search, page, token),
            identityOf: TheProtocolMapper.GetId,
            tryEmit: (item, token) => TryEmitAsync(item, search, onOffer, token),
            logger: logger,
            ct: ct);

    /// <summary>No detail-body fetch wired for this source (feature 006 targets justjoin.it) → "not available".</summary>
    public Task<string?> FetchBodyAsync(CollectedOffer offer, CancellationToken ct) => Task.FromResult<string?>(null);

    private async Task<bool> TryEmitAsync(
        JsonElement item,
        JobSourceSearch search,
        Func<CollectedOffer, CancellationToken, Task> onOffer,
        CancellationToken ct)
    {
        var mapped = TheProtocolMapper.MapListItem(item, Id, _options.OfferUrlTemplate);
        if (mapped.IsFailure)
        {
            logger.LogWarning("Skipping unmappable theprotocol offer: {Error}", mapped.Error);
            return false;
        }

        var mode = mapped.Value.Content.WorkMode;
        if (!WorkplaceFilter.Keep(mode, search.WorkplaceKeep, out var unknown))
        {
            return false;
        }

        if (unknown)
        {
            logger.LogInformation("Keeping theprotocol offer with unknown work mode (flagged, not dropped).");
        }

        await onOffer(mapped.Value, ct);
        return true;
    }
}
