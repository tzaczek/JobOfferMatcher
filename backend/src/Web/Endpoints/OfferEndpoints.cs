using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>Offers feed + detail + user status (contracts/rest-api.md §Offers).</summary>
internal static class OfferEndpoints
{
    public sealed record SetStatusRequest(string Status);

    public static IEndpointRouteBuilder MapOfferEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/offers");

        group.MapGet("/", async (
            string? status,
            string? source,
            string? workMode,
            string? sort,
            string? availability,
            string? q,
            IOfferReadService offers,
            CancellationToken ct) =>
        {
            var filter = new OfferListFilter
            {
                Status = ParseEnum(status, OfferStatusFilter.All),
                Source = SourceId.TryParse(source, out var sourceId) ? sourceId : null,
                WorkMode = workMode,
                Sort = ParseEnum(sort, OfferSort.Rank),
                Availability = ParseEnum(availability, AvailabilityFilter.Available),
                Query = q,
            };

            var result = await offers.ListAsync(filter, ct);
            return Results.Ok(result);
        });

        group.MapGet("/{id}", async (string id, IOfferReadService offers, CancellationToken ct) =>
        {
            if (!OfferId.TryParse(id, out var offerId))
            {
                return Results.NotFound();
            }

            var detail = await offers.GetAsync(offerId, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapPost("/{id}/status", async (
            string id, SetStatusRequest body, SetUserOfferStatus setStatus, CancellationToken ct) =>
        {
            if (!OfferId.TryParse(id, out var offerId))
            {
                return Results.NotFound();
            }

            if (!Enum.TryParse<UserOfferStatus>(body.Status, ignoreCase: true, out var status))
            {
                return Results.BadRequest(new { error = new { code = "InvalidStatus", message = $"Unknown status '{body.Status}'." } });
            }

            var result = await setStatus.ExecuteAsync(offerId, status, ct);
            return result.ToHttp(() => Results.NoContent());
        });

        return api;
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
