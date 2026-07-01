using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Matching;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.RoleGroups;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// Read side for the offers feed/detail. Fit/summary are produced by the Claude worker and PERSISTED
/// in the <c>offer_enrichment</c>/<c>offer_fit</c> satellites (ADR-1) — this service no longer scores
/// (ADR-2): it loads the satellites, recomputes the current input hash per offer, and projects
/// produced/pending/failed (a Produced row whose hash ≠ current renders as <c>pending</c>; never a
/// non-AI fallback — FR-005). Default sort is two-tier: produced fits by <c>CombinedRank</c>, then the
/// rest by <c>DegradedRank</c> (salary + recency). Fit-absence is keyed on the produced CV profile,
/// not the PdfPig gauge (R4).
/// </summary>
internal sealed class OfferReadService(
    AppDbContext db,
    ICvRepository cvs,
    ISettingsRepository settingsRepo,
    Ganss.Xss.IHtmlSanitizer htmlSanitizer) : IOfferReadService
{
    public async Task<OfferListResult> ListAsync(OfferListFilter filter, CancellationToken ct = default)
    {
        var offers = await ApplyFilters(db.Offers.AsNoTracking().AsQueryable(), filter).ToListAsync(ct);
        var context = await LoadContextAsync(ct);

        var projected = offers.Select(o => Project(o, context)).ToList();

        // Relative salary/recency scores (0..100) for ranking.
        var maxMonthly = projected.Select(p => p.Normalized?.ComparableMonthly.Amount ?? 0m).DefaultIfEmpty(0m).Max();
        var ticks = projected.Select(p => p.Offer.FirstSuggestedAt.UtcTicks).ToList();
        long minTick = ticks.Count > 0 ? ticks.Min() : 0;
        long maxTick = ticks.Count > 0 ? ticks.Max() : 0;

        var ranked = projected.Select(p =>
        {
            var salaryScore = maxMonthly > 0 && p.Normalized is not null
                ? (double)(p.Normalized.ComparableMonthly.Amount / maxMonthly) * 100
                : 0;
            var recencyScore = maxTick > minTick
                ? (double)(p.Offer.FirstSuggestedAt.UtcTicks - minTick) / (maxTick - minTick) * 100
                : 100;

            var producedScore = ProducedFitScore(p.Fit);
            // Tier 0 = a current produced fit (ranked by the AI score); tier 1 = pending/absent (salary + recency).
            var tier = producedScore is not null ? 0 : 1;
            var rank = producedScore is not null
                ? Scorer.CombinedRank(producedScore.Value, salaryScore)
                : Scorer.DegradedRank(salaryScore, recencyScore);
            return new RankedOffer(p, rank, tier);
        }).ToList();

        var sorted = Sort(ranked, filter.Sort);
        var items = await CollapseGroupsAsync(sorted, offers, ct);

        var pendingEnrichment = context.Enrichments.Values.Count(e => e.State == EnrichmentState.Pending);
        var failedEnrichment = context.Enrichments.Values.Count(e => e.State == EnrichmentState.Failed);
        var hasAffinityBasis = context.AppliedCount >= OfferAffinity.MinApplications;
        // Affinity counts are gated on the basis (like fits on a produced profile) so they can reach 0.
        var pendingAffinity = hasAffinityBasis ? context.Affinities.Values.Count(a => a.State == EnrichmentState.Pending) : 0;
        var failedAffinity = hasAffinityBasis ? context.Affinities.Values.Count(a => a.State == EnrichmentState.Failed) : 0;
        var meta = new OfferListMeta(
            items.Count,
            items.Count(i => i.IsNew),
            context.HasProducedProfile,
            pendingEnrichment,
            failedEnrichment,
            pendingAffinity,
            failedAffinity,
            context.AppliedCount,
            hasAffinityBasis);
        return new OfferListResult(items, meta);
    }

    public async Task<OfferDetail?> GetAsync(OfferId id, CancellationToken ct = default)
    {
        var offer = await db.Offers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (offer is null)
        {
            return null;
        }

        var context = await LoadContextAsync(ct);
        var projected = Project(offer, context);

        var versions = await db.OfferVersions.AsNoTracking()
            .Where(v => v.OfferId == id)
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new OfferVersionView(v.CreatedAt, v.ChangeTier.ToString()))
            .ToListAsync(ct);

        var events = await db.OfferEvents.AsNoTracking()
            .Where(e => e.OfferId == id)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new OfferEventView(e.OccurredAt, e.Type.ToString()))
            .ToListAsync(ct);

        // Sanitize source-supplied HTML before it can reach the renderer (XSS defense).
        var safeDescription = offer.DescriptionHtml is null ? null : htmlSanitizer.Sanitize(offer.DescriptionHtml);

        return new OfferDetail(ToListItem(projected), safeDescription, versions, events);
    }

    private async Task<ReadContext> LoadContextAsync(CancellationToken ct)
    {
        var settings = await settingsRepo.GetAsync(ct);
        var allCvs = await cvs.GetAllAsync(ct);
        var producedCv = allCvs
            .Where(c => c.HasProducedProfile)
            .OrderByDescending(c => c.ProfileProducedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        var effectiveProfileVersion = producedCv is not null
            ? EffectiveProfile.Version(producedCv.Profile!, settings.Preferences)
            : (InputHash?)null;

        var enrichments = (await db.OfferEnrichments.AsNoTracking().ToListAsync(ct)).ToDictionary(e => e.OfferId);
        var fits = (await db.OfferFits.AsNoTracking().ToListAsync(ct)).ToDictionary(f => f.OfferId);
        var affinities = (await db.OfferAffinities.AsNoTracking().ToListAsync(ct)).ToDictionary(a => a.OfferId);

        // Affinity basis (006): ALL applied offers (not just the filtered feed), weighted equally. The
        // version is null below the ≥3 cold-start gate → the read path returns "insufficient" (FR-006).
        var appliedOffers = await db.Offers.AsNoTracking().Where(o => o.Applied).ToListAsync(ct);
        var appliedBasis = appliedOffers.Select(o => (o.Id, o.CurrentFingerprint.Hash)).ToList();
        var basisVersion = AppliedBasisInputs.Version(appliedBasis);

        var preferredBasis = settings.Preferences.PreferredEmployment.Count > 0
            ? settings.Preferences.PreferredEmployment[0]
            : (EmploymentBasis?)null;

        return new ReadContext(
            settings, preferredBasis, producedCv is not null, effectiveProfileVersion,
            enrichments, fits, affinities, appliedBasis.Count, basisVersion);
    }

    private Projected Project(Offer o, ReadContext ctx)
    {
        var normalized = OfferSalaryReducer.BestComparable(o.SalaryBands, ctx.Settings.Normalization, ctx.PreferredBasis);
        var offerEnrichHash = OfferEnrichmentInputs.Hash(o.CurrentFingerprint.Hash, o.Company, o.Location, o.DescriptionHtml);

        var enrichment = ctx.Enrichments.GetValueOrDefault(o.Id);
        var (enrichmentState, summary, keySkills) = ProjectEnrichment(enrichment, offerEnrichHash.Serialized);

        var fit = ProjectFit(o, offerEnrichHash, ctx);
        var affinity = ProjectAffinity(o, offerEnrichHash, ctx);

        return new Projected(o, normalized, enrichmentState, summary, keySkills, fit, affinity);
    }

    private static (string State, string? Summary, IReadOnlyList<string> KeySkills) ProjectEnrichment(OfferEnrichment? e, string currentHash)
    {
        if (e is { State: EnrichmentState.Produced } && e.InputsHash == currentHash)
        {
            return ("produced", e.Summary, e.KeySkills);
        }

        if (e is { State: EnrichmentState.Failed } && e.InputsHash == currentHash)
        {
            return ("failed", null, []);
        }

        return ("pending", null, []);
    }

    private FitView? ProjectFit(Offer o, InputHash offerEnrichHash, ReadContext ctx)
    {
        // Fit-absence is keyed on a current produced CV profile (not the PdfPig gauge): nothing to match against.
        if (!ctx.HasProducedProfile || ctx.EffectiveProfileVersion is null)
        {
            return null;
        }

        var currentFitHash = OfferFitInputs.Hash(offerEnrichHash, ctx.EffectiveProfileVersion, ctx.Settings.Weights).Serialized;
        var f = ctx.Fits.GetValueOrDefault(o.Id);

        if (f is { State: EnrichmentState.Produced } && f.InputsHash == currentFitHash)
        {
            return new FitView("produced", f.Score, f.Matched, f.Missing, f.Rationale);
        }

        if (f is { State: EnrichmentState.Failed } && f.InputsHash == currentFitHash)
        {
            return new FitView("failed", null, [], [], null);
        }

        return new FitView("pending", null, [], [], null);
    }

    private static int? ProducedFitScore(FitView? fit) =>
        fit is { State: "produced", Score: { } score } ? score : null;

    /// <summary>
    /// Project affinity (006) with the same recompute-from-live-inputs stale guard as fit: a
    /// <c>Produced</c> row is only shown when its stored hash matches the current one AND ≥3 applied
    /// offers exist; below that it is <c>insufficient</c> (cold start); else <c>pending</c>/<c>failed</c>.
    /// Never a fabricated fallback (FR-009). Orthogonal to fit and the CV profile (ADR-5).
    /// </summary>
    private static AffinityView ProjectAffinity(Offer o, InputHash offerEnrichHash, ReadContext ctx)
    {
        if (ctx.AppliedCount < OfferAffinity.MinApplications || ctx.BasisVersion is null)
        {
            return new AffinityView("insufficient", null, [], null);
        }

        var currentHash = OfferAffinityInputs.Hash(offerEnrichHash, ctx.BasisVersion).Serialized;
        var a = ctx.Affinities.GetValueOrDefault(o.Id);

        if (a is { State: EnrichmentState.Produced } && a.InputsHash == currentHash)
        {
            return new AffinityView("produced", a.Score, a.Resembles, a.Rationale);
        }

        if (a is { State: EnrichmentState.Failed } && a.InputsHash == currentHash)
        {
            return new AffinityView("failed", null, [], null);
        }

        return new AffinityView("pending", null, [], null);
    }

    private static int? ProducedAffinityScore(AffinityView affinity) =>
        affinity is { State: "produced", Score: { } score } ? score : null;

    private async Task<List<OfferListItem>> CollapseGroupsAsync(List<RankedOffer> sorted, List<Offer> materialized, CancellationToken ct)
    {
        var groups = (await db.RoleGroups.AsNoTracking().ToListAsync(ct)).ToDictionary(g => g.Id);
        if (groups.Count == 0)
        {
            return sorted.Select(r => ToListItem(r.Projected)).ToList();
        }

        var sourceNames = await db.JobSources.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var offerById = materialized.ToDictionary(o => o.Id);
        var consumed = new HashSet<OfferId>();
        var items = new List<OfferListItem>();

        foreach (var r in sorted)
        {
            var offer = r.Projected.Offer;
            if (consumed.Contains(offer.Id))
            {
                continue;
            }

            var item = ToListItem(r.Projected);

            if (offer.RoleGroupId is { } groupId
                && groups.TryGetValue(groupId, out var group)
                && group.UserOverride != RoleGroupOverride.NotSame
                && group.MemberOfferIds.Count > 1)
            {
                var members = group.MemberOfferIds
                    .Where(mid => mid != offer.Id && offerById.ContainsKey(mid))
                    .Select(mid => offerById[mid])
                    .Select(m => new OfferGroupMemberView(m.Id.Value, sourceNames.GetValueOrDefault(m.SourceId, "source"), m.CanonicalUrl))
                    .ToList();

                item = item with { GroupMembers = members };
                foreach (var mid in group.MemberOfferIds)
                {
                    consumed.Add(mid);
                }
            }

            items.Add(item);
        }

        return items;
    }

    private static IQueryable<Offer> ApplyFilters(IQueryable<Offer> query, OfferListFilter filter)
    {
        if (filter.Availability == AvailabilityFilter.Available)
        {
            query = query.Where(o => o.Availability == AvailabilityStatus.Available);
        }

        query = filter.Status switch
        {
            OfferStatusFilter.New => query.Where(o => o.UserStatus == UserOfferStatus.New),
            OfferStatusFilter.Active => query.Where(o => o.UserStatus != UserOfferStatus.Dismissed),
            OfferStatusFilter.Interested => query.Where(o => o.UserStatus == UserOfferStatus.Interested),
            OfferStatusFilter.Dismissed => query.Where(o => o.UserStatus == UserOfferStatus.Dismissed),
            OfferStatusFilter.Viewed => query.Where(o => o.UserStatus == UserOfferStatus.Viewed),
            _ => query,
        };

        if (filter.Applied is { } applied)
        {
            query = query.Where(o => o.Applied == applied);
        }

        if (filter.Source is { } source)
        {
            query = query.Where(o => o.ExternalRef.SourceId == source);
        }

        if (!string.IsNullOrWhiteSpace(filter.WorkMode)
            && Enum.TryParse<WorkMode>(filter.WorkMode, ignoreCase: true, out var workMode))
        {
            query = query.Where(o => o.WorkMode == workMode);
        }

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var pattern = $"%{filter.Query.Trim()}%";
            query = query.Where(o => EF.Functions.ILike(o.Title, pattern) || EF.Functions.ILike(o.Company, pattern));
        }

        return query;
    }

    private static List<RankedOffer> Sort(List<RankedOffer> ranked, OfferSort sort) => sort switch
    {
        OfferSort.Fit => [.. ranked.OrderByDescending(r => ProducedFitScore(r.Projected.Fit) ?? -1).ThenByDescending(r => r.Rank)],
        OfferSort.Salary => [.. ranked.OrderByDescending(r => r.Projected.Normalized?.ComparableMonthly.Amount ?? -1m)
            .ThenByDescending(r => r.Projected.Offer.FirstSuggestedAt)],
        OfferSort.Recency => [.. ranked.OrderByDescending(r => r.Projected.Offer.FirstSuggestedAt)],
        OfferSort.Published => [.. ranked
            .OrderByDescending(r => r.Projected.Offer.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(r => r.Projected.Offer.FirstSuggestedAt)],
        // 006: produced-affinity score desc, then the default rank (mirrors the Fit sort).
        OfferSort.Affinity => [.. ranked.OrderByDescending(r => ProducedAffinityScore(r.Projected.Affinity) ?? -1).ThenByDescending(r => r.Rank)],
        // Default two-tier: produced-fit offers (CombinedRank) first, then pending/absent (DegradedRank).
        _ => [.. ranked.OrderBy(r => r.Tier).ThenByDescending(r => r.Rank).ThenByDescending(r => r.Projected.Offer.FirstSuggestedAt)],
    };

    private static OfferListItem ToListItem(Projected p)
    {
        var o = p.Offer;
        return new OfferListItem(
            OfferId: o.Id.Value,
            RoleGroupId: o.RoleGroupId?.Value,
            Title: o.Title,
            Company: o.Company,
            Location: o.Location,
            WorkMode: o.WorkMode == WorkMode.Unknown ? null : o.WorkMode.ToString().ToLowerInvariant(),
            EmploymentType: o.EmploymentType,
            Seniority: o.Seniority,
            RequiredSkills: [.. o.RequiredSkills],
            NiceToHaveSkills: [.. o.NiceToHaveSkills],
            SalaryBands: [.. o.SalaryBands.Select(ToBandView)],
            NormalizedSalary: p.Normalized is null ? null : ToNormalizedView(p.Normalized),
            Summary: p.Summary,
            KeySkills: p.KeySkills,
            EnrichmentState: p.EnrichmentState,
            Fit: p.Fit,
            FitState: p.Fit?.State,
            Affinity: p.Affinity,
            AffinityState: p.Affinity.State,
            CanonicalUrl: o.CanonicalUrl,
            IsNew: o.UserStatus == UserOfferStatus.New,
            IsUpdated: o.HasUnseenUpdate,
            Availability: o.Availability == AvailabilityStatus.Available ? "available" : "no_longer_available",
            FirstSeenAt: o.FirstSeenAt,
            FirstSuggestedAt: o.FirstSuggestedAt,
            LastSeenAt: o.LastSeenAt,
            PublishedAt: o.PublishedAt,
            UserStatus: o.UserStatus.ToString().ToLowerInvariant(),
            Applied: o.Applied,
            AppliedAt: o.AppliedAt,
            ApplicationNote: o.ApplicationNote,
            GroupMembers: []);
    }

    private static NormalizedSalaryView ToNormalizedView(NormalizedSalary n) => new(
        ComparableMonthly: new ComparableMonthlyView(n.ComparableMonthly.Amount, n.ComparableMonthly.Currency.Code),
        Quality: n.Quality.ToString(),
        Assumptions: n.Assumptions);

    private static SalaryBandView ToBandView(SalaryBand b) => new(
        Min: b.AmountMin,
        Max: b.AmountMax,
        Currency: b.Currency?.Code,
        Period: b.Period?.ToString().ToLowerInvariant(),
        Basis: b.Basis.ToString().ToLowerInvariant(),
        Tax: b.Tax.ToString().ToLowerInvariant());

    private sealed record ReadContext(
        AppSettings Settings,
        EmploymentBasis? PreferredBasis,
        bool HasProducedProfile,
        InputHash? EffectiveProfileVersion,
        IReadOnlyDictionary<OfferId, OfferEnrichment> Enrichments,
        IReadOnlyDictionary<OfferId, OfferFit> Fits,
        IReadOnlyDictionary<OfferId, OfferAffinity> Affinities,
        int AppliedCount,
        InputHash? BasisVersion);

    private sealed record Projected(
        Offer Offer,
        NormalizedSalary? Normalized,
        string EnrichmentState,
        string? Summary,
        IReadOnlyList<string> KeySkills,
        FitView? Fit,
        AffinityView Affinity);

    private sealed record RankedOffer(Projected Projected, double Rank, int Tier);
}
