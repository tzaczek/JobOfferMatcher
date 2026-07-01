using System.Text.Json;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobOfferMatcher.Infrastructure.Sources.NoFluffJobs;

/// <summary>
/// <see cref="IJobSource"/> adapter for nofluffjobs.com's public posting-search API. The pagination
/// footguns (dedup the multilocation expansion by <c>reference</c>, terminate on totalPages / zero-new,
/// hard cap) and the client-side workplace filter live in <see cref="PagedCollector"/> and this class;
/// the 403/429 → Partial/ChallengeDetected escalation is shared (FR-040). LIST carries every core field.
/// </summary>
public sealed class NoFluffJobsSource(
    SourceId id,
    INoFluffJobsClient client,
    IOptions<NoFluffJobsOptions> options,
    ILogger<NoFluffJobsSource> logger) : IJobSource
{
    private readonly NoFluffJobsOptions _options = options.Value;

    public SourceId Id => id;
    public SourceKind Kind => SourceKind.DirectApi;

    public Task<CollectionResult> CollectAsync(
        JobSourceSearch search,
        Func<CollectedOffer, CancellationToken, Task> onOffer,
        CancellationToken ct) =>
        PagedCollector.CollectAsync(
            sourceName: "nofluffjobs",
            maxPages: _options.MaxPages,
            fetchPage: (page, token) => client.FetchListAsync(search, page, token),
            identityOf: NoFluffJobsMapper.GetReference,
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
        var mapped = NoFluffJobsMapper.MapListItem(item, Id, _options.SiteOfferUrlTemplate);
        if (mapped.IsFailure)
        {
            logger.LogWarning("Skipping unmappable nofluffjobs posting: {Error}", mapped.Error);
            return false;
        }

        var mode = mapped.Value.Content.WorkMode;
        if (!WorkplaceFilter.Keep(mode, search.WorkplaceKeep, out var unknown))
        {
            return false;
        }

        if (unknown)
        {
            logger.LogInformation("Keeping nofluffjobs offer with unknown work mode (flagged, not dropped).");
        }

        await onOffer(mapped.Value, ct);
        return true;
    }
}
