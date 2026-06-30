using System.Text.Json;
using System.Text.Json.Nodes;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Infrastructure.Sources.TheProtocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// Contract test: __NEXT_DATA__ extraction, id-dedup pagination, multi-contract salary, the PL/EN
/// localization mapping (work mode / period / gross-net), workplace filter, cap, escalation.
/// </summary>
public sealed class TheProtocolCollectionTests
{
    private static readonly SourceId Source = SourceId.New();

    private static TheProtocolSource MakeSource(ITheProtocolClient client, TheProtocolOptions? options = null) =>
        new(Source, client, Options.Create(options ?? new TheProtocolOptions()), NullLogger<TheProtocolSource>.Instance);

    private static async Task<(IReadOnlyList<CollectedOffer> Offers, CollectionResult Result)> CollectAsync(
        TheProtocolSource source, JobSourceSearch search)
    {
        var collected = new List<CollectedOffer>();
        var result = await source.CollectAsync(search, (o, _) => { collected.Add(o); return Task.CompletedTask; }, CancellationToken.None);
        return (collected, result);
    }

    [Fact]
    public void NextDataExtractor_reads_offers_response_and_rejects_junk()
    {
        const string html = """
            <html><head><script id="__NEXT_DATA__" type="application/json">
            {"props":{"pageProps":{"offersResponse":{"page":{"number":1,"size":50,"count":2},"offers":[{"id":"a"},{"id":"b"}]}}}}
            </script></head><body></body></html>
            """;

        var response = NextDataExtractor.ExtractOffersResponse(html);

        response.ShouldNotBeNull();
        response!.Value.GetProperty("offers").GetArrayLength().ShouldBe(2);
        response.Value.GetProperty("page").GetProperty("count").GetInt32().ShouldBe(2);

        NextDataExtractor.ExtractOffersResponse("<html>no next data here</html>").ShouldBeNull();
        NextDataExtractor.ExtractOffersResponse("").ShouldBeNull();
    }

    [Fact]
    public async Task Dedups_offers_by_id()
    {
        var (offers, result) = await CollectAsync(MakeSource(new FixtureTheProtocolClient()), new JobSourceSearch());

        result.Outcome.ShouldBe(ScanOutcome.Complete);
        offers.Count.ShouldBe(4);
        offers.Select(o => o.ExternalRef.NativeKey).Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task Maps_english_offer_with_two_contract_salary_bands()
    {
        var (offers, _) = await CollectAsync(MakeSource(new FixtureTheProtocolClient()), new JobSourceSearch());

        var offer = offers.Single(o => o.ExternalRef.NativeKey == "79620000-9298-b25a-a9cc-08dec6047dbe").Content;
        offer.Title.ShouldBe("Senior Dynamics 365 for Sales Technical Consultant");
        offer.Company.ShouldBe("ITDS Polska Sp. z o.o.");
        offer.WorkMode.ShouldBe(WorkMode.Hybrid);
        offer.Location.ShouldBe("Kraków");
        offer.Seniority.ShouldBe("senior");
        offer.RequiredSkills.ShouldContain("C#");
        offer.CanonicalUrl.ShouldBe("https://theprotocol.it/szczegoly/praca/senior-dynamics-365-for-sales-technical-consultant-krakow,oferta,79620000-9298-b25a-a9cc-08dec6047dbe");
        offer.PublishedAt.ShouldNotBeNull();

        offer.SalaryBands.Count.ShouldBe(2);
        var permanent = offer.SalaryBands.Single(b => b.Basis == EmploymentBasis.Permanent); // contract id 0
        permanent.Tax.ShouldBe(TaxTreatment.Gross);
        permanent.Period.ShouldBe(SalaryPeriod.Monthly);
        permanent.AmountMin.ShouldBe(21700);
        permanent.Currency!.Code.ShouldBe("PLN");

        var b2b = offer.SalaryBands.Single(b => b.Basis == EmploymentBasis.B2B); // contract id 3
        b2b.Tax.ShouldBe(TaxTreatment.Net); // "net (+ VAT)"
        b2b.AmountMax.ShouldBe(36330);
    }

    [Fact]
    public async Task Maps_polish_localized_offer_labels()
    {
        var (offers, _) = await CollectAsync(MakeSource(new FixtureTheProtocolClient()), new JobSourceSearch());

        // Polish offer: workMode "zdalna", period "godzinowo", kind "netto (+ VAT)".
        var offer = offers.Single(o => o.ExternalRef.NativeKey == "32c90000-7e6c-d690-5d57-08dec0a9fa42").Content;
        offer.WorkMode.ShouldBe(WorkMode.Remote);
        var band = offer.SalaryBands.ShouldHaveSingleItem();
        band.Period.ShouldBe(SalaryPeriod.Hourly);
        band.Tax.ShouldBe(TaxTreatment.Net);
        band.Basis.ShouldBe(EmploymentBasis.B2B);
        band.AmountMin.ShouldBe(150);
        band.Currency!.Code.ShouldBe("PLN"); // "zł" → PLN
    }

    [Fact]
    public async Task Workplace_filter_keeps_remote_only()
    {
        var search = new JobSourceSearch { WorkplaceKeep = ["remote"] };

        var (offers, _) = await CollectAsync(MakeSource(new FixtureTheProtocolClient()), search);

        // The PL "zdalna" and the EN "remote" offers map to Remote; hybrid + full-office are dropped.
        offers.Select(o => o.Content.WorkMode).ShouldAllBe(m => m == WorkMode.Remote);
        offers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Collects_every_page_up_to_page_count_then_stops()
    {
        var requested = new List<int>();
        var client = new FakeClient(page =>
        {
            requested.Add(page);
            return (SourceFetchStatus.Ok, Page([$"id-{page}"], totalPages: 2));
        });

        var (offers, result) = await CollectAsync(MakeSource(client), new JobSourceSearch());

        result.Outcome.ShouldBe(ScanOutcome.Complete);
        offers.Select(o => o.ExternalRef.NativeKey).ShouldBe(["id-1", "id-2"]);
        requested.ShouldBe([1, 2]); // page 3 never requested
    }

    [Fact]
    public async Task Hard_cap_truncates_to_partial_when_the_feed_claims_more_pages_than_the_cap()
    {
        var counter = 0;
        var client = new FakeClient(_ =>
        {
            var id = $"id-{counter++}";
            return (SourceFetchStatus.Ok, Page([id], totalPages: int.MaxValue));
        });

        var (offers, result) = await CollectAsync(MakeSource(client, new TheProtocolOptions { MaxPages = 4 }), new JobSourceSearch());

        // A cap-induced truncation is INCOMPLETE → Partial, so disappearance reconciliation is skipped.
        result.Outcome.ShouldBe(ScanOutcome.Partial);
        result.Reason.ShouldBe(IncompleteReason.LayoutChanged);
        offers.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Blocked_response_yields_partial_challenge_detected_and_source_blocked()
    {
        var (_, result) = await CollectAsync(MakeSource(new FakeClient(_ => (SourceFetchStatus.Blocked, null))), new JobSourceSearch());

        result.Outcome.ShouldBe(ScanOutcome.Partial);
        result.Reason.ShouldBe(IncompleteReason.ChallengeDetected);
        result.SourceBlocked.ShouldBeTrue();
    }

    private static SourceListPage Page(IEnumerable<string> ids, int totalPages)
    {
        var elements = ids
            .Select(id => JsonSerializer.SerializeToElement(new JsonObject
            {
                ["id"] = id,
                ["offerUrlName"] = id + "-name",
                ["title"] = "t",
                ["employer"] = "e",
                ["workplace"] = new JsonArray(new JsonObject { ["city"] = "Warszawa" }),
                ["positionLevels"] = new JsonArray(new JsonObject { ["value"] = "senior" }),
                ["workModes"] = new JsonArray("remote"),
                ["technologies"] = new JsonArray("C#"),
                ["typesOfContracts"] = new JsonArray(new JsonObject
                {
                    ["id"] = 3,
                    ["salary"] = new JsonObject
                    {
                        ["from"] = 1,
                        ["to"] = 2,
                        ["currencySymbol"] = "zł",
                        ["timeUnit"] = new JsonObject { ["longForm"] = "monthly" },
                        ["kindName"] = "net (+ VAT)",
                    },
                }),
            }))
            .ToList();
        return new SourceListPage(elements, totalPages);
    }

    private sealed class FakeClient(Func<int, (SourceFetchStatus, SourceListPage?)> pageFor) : ITheProtocolClient
    {
        public Task<(SourceFetchStatus Status, SourceListPage? Page)> FetchListAsync(JobSourceSearch search, int page, CancellationToken ct) =>
            Task.FromResult(pageFor(page));
    }
}
