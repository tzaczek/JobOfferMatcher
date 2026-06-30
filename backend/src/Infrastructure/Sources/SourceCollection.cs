using System.Text.Json;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Scans;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Infrastructure.Sources;

/// <summary>Result of one LIST fetch: success, a polite-retry-exhausted block, or a transport failure.</summary>
public enum SourceFetchStatus
{
    Ok,
    Blocked,
    Failed,
}

/// <summary>
/// One LIST page shared by the page-numbered DirectApi sources (nofluffjobs, theprotocol).
/// <see cref="TotalPages"/> (read from the first page) bounds the pagination loop authoritatively;
/// the loop also stops early on a page that contributes zero new identities (research §1 footguns).
/// </summary>
public sealed record SourceListPage(IReadOnlyList<JsonElement> Items, int TotalPages);

/// <summary>
/// Client-side workplace keep-set filter, shared across sources. UNKNOWN is always kept + flagged,
/// never silently dropped (parity with <c>JustJoinItMapper.MatchesWorkplaceKeep</c> / FR-010).
/// </summary>
internal static class WorkplaceFilter
{
    public static bool Keep(WorkMode mode, IReadOnlyList<string> keep, out bool unknown)
    {
        unknown = mode == WorkMode.Unknown;
        if (keep.Count == 0 || unknown)
        {
            return true;
        }

        return keep.Any(k => string.Equals(k, mode.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The page-number pagination loop shared by the second-generation DirectApi sources. Owns the
/// cross-source footguns so each adapter stays thin (contracts/ijobsource-port.md): dedup by native
/// identity, authoritative termination on <c>TotalPages</c> OR a zero-new-identity page, an absolute
/// page cap so a misreported count can't loop forever, and 403/429 → Partial/ChallengeDetected /
/// transport failure → Failed/NetworkFailure escalation (FR-040). justjoin.it keeps its own
/// cursor-based loop (it paginates by <c>from</c>, not page numbers).
/// </summary>
internal static class PagedCollector
{
    public static async Task<CollectionResult> CollectAsync(
        string sourceName,
        int maxPages,
        Func<int, CancellationToken, Task<(SourceFetchStatus Status, SourceListPage? Page)>> fetchPage,
        Func<JsonElement, string?> identityOf,
        Func<JsonElement, CancellationToken, Task<bool>> tryEmit,
        ILogger logger,
        CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var collected = 0;
        var page = 1;
        var totalPages = 1; // refined from the first page
        var truncated = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (page > maxPages)
            {
                // The cap can only fire when the source claims MORE pages than we read (totalPages > maxPages),
                // so this is an INCOMPLETE pass. Reporting Complete here would let the orchestrator's
                // disappearance reconciliation mark the unread tail Unavailable (a false disappearance) — so
                // signal Partial and let reconciliation be skipped (parity with the reference's derived cap).
                logger.LogWarning("{Source} pagination hit the safety cap ({Cap} pages) — truncating (reported Partial).", sourceName, maxPages);
                truncated = true;
                break;
            }

            var (status, result) = await fetchPage(page, ct);
            if (status == SourceFetchStatus.Blocked)
            {
                logger.LogWarning("{Source} blocked collection (403/429/challenge) at page {Page}.", sourceName, page);
                return CollectionResult.Partial(IncompleteReason.ChallengeDetected, collected);
            }

            if (status == SourceFetchStatus.Failed || result is null)
            {
                logger.LogWarning("{Source} transport failure at page {Page}.", sourceName, page);
                return CollectionResult.Failed(IncompleteReason.NetworkFailure, collected);
            }

            if (page == 1)
            {
                totalPages = Math.Max(1, result.TotalPages);
            }

            var newThisPage = 0;
            foreach (var item in result.Items)
            {
                if (identityOf(item) is not { } key || !seen.Add(key))
                {
                    continue; // missing identity, or dedup of multilocation/repeat rows
                }

                newThisPage++;
                if (await tryEmit(item, ct))
                {
                    collected++;
                }
            }

            // Authoritative termination: empty page, a page with zero new identities, or the last page.
            if (result.Items.Count == 0 || newThisPage == 0 || page >= totalPages)
            {
                break;
            }

            page++;
        }

        return truncated
            ? CollectionResult.Partial(IncompleteReason.LayoutChanged, collected)
            : CollectionResult.Complete(collected);
    }
}
