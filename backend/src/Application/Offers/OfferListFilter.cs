using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Application.Offers;

public enum OfferStatusFilter
{
    New,
    All,
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
}
