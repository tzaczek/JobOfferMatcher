using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Infrastructure.Sources.LinkedIn;

/// <summary>
/// Thin transport over LinkedIn's logged-in jobs pages (feature 008), separated from the
/// <c>LinkedInSource</c> pass/dedup/tolerance logic so that logic is unit-tested offline against a fake
/// (Principles V/VI — the real Playwright path is verified by hand, never in the automated suite). It
/// also implements <see cref="IInteractiveBrowserSession"/> — the deferred manual-login port, now
/// activated. Two DI-swapped impls mirror <c>ITheProtocolClient</c>: <c>PlaywrightLinkedInClient</c>
/// (real, <c>Sources:LinkedIn:UseBrowser=true</c>) and <c>NotConfiguredLinkedInClient</c> (false).
/// </summary>
public interface ILinkedInClient : IInteractiveBrowserSession
{
    /// <summary>Collect one pass (the recommended feed or a keyword search), bounded + politely paced.</summary>
    Task<LinkedInListResult> FetchListAsync(LinkedInListRequest request, CancellationToken ct);

    /// <summary>The job detail body for one posting; <c>null</c> on any failure/block (tolerated upstream).</summary>
    Task<string?> FetchBodyAsync(string jobId, CancellationToken ct);
}

/// <summary>
/// One collection pass: the recommended feed (<paramref name="Recommended"/> = true, <see cref="Search"/>
/// null) or a keyword search (<paramref name="Recommended"/> = false, <see cref="Search"/> set). Bounded
/// by <see cref="MaxResults"/> (FR-013).
/// </summary>
public sealed record LinkedInListRequest(bool Recommended, LinkedInSearch? Search, int MaxResults);

/// <summary>Result of one list pass: the fetch status (reused enum) + the extracted job cards.</summary>
public sealed record LinkedInListResult(SourceFetchStatus Status, IReadOnlyList<LinkedInJobCard> Jobs);

/// <summary>
/// One job card scraped from a LinkedIn list. <see cref="JobId"/> is the numeric LinkedIn job id — the
/// offer's stable <c>native_key</c> (data-model §3). Salary is usually absent (renders "not available").
/// </summary>
public sealed record LinkedInJobCard(
    string JobId,
    string Title,
    string Company,
    string? Location,
    WorkMode WorkMode,
    string CanonicalUrl);
