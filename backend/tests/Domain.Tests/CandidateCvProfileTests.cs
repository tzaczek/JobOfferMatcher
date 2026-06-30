using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>CandidateCv AI-profile state-machine unit tests (T016): Unreadable is distinct from Failed and takes no retries.</summary>
public sealed class CandidateCvProfileTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

    private static CandidateCv Uploaded(bool readable = true)
    {
        var cv = CandidateCv.Create(CvId.New(), "cv.pdf");
        cv.SetExtractionGauge(readable, "SHA256:1:bytes", At);
        return cv;
    }

    [Fact]
    public void Upload_arms_the_profile_as_pending()
    {
        var cv = Uploaded();
        cv.ProfileState.ShouldBe(CvProcessingState.Pending);
        cv.HasProducedProfile.ShouldBeFalse();
        cv.EnrichmentInputHash.ShouldBe("SHA256:1:bytes");
        cv.IsReadable.ShouldBeTrue();
    }

    [Fact]
    public void Apply_profile_produces_and_exposes_it()
    {
        var cv = Uploaded();
        cv.ApplyProfile(new CvProfile(["C#", "EF Core"], "Senior", "Backend engineer."), "SHA256:1:bytes", At);

        cv.ProfileState.ShouldBe(CvProcessingState.Produced);
        cv.HasProducedProfile.ShouldBeTrue();
        cv.Profile!.Skills.ShouldBe(["C#", "EF Core"]);
        cv.Profile.Seniority.ShouldBe("Senior");
    }

    [Fact]
    public void Unreadable_is_terminal_distinct_from_failed_and_has_no_retries()
    {
        var cv = Uploaded(readable: false);

        cv.MarkUnreadable(At);

        cv.ProfileState.ShouldBe(CvProcessingState.Unreadable);
        cv.ProfileState.ShouldNotBe(CvProcessingState.Failed);
        cv.ProfileAttempts.ShouldBe(0); // no retries consumed
        cv.Profile.ShouldBeNull();

        // A failed-rerun does NOT revive an unreadable CV (content verdict).
        cv.RearmProfile();
        cv.ProfileState.ShouldBe(CvProcessingState.Unreadable);
    }

    [Fact]
    public void Profile_failures_become_terminal_only_at_the_retry_limit_then_rearm()
    {
        var cv = Uploaded();
        cv.RecordProfileFailure(retryLimit: 2, At);
        cv.ProfileState.ShouldBe(CvProcessingState.Pending);
        cv.RecordProfileFailure(retryLimit: 2, At);
        cv.ProfileState.ShouldBe(CvProcessingState.Failed);

        cv.RearmProfile();
        cv.ProfileState.ShouldBe(CvProcessingState.Pending);
        cv.ProfileAttempts.ShouldBe(0);
    }
}
