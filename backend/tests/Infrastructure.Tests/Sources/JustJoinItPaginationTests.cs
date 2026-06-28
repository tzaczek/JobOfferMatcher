using System.Text.Json;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Sources.JustJoinIt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>Contract test (T022): pagination termination/cap/dedup, workplace filter, escalation.</summary>
public sealed class JustJoinItPaginationTests
{
    private static readonly JobSourceSearch RemoteHybrid = new() { WorkplaceKeep = ["remote", "hybrid"] };

    private static JustJoinItSource MakeSource(IJustJoinItClient client) =>
        new(SourceId.New(), client, Options.Create(new JustJoinItOptions()), NullLogger<JustJoinItSource>.Instance);

    private static async Task<(IReadOnlyList<CollectedOffer> Offers, CollectionResult Result)> CollectAsync(
        JustJoinItSource source, JobSourceSearch search)
    {
        var collected = new List<CollectedOffer>();
        var result = await source.CollectAsync(search, (o, _) => { collected.Add(o); return Task.CompletedTask; }, CancellationToken.None);
        return (collected, result);
    }

    [Fact]
    public void Category_map_guards_seven_to_net()
    {
        new JustJoinItOptions().CategoryMap["7"].ShouldBe("net");
    }

    [Fact]
    public async Task Terminates_on_zero_new_guids_dedups_and_advances_via_from()
    {
        var source = MakeSource(new FakeClient(from => from switch
        {
            0 => Ok(Page([("A", "remote"), ("B", "remote"), ("C", "hybrid")], nextCursor: 20, total: 4)),
            20 => Ok(Page([("B", "remote"), ("C", "hybrid"), ("D", "remote")], nextCursor: 40, total: 4)),
            _ => Ok(Page([("B", "remote"), ("C", "hybrid"), ("D", "remote")], nextCursor: 60, total: 4)), // all repeats → stop
        }));

        var (offers, result) = await CollectAsync(source, RemoteHybrid);

        result.Outcome.ShouldBe(ScanOutcome.Complete);
        offers.Select(o => o.ExternalRef.NativeKey).ShouldBe(["A", "B", "C", "D"]); // deduped by guid
    }

    [Fact]
    public async Task Respects_the_hard_page_cap_and_does_not_loop()
    {
        // total=1 → cap = ceil(1/20)+1 = 2. A misbehaving feed returning new guids forever must stop.
        var counter = 0;
        var source = MakeSource(new FakeClient(_ =>
        {
            var guid = $"g{counter++}";
            return Ok(Page([(guid, "remote")], nextCursor: 999, total: 1));
        }));

        var (offers, result) = await CollectAsync(source, RemoteHybrid);

        result.Outcome.ShouldBe(ScanOutcome.Complete);
        offers.Count.ShouldBe(2); // capped at 2 pages × 1 item
    }

    [Fact]
    public async Task Workplace_filter_keeps_remote_hybrid_and_flags_unknown_but_drops_office()
    {
        var source = MakeSource(new FakeClient(from => from == 0
            ? Ok(Page([("rem", "remote"), ("hyb", "hybrid"), ("off", "office"), ("unk", "satellite")], nextCursor: null, total: 4))
            : Ok(Page([], nextCursor: null, total: 4))));

        var (offers, _) = await CollectAsync(source, RemoteHybrid);

        var keys = offers.Select(o => o.ExternalRef.NativeKey).ToList();
        keys.ShouldContain("rem");
        keys.ShouldContain("hyb");
        keys.ShouldContain("unk"); // UNKNOWN value kept + flagged, never silently dropped
        keys.ShouldNotContain("off"); // office filtered out
    }

    [Fact]
    public async Task Blocked_response_yields_partial_challenge_detected_and_source_blocked()
    {
        var source = MakeSource(new FakeClient(_ => (FetchStatus.Blocked, null)));

        var (_, result) = await CollectAsync(source, RemoteHybrid);

        result.Outcome.ShouldBe(ScanOutcome.Partial);
        result.Reason.ShouldBe(IncompleteReason.ChallengeDetected);
        result.SourceBlocked.ShouldBeTrue();
    }

    private static (FetchStatus, JustJoinItPage?) Ok(JustJoinItPage page) => (FetchStatus.Ok, page);

    private static JustJoinItPage Page(IEnumerable<(string Guid, string Workplace)> items, int? nextCursor, long total)
    {
        var elements = items
            .Select(i => JsonDocument.Parse(
                $$"""{"guid":"{{i.Guid}}","slug":"{{i.Guid}}","title":"t","companyName":"c","workplaceType":"{{i.Workplace}}","employmentTypes":[]}""")
                .RootElement.Clone())
            .ToList();
        return new JustJoinItPage(elements, total, nextCursor);
    }

    private sealed class FakeClient(Func<int, (FetchStatus, JustJoinItPage?)> pageFor) : IJustJoinItClient
    {
        public Task<(FetchStatus Status, JustJoinItPage? Page)> FetchListAsync(JobSourceSearch search, int from, CancellationToken ct) =>
            Task.FromResult(pageFor(from));

        public Task<JsonElement?> FetchDetailAsync(string slug, CancellationToken ct) =>
            Task.FromResult<JsonElement?>(null);
    }
}
