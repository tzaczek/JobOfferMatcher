using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.RoleGroups;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>Unit tests (T060): the conservative cross-source grouping gate (FR-016, research §6.4).</summary>
public sealed class RoleGroupMatchingTests
{
    private static GroupingCandidate Candidate(
        string company = "Acme Software sp. z o.o.",
        string title = "Senior .NET Engineer",
        string? location = "Kraków",
        WorkMode workMode = WorkMode.Remote) => new(company, title, location, workMode);

    [Fact]
    public void Same_company_after_stripping_legal_suffixes_and_same_title_merges()
    {
        var a = Candidate(company: "Acme Software sp. z o.o.");
        var b = Candidate(company: "Acme Software S.A.");

        var confidence = RoleGroupMatcher.Evaluate(a, b);

        confidence.MeetsMergeThreshold.ShouldBeTrue();
    }

    [Fact]
    public void Different_company_does_not_merge()
    {
        var confidence = RoleGroupMatcher.Evaluate(Candidate(company: "Acme"), Candidate(company: "Globex"));
        confidence.MeetsMergeThreshold.ShouldBeFalse();
    }

    [Fact]
    public void Different_title_below_threshold_defaults_to_not_merging()
    {
        var a = Candidate(title: "Senior .NET Engineer");
        var b = Candidate(title: "Junior Frontend React Developer");

        RoleGroupMatcher.Evaluate(a, b).MeetsMergeThreshold.ShouldBeFalse();
    }

    [Fact]
    public void Incompatible_location_does_not_merge()
    {
        var a = Candidate(location: "Kraków", workMode: WorkMode.Office);
        var b = Candidate(location: "Gdańsk", workMode: WorkMode.Office);

        RoleGroupMatcher.Evaluate(a, b).MeetsMergeThreshold.ShouldBeFalse();
    }

    [Fact]
    public void Both_remote_are_location_compatible()
    {
        var a = Candidate(location: "Kraków", workMode: WorkMode.Remote);
        var b = Candidate(location: "Warszawa", workMode: WorkMode.Hybrid);

        RoleGroupMatcher.Evaluate(a, b).MeetsMergeThreshold.ShouldBeTrue();
    }

    [Fact]
    public void Persisted_user_override_wins_over_the_heuristic()
    {
        var group = RoleGroup.Create(
            Domain.Common.Ids.RoleGroupId.New(),
            Domain.Common.Ids.OfferId.New(),
            new MatchConfidence(0.9));

        group.SetOverride(RoleGroupOverride.NotSame);
        group.UserOverride.ShouldBe(RoleGroupOverride.NotSame);
    }
}
