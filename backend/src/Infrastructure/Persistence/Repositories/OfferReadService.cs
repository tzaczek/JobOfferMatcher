using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Matching;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.RoleGroups;
using JobOfferMatcher.Domain.Salary;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// Read side for the offers feed/detail. Translatable filters run in SQL; salary/skills live in
/// jsonb columns so normalization, scoring (US3), and ranking finish in memory after
/// materialization (≈180 rows — fine at single-user scale). Fit/normalizedSalary are DERIVED here,
/// never stored (FR-035); with no readable CV the feed degrades to salary + recency (FR-026).
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

        var (profile, hasReadableCv) = await ProfileService.BuildEffectiveProfileAsync(cvs, settingsRepo, ct);
        var settings = await settingsRepo.GetAsync(ct);
        var preferredBasis = profile.PreferredEmployment.Count > 0 ? profile.PreferredEmployment[0] : (EmploymentBasis?)null;

        // First pass: normalize + score each offer.
        var scored = offers.Select(o =>
        {
            var normalized = OfferSalaryReducer.BestComparable(o.SalaryBands, settings.Normalization, preferredBasis);
            var fit = hasReadableCv ? Scorer.Score(ToScoringInput(o, normalized), profile, settings.Weights) : null;
            return new ScoredOffer(o, normalized, fit);
        }).ToList();

        // Relative salary/recency scores (0..100) for ranking.
        var maxMonthly = scored.Select(s => s.Normalized?.ComparableMonthly.Amount ?? 0m).DefaultIfEmpty(0m).Max();
        var ticks = scored.Select(s => s.Offer.FirstSuggestedAt.UtcTicks).ToList();
        long minTick = ticks.Count > 0 ? ticks.Min() : 0;
        long maxTick = ticks.Count > 0 ? ticks.Max() : 0;

        var ranked = scored.Select(s =>
        {
            var salaryScore = maxMonthly > 0 && s.Normalized is not null
                ? (double)(s.Normalized.ComparableMonthly.Amount / maxMonthly) * 100
                : 0;
            var recencyScore = maxTick > minTick
                ? (double)(s.Offer.FirstSuggestedAt.UtcTicks - minTick) / (maxTick - minTick) * 100
                : 100;
            var rank = hasReadableCv && s.Fit is not null
                ? Scorer.CombinedRank(s.Fit.Value, salaryScore)
                : Scorer.DegradedRank(salaryScore, recencyScore);
            return new RankedOffer(s, rank);
        }).ToList();

        var sorted = Sort(ranked, filter.Sort);
        var items = await CollapseGroupsAsync(sorted, offers, ct);

        var meta = new OfferListMeta(items.Count, items.Count(i => i.IsNew), NoReadableCv: !hasReadableCv);
        return new OfferListResult(items, meta);
    }

    /// <summary>
    /// Collapse cross-source role groups into one entry per group (FR-016), members listed. Groups
    /// the user marked "not same" stay as separate entries; ungrouped offers are individual.
    /// </summary>
    private async Task<List<OfferListItem>> CollapseGroupsAsync(List<RankedOffer> sorted, List<Offer> materialized, CancellationToken ct)
    {
        var groups = (await db.RoleGroups.AsNoTracking().ToListAsync(ct)).ToDictionary(g => g.Id);
        if (groups.Count == 0)
        {
            return sorted.Select(r => ToListItem(r.Scored.Offer, r.Scored.Normalized, r.Scored.Fit)).ToList();
        }

        var sourceNames = await db.JobSources.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var offerById = materialized.ToDictionary(o => o.Id);
        var consumed = new HashSet<OfferId>();
        var items = new List<OfferListItem>();

        foreach (var r in sorted)
        {
            var offer = r.Scored.Offer;
            if (consumed.Contains(offer.Id))
            {
                continue;
            }

            var item = ToListItem(offer, r.Scored.Normalized, r.Scored.Fit);

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

    public async Task<OfferDetail?> GetAsync(OfferId id, CancellationToken ct = default)
    {
        var offer = await db.Offers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (offer is null)
        {
            return null;
        }

        var (profile, hasReadableCv) = await ProfileService.BuildEffectiveProfileAsync(cvs, settingsRepo, ct);
        var settings = await settingsRepo.GetAsync(ct);
        var preferredBasis = profile.PreferredEmployment.Count > 0 ? profile.PreferredEmployment[0] : (EmploymentBasis?)null;
        var normalized = OfferSalaryReducer.BestComparable(offer.SalaryBands, settings.Normalization, preferredBasis);
        var fit = hasReadableCv ? Scorer.Score(ToScoringInput(offer, normalized), profile, settings.Weights) : null;

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

        // Sanitize source-supplied HTML before it can reach the renderer (XSS defense, T068).
        var safeDescription = offer.DescriptionHtml is null ? null : htmlSanitizer.Sanitize(offer.DescriptionHtml);

        return new OfferDetail(ToListItem(offer, normalized, fit), safeDescription, versions, events);
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
            OfferStatusFilter.Interested => query.Where(o => o.UserStatus == UserOfferStatus.Interested),
            OfferStatusFilter.Dismissed => query.Where(o => o.UserStatus == UserOfferStatus.Dismissed),
            OfferStatusFilter.Viewed => query.Where(o => o.UserStatus == UserOfferStatus.Viewed),
            _ => query,
        };

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
        OfferSort.Fit => [.. ranked.OrderByDescending(r => r.Scored.Fit?.Value ?? -1).ThenByDescending(r => r.Rank)],
        OfferSort.Salary => [.. ranked.OrderByDescending(r => r.Scored.Normalized?.ComparableMonthly.Amount ?? -1m)
            .ThenByDescending(r => r.Scored.Offer.FirstSuggestedAt)],
        OfferSort.Recency => [.. ranked.OrderByDescending(r => r.Scored.Offer.FirstSuggestedAt)],
        _ => [.. ranked.OrderByDescending(r => r.Rank).ThenByDescending(r => r.Scored.Offer.FirstSuggestedAt)],
    };

    private static ScoringInput ToScoringInput(Offer o, NormalizedSalary? normalized) => new(
        RequiredSkills: o.RequiredSkills,
        NiceToHaveSkills: o.NiceToHaveSkills,
        Seniority: SeniorityLevels.Parse(o.Seniority),
        WorkMode: o.WorkMode,
        EmploymentBases: [.. o.SalaryBands.Select(b => b.Basis).Distinct()],
        NormalizedMonthly: normalized?.ComparableMonthly.Amount);

    private static OfferListItem ToListItem(Offer o, NormalizedSalary? normalized, FitScore? fit) => new(
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
        NormalizedSalary: normalized is null ? null : ToNormalizedView(normalized),
        Fit: fit is null ? null : new FitView(fit.Value, fit.Breakdown.Matched, fit.Breakdown.Missing),
        CanonicalUrl: o.CanonicalUrl,
        IsNew: o.UserStatus == UserOfferStatus.New,
        IsUpdated: o.HasUnseenUpdate,
        Availability: o.Availability == AvailabilityStatus.Available ? "available" : "no_longer_available",
        FirstSeenAt: o.FirstSeenAt,
        FirstSuggestedAt: o.FirstSuggestedAt,
        LastSeenAt: o.LastSeenAt,
        UserStatus: o.UserStatus.ToString().ToLowerInvariant(),
        GroupMembers: []);

    private static NormalizedSalaryView ToNormalizedView(NormalizedSalary n) => new(
        Amount: n.ComparableMonthly.Amount,
        Currency: n.ComparableMonthly.Currency.Code,
        Quality: n.Quality.ToString(),
        Assumptions: n.Assumptions);

    private static SalaryBandView ToBandView(SalaryBand b) => new(
        Min: b.AmountMin,
        Max: b.AmountMax,
        Currency: b.Currency?.Code,
        Period: b.Period?.ToString().ToLowerInvariant(),
        Basis: b.Basis.ToString().ToLowerInvariant(),
        Tax: b.Tax.ToString().ToLowerInvariant());

    private sealed record ScoredOffer(Offer Offer, NormalizedSalary? Normalized, FitScore? Fit);

    private sealed record RankedOffer(ScoredOffer Scored, double Rank);
}
