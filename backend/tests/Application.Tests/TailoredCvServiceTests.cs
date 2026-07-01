using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Application.TailoredCvs;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.TailoredCvs;
using static JobOfferMatcher.Application.Tests.TailoredCvDoubles;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// TailoredCvService unit tests (T026 US1 + T034 US2): generate-create → Pending v1; write-back
/// accept→Produced (fake renderer); render-throws→Failed; NoCvOnFile/OfferNotFound/empty-prompt
/// Results; default emphasised-skills + source-CV selection rules; regenerate version-bump; and the
/// stale-version supersede guard (SC-003 — the stored prompt equals what was submitted).
/// </summary>
public sealed class TailoredCvServiceTests
{
    private static GenerateTailoredCvRequest Request(string prompt, IReadOnlyList<string>? skills = null, Guid? sourceCvId = null) =>
        new(prompt, skills ?? [], sourceCvId);

    private static TailoredCvResultItem Produced(Guid offerId, int version, string html = "<html>cv</html>") =>
        new($"tailored:{offerId}", version, "produced", html, null);

    // ---- Generate (US1) ------------------------------------------------------------------------

    [Fact]
    public async Task Generate_create_sets_pending_version_one_and_stores_the_prompt()
    {
        var h = new TailoredCvHarness(cvs: [MakeCv()]);
        var offer = h.AddOffer(MakeOffer());

        var result = await h.Service.GenerateAsync(offer.Id, Request("tailor it for me", ["C#"]));

        result.IsSuccess.ShouldBeTrue();
        result.Value.State.ShouldBe("pending");
        result.Value.GenerationVersion.ShouldBe(1);
        result.Value.Prompt.ShouldBe("tailor it for me");
        h.Tailored.Rows[offer.Id].State.ShouldBe(TailoredCvState.Pending);
    }

    [Fact]
    public async Task Generate_without_any_cv_returns_NoCvOnFile()
    {
        var h = new TailoredCvHarness(cvs: []);
        var offer = h.AddOffer(MakeOffer());

        var result = await h.Service.GenerateAsync(offer.Id, Request("x"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("NoCvOnFile");
    }

    [Fact]
    public async Task Generate_for_an_unknown_offer_returns_OfferNotFound()
    {
        var h = new TailoredCvHarness(cvs: [MakeCv()]);

        var result = await h.Service.GenerateAsync(OfferId.New(), Request("x"));

        result.Error.Code.ShouldBe("OfferNotFound");
    }

    [Fact]
    public async Task Generate_with_an_empty_prompt_is_rejected()
    {
        var h = new TailoredCvHarness(cvs: [MakeCv()]);
        var offer = h.AddOffer(MakeOffer());

        var result = await h.Service.GenerateAsync(offer.Id, Request("   "));

        result.Error.Code.ShouldBe("InvalidTailoredCvRequest");
    }

    // ---- Write-back (US1) ----------------------------------------------------------------------

    [Fact]
    public async Task Worker_produced_writeback_renders_a_pdf_and_marks_produced()
    {
        var h = new TailoredCvHarness(cvs: [MakeCv()]);
        var offer = h.AddOffer(MakeOffer());
        await h.Service.GenerateAsync(offer.Id, Request("p", ["C#"]));

        var response = await h.Service.SubmitResultsAsync(new TailoredCvSubmitRequest([Produced(offer.Id.Value, 1)]));

        response.Accepted.ShouldBe(1);
        response.Results.Single().Outcome.ShouldBe("produced");
        h.Tailored.Rows[offer.Id].State.ShouldBe(TailoredCvState.Produced);
        h.Renderer.Calls.ShouldBe(1);
        h.Files.Saved.ShouldContainKey(offer.Id);
    }

    [Fact]
    public async Task A_render_failure_records_a_failed_attempt()
    {
        var h = new TailoredCvHarness(cvs: [MakeCv()], renderThrows: true, retryLimit: 1);
        var offer = h.AddOffer(MakeOffer());
        await h.Service.GenerateAsync(offer.Id, Request("p"));

        var response = await h.Service.SubmitResultsAsync(new TailoredCvSubmitRequest([Produced(offer.Id.Value, 1)]));

        response.Accepted.ShouldBe(0);
        response.Results.Single().Outcome.ShouldBe("renderFailed");
        h.Tailored.Rows[offer.Id].State.ShouldBe(TailoredCvState.Failed); // retryLimit 1
    }

    [Fact]
    public async Task A_worker_failed_status_records_a_failure_without_rendering()
    {
        var h = new TailoredCvHarness(cvs: [MakeCv()], retryLimit: 1);
        var offer = h.AddOffer(MakeOffer());
        await h.Service.GenerateAsync(offer.Id, Request("p"));

        var response = await h.Service.SubmitResultsAsync(new TailoredCvSubmitRequest([
            new TailoredCvResultItem($"tailored:{offer.Id.Value}", 1, "failed", null, "source CV too thin")]));

        response.Results.Single().Outcome.ShouldBe("failed");
        h.Renderer.Calls.ShouldBe(0);
        h.Tailored.Rows[offer.Id].State.ShouldBe(TailoredCvState.Failed);
    }

    // ---- Pending queue (US1) -------------------------------------------------------------------

    [Fact]
    public async Task Pending_work_carries_the_version_prompt_and_source_cv_path()
    {
        var h = new TailoredCvHarness(cvs: [MakeCv()]);
        var offer = h.AddOffer(MakeOffer());
        await h.Service.GenerateAsync(offer.Id, Request("emphasise backend", ["C#"]));

        var work = await h.Service.GetPendingWorkAsync(10);

        work.Meta.PendingTotal.ShouldBe(1);
        var item = work.Items.Single();
        item.WorkItemId.ShouldBe($"tailored:{offer.Id.Value}");
        item.GenerationVersion.ShouldBe(1);
        item.Prompt.ShouldBe("emphasise backend");
        item.Offer.Title.ShouldBe(offer.Title);
        item.SourceCv.Path.ShouldNotBeNullOrEmpty();
    }

    // ---- Draft: emphasised-skills + source-CV selection (US1) ----------------------------------

    [Fact]
    public async Task Draft_default_skills_prefer_enriched_key_skills_and_fit_when_produced()
    {
        var h = new TailoredCvHarness(cvs: [MakeCv()]);
        var offer = h.AddOffer(
            MakeOffer(required: ["C#"], nice: ["Docker"]),
            enrichmentState: "produced",
            keySkills: ["PostgreSQL", "EF Core"],
            fit: new FitView("produced", 80, ["C#"], ["Kafka"], "ok"));

        var draft = await h.Service.GetDraftAsync(offer.Id);

        draft.IsSuccess.ShouldBeTrue();
        draft.Value.EmphasisedSkills.ShouldContain("PostgreSQL"); // key skill
        draft.Value.EmphasisedSkills.ShouldContain("Kafka");      // fit missing
        draft.Value.AllOfferSkills.ShouldContain("Docker");       // nice-to-have stays in the toggle pool
        draft.Value.Prompt.ShouldContain("PostgreSQL");
        draft.Value.SourceCv.ShouldNotBeNull();
    }

    [Fact]
    public async Task Draft_default_skills_fall_back_to_offer_skills_when_enrichment_pending()
    {
        var h = new TailoredCvHarness(cvs: [MakeCv()]);
        var offer = h.AddOffer(MakeOffer(required: ["C#", ".NET"], nice: ["Docker"]));

        var draft = await h.Service.GetDraftAsync(offer.Id);

        draft.Value.EmphasisedSkills.ShouldBe(["C#", ".NET", "Docker"]);
    }

    [Fact]
    public async Task Draft_without_a_cv_returns_NoCvOnFile()
    {
        var h = new TailoredCvHarness(cvs: []);
        var offer = h.AddOffer(MakeOffer());

        var draft = await h.Service.GetDraftAsync(offer.Id);

        draft.Error.Code.ShouldBe("NoCvOnFile");
    }

    [Fact]
    public async Task Source_cv_selection_prefers_readable_then_an_explicit_override()
    {
        var older = MakeCv(readable: false, fileName: "old.pdf", uploadedAt: Now.AddDays(-2));
        var newerReadable = MakeCv(readable: true, fileName: "new.pdf", uploadedAt: Now);
        var h = new TailoredCvHarness(cvs: [older, newerReadable]);
        var offer = h.AddOffer(MakeOffer());

        // No explicit id → the most-recently-uploaded readable CV.
        (await h.Service.GenerateAsync(offer.Id, Request("p"))).IsSuccess.ShouldBeTrue();
        h.Tailored.Rows[offer.Id].SourceCvId.ShouldBe(newerReadable.Id);

        // Explicit override is honoured even if it is the unreadable one.
        await h.Service.GenerateAsync(offer.Id, Request("p2", sourceCvId: older.Id.Value));
        h.Tailored.Rows[offer.Id].SourceCvId.ShouldBe(older.Id);
    }

    // ---- Regenerate + supersede (US2 / T034) ---------------------------------------------------

    [Fact]
    public async Task Regenerate_bumps_the_version_re_pendings_and_stores_the_edited_prompt()
    {
        var h = new TailoredCvHarness(cvs: [MakeCv()]);
        var offer = h.AddOffer(MakeOffer());
        await h.Service.GenerateAsync(offer.Id, Request("first prompt", ["C#"]));
        await h.Service.SubmitResultsAsync(new TailoredCvSubmitRequest([Produced(offer.Id.Value, 1)]));

        var regen = await h.Service.GenerateAsync(offer.Id, Request("lead with leadership", ["React"]));

        regen.Value.GenerationVersion.ShouldBe(2);
        regen.Value.State.ShouldBe("pending");
        regen.Value.Prompt.ShouldBe("lead with leadership"); // SC-003 — stored == submitted
        h.Tailored.Rows[offer.Id].EmphasisedSkills.ShouldBe(["React"]);
    }

    [Fact]
    public async Task A_stale_writeback_after_a_newer_regenerate_is_superseded_and_writes_nothing()
    {
        var h = new TailoredCvHarness(cvs: [MakeCv()]);
        var offer = h.AddOffer(MakeOffer());
        await h.Service.GenerateAsync(offer.Id, Request("v1"));
        await h.Service.GenerateAsync(offer.Id, Request("v2")); // bumps to version 2, still pending

        // A slow worker posts a result echoing the OLD version 1.
        var response = await h.Service.SubmitResultsAsync(new TailoredCvSubmitRequest([Produced(offer.Id.Value, 1)]));

        response.Accepted.ShouldBe(0);
        response.Results.Single().Outcome.ShouldBe("superseded");
        h.Renderer.Calls.ShouldBe(0);               // nothing rendered
        h.Files.Saved.ShouldNotContainKey(offer.Id); // nothing written
        h.Tailored.Rows[offer.Id].State.ShouldBe(TailoredCvState.Pending);
        h.Tailored.Rows[offer.Id].GenerationVersion.ShouldBe(2);
    }
}
