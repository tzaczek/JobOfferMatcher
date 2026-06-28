using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Application.Offers;

/// <summary>
/// Read port for the offers feed and detail view (CQRS-lite query side, Principle I). Implemented
/// in Infrastructure with EF projections and tested against real Postgres (Principle V).
/// </summary>
public interface IOfferReadService
{
    Task<OfferListResult> ListAsync(OfferListFilter filter, CancellationToken ct = default);
    Task<OfferDetail?> GetAsync(OfferId id, CancellationToken ct = default);
}
