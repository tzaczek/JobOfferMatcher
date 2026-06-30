using System.Text.Json;
using System.Text.Json.Nodes;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Infrastructure.Sources.NoFluffJobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>Contract test: reference-dedup of the multilocation expansion, field mapping, workplace filter, cap, escalation.</summary>
public sealed class NoFluffJobsCollectionTests
{
    private static readonly SourceId Source = SourceId.New();

    private static NoFluffJobsSource MakeSource(INoFluffJobsClient client, NoFluffJobsOptions? options = null) =>
        new(Source, client, Options.Create(options ?? new NoFluffJobsOptions()), NullLogger<NoFluffJobsSource>.Instance);

    private static async Task<(IReadOnlyList<CollectedOffer> Offers, CollectionResult Result)> CollectAsync(
        NoFluffJobsSource source, JobSourceSearch search)
    {
        var collected = new List<CollectedOffer>();
        var result = await source.CollectAsync(search, (o, _) => { collected.Add(o); return Task.CompletedTask; }, CancellationToken.None);
        return (collected, result);
    }

    [Fact]
    public async Task Dedups_multilocation_postings_by_reference()
    {
        var (offers, result) = await CollectAsync(MakeSource(new FixtureNoFluffJobsClient()), new JobSourceSearch());

        result.Outcome.ShouldBe(ScanOutcome.Complete);
        // Fixture has 5 postings but only 4 distinct references (one job is location-expanded twice).
        offers.Select(o => o.ExternalRef.NativeKey).ShouldBe(["YB43U7GA", "A6UVEW9P", "M8OVK0BK", "TT1V91RL"], ignoreOrder: true);
    }

    [Fact]
    public async Task Maps_remote_b2b_offer_core_fields()
    {
        var (offers, _) = await CollectAsync(MakeSource(new FixtureNoFluffJobsClient()), new JobSourceSearch());

        var remote = offers.Single(o => o.ExternalRef.NativeKey == "YB43U7GA").Content;
        remote.Title.ShouldBe("Fullstack Developer .NET + React");
        remote.Company.ShouldBe("Moondigo Sp. z o.o.");
        remote.WorkMode.ShouldBe(WorkMode.Remote);
        remote.Location.ShouldBe("Remote");
        remote.Seniority.ShouldBe("Senior");
        remote.RequiredSkills.ShouldBe([".NET", "C#", "React"]);
        remote.CanonicalUrl.ShouldBe("https://nofluffjobs.com/pl/job/fullstack-developer-net-react-moondigo-remote-1");
        remote.PublishedAt.ShouldNotBeNull();

        var band = remote.SalaryBands.ShouldHaveSingleItem();
        band.AmountMin.ShouldBe(22000);
        band.AmountMax.ShouldBe(27000);
        band.Currency!.Code.ShouldBe("PLN");
        band.Period.ShouldBe(SalaryPeriod.Monthly);
        band.Basis.ShouldBe(EmploymentBasis.B2B);
    }

    [Fact]
    public async Task Maps_hybrid_and_permanent_offers()
    {
        var (offers, _) = await CollectAsync(MakeSource(new FixtureNoFluffJobsClient()), new JobSourceSearch());

        var hybrid = offers.Single(o => o.ExternalRef.NativeKey == "TT1V91RL").Content;
        hybrid.WorkMode.ShouldBe(WorkMode.Hybrid); // location.hybridDesc present
        hybrid.Location.ShouldBe("Łódź");

        var permanent = offers.Single(o => o.ExternalRef.NativeKey == "M8OVK0BK").Content;
        permanent.WorkMode.ShouldBe(WorkMode.Office); // physical place, no remote/hybrid signal
        permanent.Location.ShouldBe("Warszawa");
        permanent.SalaryBands.ShouldHaveSingleItem().Basis.ShouldBe(EmploymentBasis.Permanent);
    }

    [Fact]
    public async Task Workplace_filter_keeps_remote_and_drops_office_and_hybrid()
    {
        var search = new JobSourceSearch { WorkplaceKeep = ["remote"] };

        var (offers, _) = await CollectAsync(MakeSource(new FixtureNoFluffJobsClient()), search);

        offers.Select(o => o.ExternalRef.NativeKey).ShouldBe(["YB43U7GA", "A6UVEW9P"], ignoreOrder: true);
    }

    [Fact]
    public async Task Collects_every_page_up_to_total_pages_then_stops()
    {
        var requested = new List<int>();
        var client = new FakeClient(page =>
        {
            requested.Add(page);
            // totalPages=2; each page yields a distinct new reference. Page 3 would also be "new" but must not be requested.
            return (SourceFetchStatus.Ok, Page([($"P{page}", "remote")], totalPages: 2));
        });

        var (offers, result) = await CollectAsync(MakeSource(client), new JobSourceSearch());

        result.Outcome.ShouldBe(ScanOutcome.Complete);
        offers.Select(o => o.ExternalRef.NativeKey).ShouldBe(["P1", "P2"]);
        requested.ShouldBe([1, 2]); // stops at page >= totalPages; page 3 never requested
    }

    [Fact]
    public async Task Hard_cap_truncates_to_partial_when_the_feed_claims_more_pages_than_the_cap()
    {
        var counter = 0;
        var client = new FakeClient(_ =>
        {
            var reference = $"R{counter++}";
            return (SourceFetchStatus.Ok, Page([(reference, "remote")], totalPages: int.MaxValue));
        });

        var (offers, result) = await CollectAsync(MakeSource(client, new NoFluffJobsOptions { MaxPages = 3 }), new JobSourceSearch());

        // A cap-induced truncation is INCOMPLETE → Partial, so disappearance reconciliation is skipped (no false unavailability).
        result.Outcome.ShouldBe(ScanOutcome.Partial);
        result.Reason.ShouldBe(IncompleteReason.LayoutChanged);
        offers.Count.ShouldBe(3); // capped at MaxPages × 1 new reference
    }

    [Fact]
    public async Task Blocked_response_yields_partial_challenge_detected_and_source_blocked()
    {
        var (_, result) = await CollectAsync(MakeSource(new FakeClient(_ => (SourceFetchStatus.Blocked, null))), new JobSourceSearch());

        result.Outcome.ShouldBe(ScanOutcome.Partial);
        result.Reason.ShouldBe(IncompleteReason.ChallengeDetected);
        result.SourceBlocked.ShouldBeTrue();
    }

    private static SourceListPage Page(IEnumerable<(string Reference, string Workplace)> items, int totalPages)
    {
        var elements = items
            .Select(i => JsonSerializer.SerializeToElement(new JsonObject
            {
                ["reference"] = i.Reference,
                ["url"] = i.Reference + "-slug",
                ["title"] = "t",
                ["name"] = "c",
                ["location"] = new JsonObject
                {
                    ["places"] = new JsonArray(new JsonObject { ["city"] = "Remote" }),
                    ["fullyRemote"] = i.Workplace == "remote",
                },
                ["seniority"] = new JsonArray("Senior"),
                ["salary"] = new JsonObject
                {
                    ["from"] = 1,
                    ["to"] = 2,
                    ["type"] = "b2b",
                    ["currency"] = "PLN",
                    ["disclosedAt"] = "VISIBLE",
                },
                ["tiles"] = new JsonObject { ["values"] = new JsonArray() },
            }))
            .ToList();
        return new SourceListPage(elements, totalPages);
    }

    private sealed class FakeClient(Func<int, (SourceFetchStatus, SourceListPage?)> pageFor) : INoFluffJobsClient
    {
        public Task<(SourceFetchStatus Status, SourceListPage? Page)> FetchListAsync(JobSourceSearch search, int page, CancellationToken ct) =>
            Task.FromResult(pageFor(page));
    }
}
