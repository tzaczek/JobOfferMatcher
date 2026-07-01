using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.TailoredCvs;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>
/// TailoredCv state-machine unit tests (T005): create → Pending v1; <see cref="TailoredCv.RequestRegeneration"/>
/// bumps the version + re-arms; the <see cref="TailoredCv.Accepts"/> supersede guard rejects a stale version;
/// failures become terminal only at the retry limit.
/// </summary>
public sealed class TailoredCvTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static TailoredCv NewRequest() =>
        TailoredCv.CreateRequest(OfferId.New(), CvId.New(), "tailor it", ["C#", ".NET"], At);

    [Fact]
    public void Create_request_is_pending_version_one()
    {
        var t = NewRequest();

        t.State.ShouldBe(TailoredCvState.Pending);
        t.GenerationVersion.ShouldBe(1);
        t.Attempts.ShouldBe(0);
        t.EmphasisedSkills.ShouldBe(["C#", ".NET"]);
        t.Prompt.ShouldBe("tailor it");
        t.HtmlFileName.ShouldBeNull();
        t.PdfFileName.ShouldBeNull();
    }

    [Fact]
    public void Mark_produced_sets_files_and_clears_attempts()
    {
        var t = NewRequest();

        t.MarkProduced(1, "tailored-x.html", "tailored-x.pdf", At);

        t.State.ShouldBe(TailoredCvState.Produced);
        t.HtmlFileName.ShouldBe("tailored-x.html");
        t.PdfFileName.ShouldBe("tailored-x.pdf");
        t.GeneratedAt.ShouldBe(At);
        t.Attempts.ShouldBe(0);
        t.LastError.ShouldBeNull();
    }

    [Fact]
    public void Regeneration_bumps_version_and_re_arms_to_pending()
    {
        var t = NewRequest();
        t.MarkProduced(1, "a.html", "a.pdf", At);

        t.RequestRegeneration(CvId.New(), "new prompt", ["React"], At.AddMinutes(5));

        t.State.ShouldBe(TailoredCvState.Pending);
        t.GenerationVersion.ShouldBe(2);
        t.Attempts.ShouldBe(0);
        t.Prompt.ShouldBe("new prompt");
        t.EmphasisedSkills.ShouldBe(["React"]);
    }

    [Fact]
    public void Accepts_only_the_current_version_while_pending()
    {
        var t = NewRequest();
        t.RequestRegeneration(CvId.New(), "v2", [], At); // now version 2

        t.Accepts(2).ShouldBeTrue();   // current + pending
        t.Accepts(1).ShouldBeFalse();  // stale version (superseded)

        t.MarkProduced(2, "a.html", "a.pdf", At);
        t.Accepts(2).ShouldBeFalse();  // no longer pending
    }

    [Fact]
    public void A_stale_write_back_throws_so_the_service_must_check_accepts_first()
    {
        var t = NewRequest();
        t.RequestRegeneration(CvId.New(), "v2", [], At); // version 2

        Should.Throw<InvalidOperationException>(() => t.MarkProduced(1, "a.html", "a.pdf", At));
        Should.Throw<InvalidOperationException>(() => t.RecordFailure(1, "x", retryLimit: 3));
    }

    [Fact]
    public void Failures_become_terminal_only_at_the_retry_limit()
    {
        var t = NewRequest();

        t.RecordFailure(1, "1", retryLimit: 2);
        t.State.ShouldBe(TailoredCvState.Pending);
        t.Attempts.ShouldBe(1);

        t.RecordFailure(1, "2", retryLimit: 2);
        t.State.ShouldBe(TailoredCvState.Failed);
        t.LastError.ShouldBe("2");
    }

    [Fact]
    public void Regenerating_a_failed_cv_re_arms_it()
    {
        var t = NewRequest();
        t.RecordFailure(1, "boom", retryLimit: 1); // → Failed
        t.State.ShouldBe(TailoredCvState.Failed);

        t.RequestRegeneration(t.SourceCvId, "again", ["C#"], At);

        t.State.ShouldBe(TailoredCvState.Pending);
        t.Attempts.ShouldBe(0);
        t.GenerationVersion.ShouldBe(2);
    }
}
