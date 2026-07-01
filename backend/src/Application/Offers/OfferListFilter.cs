using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Application.Offers;

public enum OfferStatusFilter
{
    New,
    All,

    /// <summary>The default feed: every status EXCEPT dismissed (FR-031). Dismissing hides an offer here.</summary>
    Active,
    Interested,
    Dismissed,
    Viewed,
}

public enum OfferSort
{
    Rank,
    Salary,
    Fit,
    Recency,

    /// <summary>By the source-reported publish date (newest first); offers without one sort last.</summary>
    Published,

    /// <summary>By produced affinity score (highest first), then the default rank (FR-004, feature 006).</summary>
    Affinity,
}

public enum AvailabilityFilter
{
    Available,
    All,
}

/// <summary>Query options for the offers feed (contracts/rest-api.md GET /api/offers).</summary>
public sealed record OfferListFilter
{
    public OfferStatusFilter Status { get; init; } = OfferStatusFilter.All;
    public SourceId? Source { get; init; }
    public string? WorkMode { get; init; }
    public OfferSort Sort { get; init; } = OfferSort.Rank;
    public AvailabilityFilter Availability { get; init; } = AvailabilityFilter.Available;
    public string? Query { get; init; }

    /// <summary>When set, keep only offers whose applied flag matches (true → applied-to only).</summary>
    public bool? Applied { get; init; }
}
