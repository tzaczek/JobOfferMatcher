using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Infrastructure.Sources.LinkedIn;

/// <summary>
/// The <c>Sources:LinkedIn:UseBrowser=false</c> fallback (feature 008, ADR-2/R6): used offline, in CI,
/// and when the user hasn't provisioned Chromium. It never launches a browser — login always reports
/// "required" and list/body reads are empty — so the app resolves and runs without a headed window.
/// </summary>
public sealed class NotConfiguredLinkedInClient : ILinkedInClient
{
    public static readonly Error LoginRequired = new(
        "LinkedInLoginRequired",
        "LinkedIn browser collection is disabled (Sources:LinkedIn:UseBrowser=false). Enable it and run a manual scan to sign in.");

    public Task<Result<SessionReady>> EnsureLoggedInAsync(SourceId source, bool interactive, CancellationToken ct) =>
        Task.FromResult(Result<SessionReady>.Failure(LoginRequired));

    public Task<LinkedInListResult> FetchListAsync(LinkedInListRequest request, CancellationToken ct) =>
        Task.FromResult(new LinkedInListResult(SourceFetchStatus.Ok, []));

    public Task<string?> FetchBodyAsync(string jobId, CancellationToken ct) => Task.FromResult<string?>(null);
}
