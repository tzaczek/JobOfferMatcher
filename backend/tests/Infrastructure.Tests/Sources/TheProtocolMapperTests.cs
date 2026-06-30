using System.Text.Json;
using System.Text.Json.Nodes;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Infrastructure.Sources.TheProtocol;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>Pure-mapping contract tests for theprotocol payload edge cases (PL localization, dates, hidden salary).</summary>
public sealed class TheProtocolMapperTests
{
    private const string Template = "https://theprotocol.it/szczegoly/praca/{offerUrlName}";
    private static readonly SourceId Source = SourceId.New();

    private static OfferContent Map(JsonObject offer) =>
        TheProtocolMapper.MapListItem(JsonSerializer.SerializeToElement(offer), Source, Template).Value.Content;

    private static JsonObject Offer(string id, JsonArray workModes, JsonArray contracts) => new()
    {
        ["id"] = id,
        ["offerUrlName"] = id + "-name",
        ["title"] = "Role",
        ["employer"] = "Co",
        ["workplace"] = new JsonArray(new JsonObject { ["city"] = "Warszawa" }),
        ["positionLevels"] = new JsonArray(new JsonObject { ["value"] = "senior" }),
        ["technologies"] = new JsonArray("C#"),
        ["workModes"] = workModes,
        ["typesOfContracts"] = contracts,
    };

    [Fact]
    public void Polish_localized_gross_monthly_labels_map_correctly()
    {
        // language=pl: workMode "stacjonarna", kind "brutto", period "miesięcznie".
        var content = Map(Offer("PL1", ["stacjonarna"],
        [
            new JsonObject
            {
                ["id"] = 0,
                ["salary"] = new JsonObject
                {
                    ["from"] = 18000,
                    ["to"] = 22000,
                    ["currencySymbol"] = "zł",
                    ["timeUnit"] = new JsonObject { ["longForm"] = "miesięcznie" },
                    ["kindName"] = "brutto",
                },
            },
        ]));

        content.WorkMode.ShouldBe(WorkMode.Office);
        var band = content.SalaryBands.ShouldHaveSingleItem();
        band.Basis.ShouldBe(EmploymentBasis.Permanent); // contract id 0
        band.Tax.ShouldBe(TaxTreatment.Gross);          // "brutto"
        band.Period.ShouldBe(SalaryPeriod.Monthly);     // "miesięcznie"
        band.Currency!.Code.ShouldBe("PLN");
    }

    [Fact]
    public void Publication_date_without_z_is_parsed_as_utc()
    {
        var content = Map(new JsonObject
        {
            ["id"] = "DT1",
            ["offerUrlName"] = "dt1-name",
            ["title"] = "Role",
            ["employer"] = "Co",
            ["workModes"] = new JsonArray("remote"),
            ["technologies"] = new JsonArray("C#"),
            ["typesOfContracts"] = new JsonArray(),
            ["publicationDateUtc"] = "2026-06-09T08:52:50.103", // no trailing Z
        });

        content.PublishedAt.ShouldBe(DateTimeOffset.Parse("2026-06-09T08:52:50.103Z").ToUniversalTime());
    }

    [Fact]
    public void Empty_contracts_yield_no_salary_bands()
    {
        var content = Map(Offer("NOSAL", ["remote"], []));

        content.SalaryBands.ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_work_mode_when_workmodes_empty()
    {
        var content = Map(Offer("UNK", [], []));

        content.WorkMode.ShouldBe(WorkMode.Unknown);
    }

    [Fact]
    public void Euro_currency_symbol_maps_to_eur()
    {
        var content = Map(Offer("EUR1", ["remote"],
        [
            new JsonObject
            {
                ["id"] = 3,
                ["salary"] = new JsonObject
                {
                    ["from"] = 100,
                    ["to"] = 120,
                    ["currencySymbol"] = "€",
                    ["timeUnit"] = new JsonObject { ["longForm"] = "hourly" },
                    ["kindName"] = "net (+ VAT)",
                },
            },
        ]));

        content.SalaryBands.ShouldHaveSingleItem().Currency!.Code.ShouldBe("EUR");
    }
}
