using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Enrichment;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

internal sealed class EnrichmentRepository(AppDbContext db) : IEnrichmentRepository
{
    public Task<OfferEnrichment?> GetEnrichmentAsync(OfferId offerId, CancellationToken ct = default) =>
        db.OfferEnrichments.FirstOrDefaultAsync(e => e.OfferId == offerId, ct);

    public Task<OfferFit?> GetFitAsync(OfferId offerId, CancellationToken ct = default) =>
        db.OfferFits.FirstOrDefaultAsync(f => f.OfferId == offerId, ct);

    public async Task AddEnrichmentAsync(OfferEnrichment enrichment, CancellationToken ct = default) =>
        await db.OfferEnrichments.AddAsync(enrichment, ct);

    public async Task AddFitAsync(OfferFit fit, CancellationToken ct = default) =>
        await db.OfferFits.AddAsync(fit, ct);

    public async Task<IReadOnlyList<OfferWorkRow>> GetOfferWorkRowsAsync(CancellationToken ct = default)
    {
        var offers = await db.Offers.AsNoTracking().ToListAsync(ct);
        var enrichments = await db.OfferEnrichments.AsNoTracking().ToListAsync(ct);
        var fits = await db.OfferFits.AsNoTracking().ToListAsync(ct);

        var enrichmentByOffer = enrichments.ToDictionary(e => e.OfferId);
        var fitByOffer = fits.ToDictionary(f => f.OfferId);

        var rows = new List<OfferWorkRow>(offers.Count);
        foreach (var offer in offers)
        {
            // Satellites are an invariant (backfill guarantees one each); skip defensively if absent.
            if (enrichmentByOffer.TryGetValue(offer.Id, out var enrichment)
                && fitByOffer.TryGetValue(offer.Id, out var fit))
            {
                rows.Add(new OfferWorkRow(offer, enrichment, fit));
            }
        }

        return rows;
    }

    public async Task<SatelliteCounts> GetCountsAsync(bool countFits, CancellationToken ct = default)
    {
        var pendingSummaries = await db.OfferEnrichments.CountAsync(e => e.State == EnrichmentState.Pending, ct);
        var failedSummaries = await db.OfferEnrichments.CountAsync(e => e.State == EnrichmentState.Failed, ct);
        var pendingFits = countFits ? await db.OfferFits.CountAsync(f => f.State == EnrichmentState.Pending, ct) : 0;
        var failedFits = countFits ? await db.OfferFits.CountAsync(f => f.State == EnrichmentState.Failed, ct) : 0;
        return new SatelliteCounts(pendingSummaries, failedSummaries, pendingFits, failedFits);
    }

    public async Task<DateTimeOffset?> GetLastResultAtAsync(CancellationToken ct = default)
    {
        var lastEnrichment = await db.OfferEnrichments.MaxAsync(e => e.ProducedAt, ct);
        var lastFit = await db.OfferFits.MaxAsync(f => f.ProducedAt, ct);
        return new[] { lastEnrichment, lastFit }.Max();
    }

    public Task InvalidateAllFitsAsync(CancellationToken ct = default) =>
        db.OfferFits.ExecuteUpdateAsync(
            s => s.SetProperty(f => f.State, EnrichmentState.Pending).SetProperty(f => f.Attempts, 0), ct);

    public async Task RearmFailedAsync(CancellationToken ct = default)
    {
        await db.OfferEnrichments.Where(e => e.State == EnrichmentState.Failed)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.State, EnrichmentState.Pending).SetProperty(e => e.Attempts, 0), ct);
        await db.OfferFits.Where(f => f.State == EnrichmentState.Failed)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.State, EnrichmentState.Pending).SetProperty(f => f.Attempts, 0), ct);
    }

    public async Task ForceAllPendingAsync(CancellationToken ct = default)
    {
        await db.OfferEnrichments
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.State, EnrichmentState.Pending).SetProperty(e => e.Attempts, 0), ct);
        await db.OfferFits
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.State, EnrichmentState.Pending).SetProperty(f => f.Attempts, 0), ct);
    }
}
