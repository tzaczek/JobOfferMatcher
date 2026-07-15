using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// Feature 008 (T015): the attended-ness gate + the LinkedIn saved-search invariant. Only a
/// <see cref="TriggerType.Manual"/> scan may auto-launch the interactive login window; scheduled /
/// catch-up / initial scans must not (ADR-3). A saved search requires non-blank keywords.
/// </summary>
public sealed class ScanContextAndLinkedInSearchTests
{
    [Fact]
    public void Only_a_manual_trigger_allows_interactive_login()
    {
        AllowFor(TriggerType.Manual).ShouldBeTrue();
        AllowFor(TriggerType.Scheduled).ShouldBeFalse();
        AllowFor(TriggerType.CatchUp).ShouldBeFalse();
        AllowFor(TriggerType.Initial).ShouldBeFalse();
    }

    [Fact]
    public void Begin_records_the_run_id_and_trigger()
    {
        var runId = ScanRunId.New();
        var context = new ScanContext();

        context.Begin(runId, TriggerType.Manual);

        context.RunId.ShouldBe(runId);
        context.Trigger.ShouldBe(TriggerType.Manual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_saved_search_requires_non_blank_keywords(string? keywords)
    {
        var result = LinkedInSearch.Create(keywords);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("InvalidLinkedInSearch");
    }

    [Fact]
    public void A_saved_search_trims_and_keeps_the_optional_fields()
    {
        var result = LinkedInSearch.Create("  Senior .NET  ", location: " Kraków ", geoId: " 90009828 ", distance: 50, recency: " r1296000 ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Keywords.ShouldBe("Senior .NET");
        result.Value.Location.ShouldBe("Kraków");
        result.Value.GeoId.ShouldBe("90009828");
        result.Value.Distance.ShouldBe(50);
        result.Value.Recency.ShouldBe("r1296000");
    }

    private static bool AllowFor(TriggerType trigger)
    {
        var context = new ScanContext();
        context.Begin(ScanRunId.New(), trigger);
        return context.AllowInteractiveLogin;
    }
}
