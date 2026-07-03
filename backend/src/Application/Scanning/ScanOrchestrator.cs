using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Application.Sources;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// Owns the cross-cutting scan rules so adapters stay thin (contracts/ijobsource-port.md):
/// explicit single-flight; new/updated/unchanged by identity existence + fingerprint diff (never
/// source dates); append-only observation/version/event history; and Complete-gated disappearance
/// reconciliation with a &lt;50% sanity guard (FR-012..015, research §6).
/// </summary>
public sealed class ScanOrchestrator(
    IJobSourceRepository sources,
    IOfferRepository offers,
    IScanRunRepository scanRuns,
    IJobSourceFactory sourceFactory,
    RoleGroupingService roleGrouping,
    IEnrichmentRepository enrichment,
    IUnitOfWork unitOfWork,
    ScanConcurrencyGuard concurrency,
    MaintenanceGate maintenance,
    TimeProvider time,
    ILogger<ScanOrchestrator> logger) : IScanRunner
{
    /// <summary>Downgrade a "Complete" run to Partial if it returns &lt; this fraction of the previous complete count.</summary>
    private const double SanityGuardFraction = 0.5;

    public static readonly Error ScanInProgress =
        new("ScanInProgress", "A scan is already running. Try again once it completes.");

    public async Task<Result<ScanRunId>> RunAsync(ScanRequest request, CancellationToken ct = default)
    {
        // A restore pauses scanning (FR-020): refuse cleanly rather than write during the wipe/reload.
        if (maintenance.IsMaintenanceActive)
        {
            return ScanInProgress;
        }

        if (!await concurrency.TryEnterAsync())
        {
            return ScanInProgress;
        }

        try
        {
            return await RunCoreAsync(request, ct);
        }
        finally
        {
            concurrency.Release();
        }
    }

    private async Task<Result<ScanRunId>> RunCoreAsync(ScanRequest request, CancellationToken ct)
    {
        var targets = await ResolveSourcesAsync(request, ct);
        var run = ScanRun.Start(
            ScanRunId.New(),
            time.GetUtcNow(),
            request.Trigger,
            [.. targets.Select(s => s.Id)],
            request.WindowUtc);

        await scanRuns.AddAsync(run, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var tally = new Tally();
        var worstOutcome = ScanOutcome.Complete;
        IncompleteReason? reason = null;
        var touched = new HashSet<OfferId>();

        try
        {
            foreach (var source in targets)
            {
                var context = new SourceScanContext();
                var (outcome, sourceReason) = await CollectSourceAsync(source, run.Id, tally, context, touched, ct);

                if (outcome == ScanOutcome.Complete)
                {
                    (outcome, sourceReason) = await ReconcileAsync(source, tally, context, ct);
                }

                if (outcome > worstOutcome)
                {
                    worstOutcome = outcome;
                    reason = sourceReason;
                }
            }

            // Cross-source grouping (FR-016) — best-effort, never fails the scan.
            await roleGrouping.AttachAsync(touched, ct);

            run.Finish(
                time.GetUtcNow(),
                new ScanCounts(tally.Collected, tally.New, tally.Updated, tally.Unavailable, tally.Failed),
                worstOutcome,
                reason);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // The caller disconnected (e.g. a client-side request timeout aborts HttpContext.RequestAborted,
            // which is this same ct), the process is shutting down, or something unexpected broke mid-scan.
            // Persist a terminal record with whatever tally was collected so far — an un-finished ScanRun
            // (finished_at stays null forever) would otherwise orphan the row and make the feed's "resume
            // in-flight scan" logic (007) poll it indefinitely. Always use a fresh token: ct itself may
            // already be the one that just cancelled.
            await FinishInterruptedAsync(run, tally, ex);
            throw;
        }

        logger.LogInformation(
            "Scan {ScanRunId} finished: {Outcome}, collected={Collected} new={New} updated={Updated} unavailable={Unavailable}.",
            run.Id, worstOutcome, tally.Collected, tally.New, tally.Updated, tally.Unavailable);

        return run.Id;
    }

    /// <summary>
    /// Best-effort terminal write for a scan interrupted by cancellation or an unhandled exception (an
    /// orphaned ScanRun with finished_at null forever would make the feed's resume-in-flight logic (007)
    /// poll it indefinitely). Always uses a fresh CancellationToken — the original may already be the one
    /// that cancelled — and never throws itself, so it can't mask the original exception.
    /// </summary>
    private async Task FinishInterruptedAsync(ScanRun run, Tally tally, Exception cause)
    {
        try
        {
            run.Finish(
                time.GetUtcNow(),
                new ScanCounts(tally.Collected, tally.New, tally.Updated, tally.Unavailable, tally.Failed),
                ScanOutcome.Failed,
                IncompleteReason.NetworkFailure);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            if (cause is OperationCanceledException)
            {
                logger.LogWarning(
                    "Scan {ScanRunId} was cancelled (caller disconnected or shutdown); marked Failed with partial counts.",
                    run.Id);
            }
            else
            {
                logger.LogError(cause, "Scan {ScanRunId} was interrupted by an unhandled exception; marked Failed with partial counts.", run.Id);
            }
        }
        catch (Exception persistEx)
        {
            logger.LogError(persistEx, "Failed to persist terminal state for interrupted scan {ScanRunId}.", run.Id);
        }
    }

    private async Task<(ScanOutcome Outcome, IncompleteReason? Reason)> CollectSourceAsync(
        JobSource source, ScanRunId scanRunId, Tally tally, SourceScanContext context, HashSet<OfferId> touched, CancellationToken ct)
    {
        try
        {
            var adapter = sourceFactory.Create(source);
            var result = await adapter.CollectAsync(
                source.Search,
                (collected, token) => UpsertAsync(collected, adapter, scanRunId, tally, context, touched, token),
                ct);

            return (result.Outcome, result.Reason);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Source {SourceId} ({Name}) failed during collection.", source.Id, source.Name);
            return (ScanOutcome.Failed, IncompleteReason.NetworkFailure);
        }
    }

    private async Task UpsertAsync(
        CollectedOffer collected, IJobSource adapter, ScanRunId scanRunId, Tally tally, SourceScanContext context, HashSet<OfferId> touched, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var fingerprint = ContentFingerprint.Compute(collected.Content);
        var existing = await offers.GetByExternalRefAsync(
            collected.ExternalRef.SourceId, collected.ExternalRef.NativeKey, ct);

        OfferId offerId;
        if (existing is null)
        {
            // Identity not seen before → new to us (FR-012), regardless of source dates.
            var created = Offer.Create(OfferId.New(), collected.ExternalRef, collected.Content, fingerprint, now);
            await offers.AddAsync(created, ct);
            await offers.AddVersionAsync(OfferVersion.Create(created.Id, now, ChangeTier.Major, collected.Content, fingerprint), ct);
            await offers.AddEventAsync(OfferEvent.Create(created.Id, now, OfferEventType.FirstSeen), ct);
            await offers.AddEventAsync(OfferEvent.Create(created.Id, now, OfferEventType.Surfaced), ct);
            // ADR-3: every offer carries Pending derived-cache satellites (the invariant; FR-014 backfill mirrors this).
            // 006: affinity is a third invariant satellite (an OfferFit twin), Pending at creation.
            await enrichment.AddEnrichmentAsync(OfferEnrichment.CreatePending(created.Id), ct);
            await enrichment.AddFitAsync(OfferFit.CreatePending(created.Id), ct);
            await enrichment.AddAffinityAsync(OfferAffinity.CreatePending(created.Id), ct);

            // 006 US2: capture the body for a new offer (Minor-tier; null on failure → "not available").
            var newBody = await TryFetchBodyAsync(adapter, collected, ct);
            if (newBody is not null)
            {
                created.SetDescription(newBody);
            }

            offerId = created.Id;
            tally.New++;
            touched.Add(created.Id);
        }
        else
        {
            var wasUnavailable = existing.Availability == AvailabilityStatus.NoLongerAvailable;
            var kind = OfferClassifier.Classify(existing.CurrentFingerprint, fingerprint);
            var storedBody = existing.DescriptionHtml; // capture before Refresh/ApplyUpdate reset it to the list-item's null
            var beforeEnrichmentHash = EnrichmentHashOf(existing);

            if (kind == OfferChangeKind.Updated)
            {
                // Major-tier change → append version + event (FR-014). User status untouched (SC-002).
                existing.ApplyUpdate(collected.Content, fingerprint, now);
                await offers.AddVersionAsync(OfferVersion.Create(existing.Id, now, ChangeTier.Major, collected.Content, fingerprint), ct);
                await offers.AddEventAsync(OfferEvent.Create(existing.Id, now, OfferEventType.Updated), ct);
                tally.Updated++;
                touched.Add(existing.Id);

                // 006: if an APPLIED offer's fingerprint changed, the affinity basis version changed →
                // all affinity pending (data-model §2/§4). A candidate's own re-arm is handled below;
                // this covers the OTHER offers whose stored affinity hash is now stale.
                if (existing.Applied)
                {
                    await enrichment.InvalidateAllAffinityAsync(ct);
                }
            }
            else
            {
                // Already seen, unchanged → bump last-seen only; never re-flag new (FR-013). ADR-3:
                // silently persist Minor-tier (company/location) edits for the summary hash.
                existing.RegisterSighting(now);
                existing.RefreshMinorContent(collected.Content);
            }

            // 006 US2: fetch the body only for new/updated/body-missing offers (respecting 001 ADR-2 — not
            // every offer every scan). Both ApplyUpdate and RefreshMinorContent just reset DescriptionHtml
            // to the list-item's null, so re-apply the stored body when we don't (re-)fetch a fresh one.
            var finalBody = kind == OfferChangeKind.Updated || storedBody is null
                ? await TryFetchBodyAsync(adapter, collected, ct)
                : storedBody;
            existing.SetDescription(finalBody);

            if (wasUnavailable && existing.Availability == AvailabilityStatus.Available)
            {
                await offers.AddEventAsync(OfferEvent.Create(existing.Id, now, OfferEventType.Reappeared), ct);
            }

            // Eager invalidation: if the offer's enrichment inputs changed (incl. a newly-captured body),
            // re-arm its summary + fit + affinity satellites to Pending (FR-006/FR-007/FR-016). Never rolls
            // back the offers upsert (same save).
            if (EnrichmentHashOf(existing) != beforeEnrichmentHash)
            {
                await InvalidateSatellitesAsync(existing.Id, ct);
            }

            offerId = existing.Id;
        }

        await offers.AddObservationAsync(OfferObservation.Create(offerId, scanRunId, now, fingerprint), ct);
        context.Seen.Add(offerId);
        context.Collected++;
        tally.Collected++;
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Disappearance reconciliation, only on a Complete collection (FR-015). The &lt;50% sanity guard
    /// downgrades a suspiciously-thin run to Partial (no reconciliation) so an anti-bot/layout break
    /// can't mass-kill live offers.
    /// </summary>
    private async Task<(ScanOutcome Outcome, IncompleteReason? Reason)> ReconcileAsync(
        JobSource source, Tally tally, SourceScanContext context, CancellationToken ct)
    {
        var previous = await scanRuns.GetLastCompleteForSourceAsync(source.Id, ct);
        var previousCount = previous?.Counts.Collected ?? 0;
        if (previousCount > 0 && context.Collected < previousCount * SanityGuardFraction)
        {
            logger.LogWarning(
                "Sanity guard: source {SourceId} returned {Now} (< 50% of previous {Prev}) — downgrading to Partial.",
                source.Id, context.Collected, previousCount);
            return (ScanOutcome.Partial, IncompleteReason.LayoutChanged);
        }

        var now = time.GetUtcNow();
        var active = await offers.GetActiveBySourceAsync(source.Id, ct);
        foreach (var offer in active.Where(o => !context.Seen.Contains(o.Id)))
        {
            offer.MarkUnavailable(now);
            await offers.AddEventAsync(OfferEvent.Create(offer.Id, now, OfferEventType.BecameUnavailable), ct);
            tally.Unavailable++;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return (ScanOutcome.Complete, null);
    }

    private static string EnrichmentHashOf(Offer o) =>
        OfferEnrichmentInputs.Hash(o.CurrentFingerprint.Hash, o.Company, o.Location, o.DescriptionHtml).Serialized;

    /// <summary>
    /// Fetch an offer's body, tolerating any failure/block (feature 006, US2): a null/thrown result never
    /// fails the scan — the offer still collects and its body simply shows "not available".
    /// </summary>
    private async Task<string?> TryFetchBodyAsync(IJobSource adapter, CollectedOffer collected, CancellationToken ct)
    {
        try
        {
            return await adapter.FetchBodyAsync(collected, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Body fetch failed for {NativeKey}; collecting the offer without a body.", collected.ExternalRef.NativeKey);
            return null;
        }
    }

    /// <summary>
    /// Re-arm an offer's summary + fit + affinity satellites to Pending (creating them if somehow absent
    /// — invariant). A candidate content change (incl. a newly-captured body) re-flips its own affinity;
    /// the affinity BASIS version is unaffected, so no OTHER offer's affinity is touched here (006).
    /// </summary>
    private async Task InvalidateSatellitesAsync(OfferId offerId, CancellationToken ct)
    {
        var enrichmentRow = await enrichment.GetEnrichmentAsync(offerId, ct);
        if (enrichmentRow is null)
        {
            await enrichment.AddEnrichmentAsync(OfferEnrichment.CreatePending(offerId), ct);
        }
        else
        {
            enrichmentRow.Invalidate();
        }

        var fitRow = await enrichment.GetFitAsync(offerId, ct);
        if (fitRow is null)
        {
            await enrichment.AddFitAsync(OfferFit.CreatePending(offerId), ct);
        }
        else
        {
            fitRow.Invalidate();
        }

        var affinityRow = await enrichment.GetAffinityAsync(offerId, ct);
        if (affinityRow is null)
        {
            await enrichment.AddAffinityAsync(OfferAffinity.CreatePending(offerId), ct);
        }
        else
        {
            affinityRow.Invalidate();
        }
    }

    private async Task<IReadOnlyList<JobSource>> ResolveSourcesAsync(ScanRequest request, CancellationToken ct)
    {
        if (request.SourceIds is null)
        {
            return await sources.GetEnabledAsync(ct);
        }

        var resolved = new List<JobSource>();
        foreach (var id in request.SourceIds)
        {
            if (await sources.GetByIdAsync(id, ct) is { } source)
            {
                resolved.Add(source);
            }
        }

        return resolved;
    }

    private sealed class SourceScanContext
    {
        public HashSet<OfferId> Seen { get; } = [];
        public int Collected { get; set; }
    }

    private sealed class Tally
    {
        public int Collected { get; set; }
        public int New { get; set; }
        public int Updated { get; set; }
        public int Unavailable { get; set; }
        public int Failed { get; set; }
    }
}
