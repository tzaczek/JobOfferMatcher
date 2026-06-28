using System.Text.Json;
using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Infrastructure.Sources.JustJoinIt;

/// <summary>Result of one LIST fetch: success, a polite-retry-exhausted block, or a transport failure.</summary>
public enum FetchStatus
{
    Ok,
    Blocked,
    Failed,
}

/// <summary>One LIST page (contracts/justjoinit-payload.md). <see cref="NextCursor"/> feeds the <c>from</c> param.</summary>
public sealed record JustJoinItPage(IReadOnlyList<JsonElement> Items, long TotalItems, int? NextCursor);

/// <summary>
/// Thin transport over the justjoin.it endpoints. Separated from pagination/mapping so the
/// pagination + escalation logic is unit-tested offline against recorded fixtures (Principles V/VI).
/// </summary>
public interface IJustJoinItClient
{
    Task<(FetchStatus Status, JustJoinItPage? Page)> FetchListAsync(JobSourceSearch search, int from, CancellationToken ct);
    Task<JsonElement?> FetchDetailAsync(string slug, CancellationToken ct);
}
