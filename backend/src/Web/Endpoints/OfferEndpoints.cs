using System.Globalization;
using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>Offers feed + detail + user status + applied flag (contracts/rest-api.md §Offers).</summary>
internal static class OfferEndpoints
{
    public sealed record SetStatusRequest(string Status);

    /// <summary>Body for marking an offer applied — both fields optional (<c>appliedAt</c> ISO-8601).</summary>
    public sealed record SetApplicationRequest(string? AppliedAt, string? Note);

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
            bool? applied,
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
                Applied = applied,
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

        // Mark applied / edit the applied date+note (idempotent on the flag).
        group.MapPut("/{id}/application", async (
            string id, SetApplicationRequest? body, SetOfferApplication application, CancellationToken ct) =>
        {
            if (!OfferId.TryParse(id, out var offerId))
            {
                return Results.NotFound();
            }

            // A literal `null` JSON body deserializes to null — treat as "no fields" rather than 500.
            body ??= new SetApplicationRequest(null, null);

            DateTimeOffset? appliedAt = null;
            if (!string.IsNullOrWhiteSpace(body.AppliedAt))
            {
                if (!DateTimeOffset.TryParse(
                        body.AppliedAt, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    return Results.BadRequest(new { error = new { code = "InvalidDate", message = $"Could not parse appliedAt '{body.AppliedAt}'." } });
                }

                appliedAt = parsed;
            }

            var result = await application.MarkAppliedAsync(offerId, appliedAt, body.Note, ct);
            return result.ToHttp(() => Results.NoContent());
        });

        // Clear the applied flag (un-apply).
        group.MapDelete("/{id}/application", async (
            string id, SetOfferApplication application, CancellationToken ct) =>
        {
            if (!OfferId.TryParse(id, out var offerId))
            {
                return Results.NotFound();
            }

            var result = await application.ClearAsync(offerId, ct);
            return result.ToHttp(() => Results.NoContent());
        });

        return api;
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
