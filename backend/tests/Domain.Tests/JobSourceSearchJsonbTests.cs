using System.Text.Json;
using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>
/// Feature 008 (T014, ADR-5): the additive LinkedIn search fields live in the existing
/// <c>search_criteria</c> jsonb column with <b>no migration</b>. This asserts they round-trip through
/// System.Text.Json (the jsonb converter's serializer, <see cref="JsonSerializerDefaults.Web"/>) AND that
/// legacy rows written before 008 — whose JSON lacks the new keys — deserialize to safe defaults
/// (<c>IncludeRecommended=false</c>, <c>LinkedInSearches=[]</c>), so every pre-008 source keeps working.
/// </summary>
public sealed class JobSourceSearchJsonbTests
{
    // Mirrors JobColumn.HasJsonbConversion — Web defaults (camelCase, case-insensitive).
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LinkedIn_search_fields_round_trip_through_jsonb()
    {
        var original = new JobSourceSearch
        {
            IncludeRecommended = true,
            LinkedInSearches =
            [
                new LinkedInSearch { Keywords = "Senior .NET Engineer", Location = "Kraków", GeoId = "90009828", Distance = 50, Recency = "r1296000" },
                new LinkedInSearch { Keywords = "Backend Developer" },
            ],
        };

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<JobSourceSearch>(json, Options)!;

        restored.IncludeRecommended.ShouldBeTrue();
        restored.LinkedInSearches.Count.ShouldBe(2);
        var first = restored.LinkedInSearches[0];
        first.Keywords.ShouldBe("Senior .NET Engineer");
        first.Location.ShouldBe("Kraków");
        first.GeoId.ShouldBe("90009828");
        first.Distance.ShouldBe(50);
        first.Recency.ShouldBe("r1296000");
        restored.LinkedInSearches[1].Keywords.ShouldBe("Backend Developer");
    }

    [Fact]
    public void Legacy_json_without_the_new_keys_deserializes_to_defaults()
    {
        // A pre-008 justjoin.it row: the LinkedIn keys are simply absent.
        const string legacy =
            """
            {
              "categories": ["7"],
              "experienceLevels": ["mid", "senior"],
              "employmentTypes": ["b2b"],
              "workingTimes": ["full_time"],
              "withSalary": true,
              "sortBy": "salary",
              "orderBy": "DESC",
              "workplaceKeep": ["remote", "hybrid"]
            }
            """;

        var restored = JsonSerializer.Deserialize<JobSourceSearch>(legacy, Options)!;

        // Pre-existing fields still bind…
        restored.Categories.ShouldBe(["7"]);
        restored.WithSalary.ShouldBeTrue();
        // …and the new LinkedIn fields fall back to safe defaults (back-compat, no migration).
        restored.IncludeRecommended.ShouldBeFalse();
        restored.LinkedInSearches.ShouldBeEmpty();
    }
}
