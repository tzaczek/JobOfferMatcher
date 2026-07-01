using System.Text.Json;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobOfferMatcher.Infrastructure.Sources.JustJoinIt;

/// <summary>
/// <see cref="IJobSource"/> adapter for justjoin.it's public JSON API (research §1). Owns the
/// pagination footguns (advance via <c>from</c>, terminate on zero-new-guids, hard cap), the
/// client-side workplace filter, and the 403/challenge → Partial/ChallengeDetected escalation
/// (FR-040). LIST carries every core field; detail is fetched only on demand (not during scan).
/// </summary>
public sealed class JustJoinItSource(
    SourceId id,
    IJustJoinItClient client,
    IOptions<JustJoinItOptions> options,
    ILogger<JustJoinItSource> logger) : IJobSource
{
    private readonly JustJoinItOptions _options = options.Value;

    public SourceId Id => id;
    public SourceKind Kind => SourceKind.DirectApi;

    public async Task<CollectionResult> CollectAsync(
        JobSourceSearch search,
        Func<CollectedOffer, CancellationToken, Task> onOffer,
        CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var from = 0;
        var pages = 0;
        var cap = int.MaxValue; // set from totalItems after the first page
        var collected = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var (status, page) = await client.FetchListAsync(search, from, ct);
            if (status == FetchStatus.Blocked)
            {
                logger.LogWarning("justjoin.it blocked collection (403/challenge) at from={From}.", from);
                return CollectionResult.Partial(IncompleteReason.ChallengeDetected, collected);
            }

            if (status == FetchStatus.Failed || page is null)
            {
                logger.LogWarning("justjoin.it transport failure at from={From}.", from);
                return CollectionResult.Failed(IncompleteReason.NetworkFailure, collected);
            }

            if (pages == 0)
            {
                cap = (int)Math.Ceiling(page.TotalItems / (double)_options.PageSize) + 1;
            }

            var newThisPage = 0;
            foreach (var item in page.Items)
            {
                if (TryGetGuid(item) is not { } guid || !seen.Add(guid))
                {
                    continue; // missing guid, or dedup of multilocation/repeat guids
                }

                newThisPage++;
                if (await TryEmitAsync(item, search, onOffer, ct))
                {
                    collected++;
                }
            }

            // Authoritative termination: a page contributing zero new guids means we're done.
            if (newThisPage == 0 || page.NextCursor is null)
            {
                break;
            }

            from = page.NextCursor.Value; // advance via `from`, value = next.cursor

            if (++pages >= cap)
            {
                logger.LogWarning("justjoin.it pagination hit the safety cap ({Cap} pages) — truncating.", cap);
                break;
            }
        }

        return CollectionResult.Complete(collected);
    }

    /// <summary>
    /// Fetch the offer's DETAIL body (feature 006, US2) via the existing detail endpoint + mapper. The
    /// slug is the last segment of the canonical site URL (built from the slug during list mapping).
    /// Resilient: any failure/block returns null (the orchestrator tolerates a null body).
    /// </summary>
    public async Task<string?> FetchBodyAsync(CollectedOffer offer, CancellationToken ct)
    {
        var slug = ExtractSlug(offer.Content.CanonicalUrl);
        if (slug is null)
        {
            return null;
        }

        try
        {
            var detail = await client.FetchDetailAsync(slug, ct);
            if (detail is null)
            {
                return null;
            }

            return JustJoinItMapper.WithDescription(offer, detail.Value).Content.DescriptionHtml;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "justjoin.it body fetch failed for slug {Slug}.", slug);
            return null;
        }
    }

    /// <summary>The slug is the final path segment of the canonical URL (siteUrlTemplate's <c>{slug}</c> slot).</summary>
    private static string? ExtractSlug(string canonicalUrl)
    {
        if (string.IsNullOrWhiteSpace(canonicalUrl))
        {
            return null;
        }

        var trimmed = canonicalUrl.TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        return idx >= 0 && idx < trimmed.Length - 1 ? trimmed[(idx + 1)..] : null;
    }

    private async Task<bool> TryEmitAsync(
        JsonElement item,
        JobSourceSearch search,
        Func<CollectedOffer, CancellationToken, Task> onOffer,
        CancellationToken ct)
    {
        var workplaceType = item.TryGetProperty("workplaceType", out var wp) ? wp.GetString() : null;
        if (!JustJoinItMapper.MatchesWorkplaceKeep(workplaceType, search.WorkplaceKeep, out var unknown))
        {
            return false;
        }

        if (unknown && workplaceType is not null)
        {
            logger.LogInformation("Keeping offer with unknown workplaceType '{Value}' (flagged, not dropped).", workplaceType);
        }

        var mapped = JustJoinItMapper.MapListItem(item, Id, _options.SiteOfferUrlTemplate);
        if (mapped.IsFailure)
        {
            logger.LogWarning("Skipping unmappable list item: {Error}", mapped.Error);
            return false;
        }

        await onOffer(mapped.Value, ct);
        return true;
    }

    private static string? TryGetGuid(JsonElement item) =>
        item.TryGetProperty("guid", out var guidProp) && guidProp.GetString() is { Length: > 0 } guid
            ? guid
            : null;
}
