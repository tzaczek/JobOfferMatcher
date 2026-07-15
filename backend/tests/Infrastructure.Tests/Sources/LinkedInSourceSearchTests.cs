using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Infrastructure.Sources.LinkedIn;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// Feature 008, US2 (T022): the single LinkedIn source runs a recommended pass + N saved-search passes
/// under one source id. A job appearing in both the feed and a search streams once (US2 AC2), and a pass
/// that blocks/throws leaves the source <see cref="ScanOutcome.Partial"/> while the other passes still
/// collect (US2 AC3). Fake client — never live LinkedIn (Principles V/VI).
/// </summary>
public sealed class LinkedInSourceSearchTests
{
    private static readonly SourceId Source = SourceId.From(new Guid("44444444-4444-4444-4444-444444444444"));

    private static LinkedInSource Build(ILinkedInClient client)
    {
        var context = new ScanContext();
        context.Begin(ScanRunId.New(), TriggerType.Manual);
        return new LinkedInSource(Source, client, context, Options.Create(new LinkedInOptions()), NullLogger<LinkedInSource>.Instance);
    }

    private static JobSourceSearch SearchWith(params string[] keywords) => new()
    {
        IncludeRecommended = true,
        LinkedInSearches = [.. keywords.Select(k => new LinkedInSearch { Keywords = k })],
    };

    [Fact]
    public async Task A_job_in_both_the_feed_and_a_search_streams_once()
    {
        var client = new FakeLinkedInClient();
        client.SetRecommended(SourceFetchStatus.Ok, FakeLinkedInClient.Card("A"), FakeLinkedInClient.Card("B"));
        client.SetSearch(".NET", SourceFetchStatus.Ok, FakeLinkedInClient.Card("B"), FakeLinkedInClient.Card("C")); // B is a duplicate
        var source = Build(client);

        var collected = new List<CollectedOffer>();
        var result = await source.CollectAsync(SearchWith(".NET"), (o, _) => { collected.Add(o); return Task.CompletedTask; }, CancellationToken.None);

        result.Outcome.ShouldBe(ScanOutcome.Complete);
        collected.Select(o => o.ExternalRef.NativeKey).OrderBy(k => k).ShouldBe(["A", "B", "C"]); // B once
    }

    [Fact]
    public async Task A_blocked_pass_leaves_the_source_partial_while_the_other_pass_still_collects()
    {
        var client = new FakeLinkedInClient();
        client.SetRecommended(SourceFetchStatus.Ok, FakeLinkedInClient.Card("A"), FakeLinkedInClient.Card("B"));
        client.SetSearch(".NET", SourceFetchStatus.Blocked); // this pass is walled off
        var source = Build(client);

        var collected = new List<CollectedOffer>();
        var result = await source.CollectAsync(SearchWith(".NET"), (o, _) => { collected.Add(o); return Task.CompletedTask; }, CancellationToken.None);

        result.Outcome.ShouldBe(ScanOutcome.Partial);
        result.Reason.ShouldBe(IncompleteReason.ChallengeDetected);
        collected.Select(o => o.ExternalRef.NativeKey).OrderBy(k => k).ShouldBe(["A", "B"]); // recommended still collected
    }

    [Fact]
    public async Task A_throwing_pass_is_tolerated_and_the_other_passes_still_collect()
    {
        var client = new ThrowingSearchLinkedInClient();
        var source = Build(client);

        var collected = new List<CollectedOffer>();
        var result = await source.CollectAsync(SearchWith("boom"), (o, _) => { collected.Add(o); return Task.CompletedTask; }, CancellationToken.None);

        result.Outcome.ShouldBe(ScanOutcome.Failed); // the throwing pass → NetworkFailure (worst outcome)
        collected.Select(o => o.ExternalRef.NativeKey).ShouldBe(["A"]); // recommended still streamed
    }

    /// <summary>A fake whose SEARCH passes throw, to prove per-pass exception tolerance (US2 AC3).</summary>
    private sealed class ThrowingSearchLinkedInClient : Application.Scanning.IInteractiveBrowserSession, ILinkedInClient
    {
        public Task<Domain.Common.Result<SessionReady>> EnsureLoggedInAsync(SourceId source, bool interactive, CancellationToken ct) =>
            Task.FromResult(Domain.Common.Result<SessionReady>.Success(new SessionReady(source, DateTimeOffset.UtcNow)));

        public Task<LinkedInListResult> FetchListAsync(LinkedInListRequest request, CancellationToken ct)
        {
            if (request.Recommended)
            {
                return Task.FromResult(new LinkedInListResult(SourceFetchStatus.Ok, [FakeLinkedInClient.Card("A")]));
            }

            throw new InvalidOperationException("boom");
        }

        public Task<string?> FetchBodyAsync(string jobId, CancellationToken ct) => Task.FromResult<string?>(null);
    }
}
