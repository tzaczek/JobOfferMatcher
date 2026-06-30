using System.Text.Json;
using System.Text.Json.Nodes;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Infrastructure.Sources.NoFluffJobs;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>Pure-mapping contract tests for the nofluffjobs payload edge cases the fixture can't exercise.</summary>
public sealed class NoFluffJobsMapperTests
{
    private const string Template = "https://nofluffjobs.com/pl/job/{url}";
    private static readonly SourceId Source = SourceId.New();

    private static OfferContent Map(JsonObject posting) =>
        NoFluffJobsMapper.MapListItem(JsonSerializer.SerializeToElement(posting), Source, Template).Value.Content;

    [Fact]
    public void Hidden_salary_yields_no_band_never_a_zero_band()
    {
        var content = Map(new JsonObject
        {
            ["reference"] = "HID1",
            ["url"] = "hidden-role",
            ["title"] = "Role",
            ["name"] = "Co",
            ["location"] = new JsonObject { ["places"] = new JsonArray(new JsonObject { ["city"] = "Warszawa" }) },
            ["salary"] = new JsonObject { ["from"] = 1, ["to"] = 2, ["type"] = "b2b", ["currency"] = "PLN", ["disclosedAt"] = "NOT_VISIBLE" },
        });

        content.SalaryBands.ShouldBeEmpty(); // FR-010
    }

    [Fact]
    public void Multilocation_first_place_province_only_picks_the_later_city()
    {
        var content = Map(new JsonObject
        {
            ["reference"] = "PROV1",
            ["url"] = "prov-role",
            ["title"] = "Role",
            ["name"] = "Co",
            ["location"] = new JsonObject
            {
                ["places"] = new JsonArray(
                    new JsonObject { ["province"] = "masovian", ["provinceOnly"] = true }, // no city
                    new JsonObject { ["city"] = "Łódź" }),
                ["fullyRemote"] = false,
            },
        });

        content.Location.ShouldBe("Łódź"); // skips the province-only entry
    }

    [Fact]
    public void Absent_location_maps_to_unknown_work_mode()
    {
        var content = Map(new JsonObject
        {
            ["reference"] = "NOLOC",
            ["url"] = "noloc",
            ["title"] = "Role",
            ["name"] = "Co",
        });

        content.WorkMode.ShouldBe(WorkMode.Unknown);
        content.Location.ShouldBeNull();
    }

    [Fact]
    public void Dates_map_from_epoch_milliseconds_and_missing_renewed_is_null()
    {
        var content = Map(new JsonObject
        {
            ["reference"] = "DATE1",
            ["url"] = "date-role",
            ["title"] = "Role",
            ["name"] = "Co",
            ["posted"] = 1781175119812L,
            // no "renewed"
        });

        content.PublishedAt.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1781175119812L));
        content.LastPublishedAt.ShouldBeNull();
    }

    [Fact]
    public void Missing_reference_is_a_failure()
    {
        var result = NoFluffJobsMapper.MapListItem(
            JsonSerializer.SerializeToElement(new JsonObject { ["title"] = "Role" }), Source, Template);

        result.IsFailure.ShouldBeTrue();
    }
}
