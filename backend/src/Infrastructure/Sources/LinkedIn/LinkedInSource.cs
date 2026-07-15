using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobOfferMatcher.Infrastructure.Sources.LinkedIn;

/// <summary>
/// <see cref="IJobSource"/> adapter for LinkedIn (feature 008) — the first real
/// <see cref="SourceKind.InteractiveBrowser"/> source (ADR-1). It reads whether the current scan may
/// launch an interactive login from the scoped <see cref="ScanContext"/>, ensures a logged-in session
/// via <see cref="ILinkedInClient"/>, then collects the user's <em>Recommended</em> feed (US1) and
/// N saved keyword searches (US2) as independent passes under one <c>source_id</c> — so a job in several
/// passes upserts once (cross-pass dedup falls out of the orchestrator's per-source <c>Seen</c> set).
/// The password is never handled here — only the user's own login in the headed browser (Principle IV).
/// </summary>
public sealed class LinkedInSource(
    SourceId id,
    ILinkedInClient client,
    ScanContext scanContext,
    IOptions<LinkedInOptions> options,
    ILogger<LinkedInSource> logger) : IJobSource
{
    private readonly LinkedInOptions _options = options.Value;

    public SourceId Id => id;
    public SourceKind Kind => SourceKind.InteractiveBrowser;

    public async Task<CollectionResult> CollectAsync(
        JobSourceSearch search,
        Func<CollectedOffer, CancellationToken, Task> onOffer,
        CancellationToken ct)
    {
        // Attended (manual) scans may auto-launch the headed login and wait; unattended (scheduled) scans
        // must not — a no-session unattended scan returns Failed/LoginNotCompleted with no window (ADR-3).
        var session = await client.EnsureLoggedInAsync(id, scanContext.AllowInteractiveLogin, ct);
        if (session.IsFailure)
        {
            logger.LogWarning(
                "LinkedIn source {SourceId}: login not completed ({Error}, interactive={Interactive}) — recording LoginNotCompleted.",
                id, session.Error.Code, scanContext.AllowInteractiveLogin);
            return CollectionResult.Failed(IncompleteReason.LoginNotCompleted, 0);
        }

        return await CollectPassesAsync(search, onOffer, ct);
    }

    /// <summary>
    /// Run the collection passes on a valid session: an optional <em>Recommended</em> pass (US1) then one
    /// pass per saved keyword search (US2), all streaming into the same <paramref name="onOffer"/> — so a
    /// job in several passes upserts once via the orchestrator's per-source <c>Seen</c> set (US2 AC2). Each
    /// pass is independently tolerant: a pass that blocks/fails contributes its outcome and the others still
    /// run (US2 AC3). The overall result is the worst pass outcome with the total collected count.
    /// </summary>
    private async Task<CollectionResult> CollectPassesAsync(
        JobSourceSearch search,
        Func<CollectedOffer, CancellationToken, Task> onOffer,
        CancellationToken ct)
    {
        var requests = new List<LinkedInListRequest>();
        if (search.IncludeRecommended)
        {
            requests.Add(new LinkedInListRequest(Recommended: true, Search: null, _options.MaxResultsPerSearch));
        }

        foreach (var saved in search.LinkedInSearches)
        {
            requests.Add(new LinkedInListRequest(Recommended: false, Search: saved, _options.MaxResultsPerSearch));
        }

        var collected = 0;
        var worstOutcome = ScanOutcome.Complete;
        IncompleteReason? worstReason = null;
        // Cross-pass dedup by LinkedIn job id: a job in both the recommended feed and a search streams
        // ONCE (US2 AC2). The orchestrator also dedups by identity, but not re-streaming here avoids a
        // redundant upsert/observation per duplicate.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var request in requests)
        {
            ct.ThrowIfCancellationRequested();

            // Each pass is independently tolerant: a pass that throws/blocks contributes its outcome and
            // the remaining passes still run (US2 AC3).
            (ScanOutcome Outcome, IncompleteReason? Reason, int Count) pass;
            try
            {
                pass = await RunPassAsync(request, seen, onOffer, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var label = request.Recommended ? "recommended" : $"search '{request.Search!.Keywords}'";
                logger.LogWarning(ex, "LinkedIn {Label} pass threw — recording NetworkFailure for this pass.", label);
                pass = (ScanOutcome.Failed, IncompleteReason.NetworkFailure, 0);
            }

            collected += pass.Count;

            // Worst-pass aggregation (Complete < Partial < Failed) — one pass failing never aborts the rest.
            if (pass.Outcome > worstOutcome)
            {
                worstOutcome = pass.Outcome;
                worstReason = pass.Reason;
            }
        }

        return worstOutcome switch
        {
            ScanOutcome.Failed => CollectionResult.Failed(worstReason ?? IncompleteReason.NetworkFailure, collected),
            ScanOutcome.Partial => CollectionResult.Partial(worstReason ?? IncompleteReason.ChallengeDetected, collected),
            _ => CollectionResult.Complete(collected),
        };
    }

    /// <summary>
    /// One collection pass. Maps the client's fetch status to a scan outcome: <c>Ok</c> → stream the cards
    /// (Complete); <c>Blocked</c> → Partial/ChallengeDetected (an anti-automation/checkpoint wall);
    /// <c>Failed</c> → Failed/NetworkFailure. A <c>&lt;50%</c>/layout downgrade is applied by the
    /// orchestrator's sanity guard on the aggregate (Complete-gated), so it isn't re-derived here.
    /// </summary>
    private async Task<(ScanOutcome Outcome, IncompleteReason? Reason, int Count)> RunPassAsync(
        LinkedInListRequest request,
        HashSet<string> seen,
        Func<CollectedOffer, CancellationToken, Task> onOffer,
        CancellationToken ct)
    {
        var label = request.Recommended ? "recommended" : $"search '{request.Search!.Keywords}'";
        var result = await client.FetchListAsync(request, ct);

        switch (result.Status)
        {
            case SourceFetchStatus.Blocked:
                logger.LogWarning("LinkedIn {Label} pass blocked (checkpoint/challenge).", label);
                return (ScanOutcome.Partial, IncompleteReason.ChallengeDetected, 0);
            case SourceFetchStatus.Failed:
                logger.LogWarning("LinkedIn {Label} pass failed (transport).", label);
                return (ScanOutcome.Failed, IncompleteReason.NetworkFailure, 0);
        }

        var count = 0;
        foreach (var card in result.Jobs)
        {
            if (!seen.Add(card.JobId))
            {
                continue; // already streamed by an earlier pass (cross-pass dedup — US2 AC2)
            }

            var mapped = LinkedInMapper.MapCard(card, id);
            if (mapped.IsFailure)
            {
                logger.LogWarning("Skipping unmappable LinkedIn card: {Error}", mapped.Error);
                continue;
            }

            await onOffer(mapped.Value, ct);
            count++;
        }

        return (ScanOutcome.Complete, null, count);
    }

    /// <summary>Fetch one offer's detail body; any failure/block yields null → "not available" (feature 006).</summary>
    public Task<string?> FetchBodyAsync(CollectedOffer offer, CancellationToken ct) =>
        client.FetchBodyAsync(offer.ExternalRef.NativeKey, ct);
}
