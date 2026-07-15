using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Infrastructure.Sources.LinkedIn;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// Deterministic offline <see cref="ILinkedInClient"/> for the LinkedIn adapter tests (feature 008) —
/// never touches live LinkedIn (Principle V/VI; the real Playwright path is verified by hand). Configure
/// the recommended-pass and per-keyword-search results, per-job bodies, and the login outcome (which can
/// depend on the <c>interactive</c> flag to model the attended-vs-unattended gate).
/// </summary>
public sealed class FakeLinkedInClient : ILinkedInClient
{
    private LinkedInListResult _recommended = new(SourceFetchStatus.Ok, []);
    private readonly Dictionary<string, LinkedInListResult> _searches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _bodies = new(StringComparer.Ordinal);

    /// <summary>Maps the <c>interactive</c> flag to whether login succeeds (default: always succeeds).</summary>
    public Func<bool, bool> LoginResult { get; set; } = _ => true;

    /// <summary>The <c>interactive</c> value the most recent login call was made with.</summary>
    public bool? LastInteractive { get; private set; }

    /// <summary>How many times <see cref="EnsureLoggedInAsync"/> was called.</summary>
    public int LoginCalls { get; private set; }

    public void SetRecommended(SourceFetchStatus status, params LinkedInJobCard[] jobs) =>
        _recommended = new LinkedInListResult(status, jobs);

    public void SetSearch(string keywords, SourceFetchStatus status, params LinkedInJobCard[] jobs) =>
        _searches[keywords] = new LinkedInListResult(status, jobs);

    public void SetBody(string jobId, string? body) => _bodies[jobId] = body;

    public Task<Result<SessionReady>> EnsureLoggedInAsync(SourceId source, bool interactive, CancellationToken ct)
    {
        LoginCalls++;
        LastInteractive = interactive;
        return Task.FromResult(LoginResult(interactive)
            ? Result<SessionReady>.Success(new SessionReady(source, DateTimeOffset.UtcNow))
            : Result<SessionReady>.Failure(new Error("LinkedInLoginRequired", "login required")));
    }

    public Task<LinkedInListResult> FetchListAsync(LinkedInListRequest request, CancellationToken ct)
    {
        if (request.Recommended)
        {
            return Task.FromResult(_recommended);
        }

        var keywords = request.Search!.Keywords;
        return Task.FromResult(_searches.TryGetValue(keywords, out var result)
            ? result
            : new LinkedInListResult(SourceFetchStatus.Ok, []));
    }

    public Task<string?> FetchBodyAsync(string jobId, CancellationToken ct) =>
        Task.FromResult(_bodies.TryGetValue(jobId, out var body) ? body : null);

    /// <summary>Build a job card with sensible defaults (title/company derived from the id).</summary>
    public static LinkedInJobCard Card(
        string jobId,
        string? title = null,
        string? company = null,
        string? location = null,
        WorkMode mode = WorkMode.Unknown) =>
        new(jobId, title ?? $"Role {jobId}", company ?? $"Co {jobId}", location, mode,
            $"https://www.linkedin.com/jobs/view/{jobId}/");
}
