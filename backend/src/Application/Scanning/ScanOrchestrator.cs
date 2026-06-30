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

        logger.LogInformation(
            "Scan {ScanRunId} finished: {Outcome}, collected={Collected} new={New} updated={Updated} unavailable={Unavailable}.",
            run.Id, worstOutcome, tally.Collected, tally.New, tally.Updated, tally.Unavailable);

        return run.Id;
    }

    private async Task<(ScanOutcome Outcome, IncompleteReason? Reason)> CollectSourceAsync(
        JobSource source, ScanRunId scanRunId, Tally tally, SourceScanContext context, HashSet<OfferId> touched, CancellationToken ct)
    {
        try
        {
            var adapter = sourceFactory.Create(source);
            var result = await adapter.CollectAsync(
                source.Search,
                (collected, token) => UpsertAsync(collected, scanRunId, tally, context, touched, token),
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
        CollectedOffer collected, ScanRunId scanRunId, Tally tally, SourceScanContext context, HashSet<OfferId> touched, CancellationToken ct)
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
            await enrichment.AddEnrichmentAsync(OfferEnrichment.CreatePending(created.Id), ct);
            await enrichment.AddFitAsync(OfferFit.CreatePending(created.Id), ct);
            offerId = created.Id;
            tally.New++;
            touched.Add(created.Id);
        }
        else
        {
            var wasUnavailable = existing.Availability == AvailabilityStatus.NoLongerAvailable;
            var kind = OfferClassifier.Classify(existing.CurrentFingerprint, fingerprint);
            var beforeEnrichmentHash = EnrichmentHashOf(existing);

            if (kind == OfferChangeKind.Updated)
            {
                // Major-tier change → append version + event (FR-014). User status untouched (SC-002).
                existing.ApplyUpdate(collected.Content, fingerprint, now);
                await offers.AddVersionAsync(OfferVersion.Create(existing.Id, now, ChangeTier.Major, collected.Content, fingerprint), ct);
                await offers.AddEventAsync(OfferEvent.Create(existing.Id, now, OfferEventType.Updated), ct);
                tally.Updated++;
                touched.Add(existing.Id);
            }
            else
            {
                // Already seen, unchanged → bump last-seen only; never re-flag new (FR-013). ADR-3:
                // silently persist Minor-tier (description/company/location) edits for the summary hash.
                existing.RegisterSighting(now);
                existing.RefreshMinorContent(collected.Content);
            }

            if (wasUnavailable && existing.Availability == AvailabilityStatus.Available)
            {
                await offers.AddEventAsync(OfferEvent.Create(existing.Id, now, OfferEventType.Reappeared), ct);
            }

            // Eager invalidation: if the offer's enrichment inputs changed, re-arm its summary + fit
            // satellites to Pending (FR-006/FR-007). Never rolls back the offers upsert (same save).
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

    /// <summary>Re-arm an offer's summary + fit satellites to Pending (creating them if somehow absent — invariant).</summary>
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
