using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Domain.Cv;
using static JobOfferMatcher.Application.Tests.EnrichmentDoubles;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// US3 CV-profile handling (T041): the profile work item is emitted first (FR-019) with the document
/// gauge; a produced result applies the AI profile; <c>unreadable</c> is a terminal verdict with no
/// retries, counted as neither pending nor failed; repeated errors become terminal <c>failed</c>.
/// </summary>
public sealed class CvProfileTests
{
    [Fact]
    public async Task Cv_profile_is_emitted_first_with_the_document_gauge()
    {
        var cv = PendingCv();
        var harness = new EnrichmentHarness(cvs: [cv], offers: [AvailableOffer("o1", Now)]);

        var work = await harness.Service.GetPendingWorkAsync(25);

        var item = work.Items.OfType<CvProfileWorkItem>().Single();
        work.Items[0].ShouldBeOfType<CvProfileWorkItem>();        // ordered first (FR-019)
        item.CvId.ShouldBe(cv.Id.Value);
        item.Document.Readable.ShouldBeTrue();                    // PdfPig gauge surfaced as a hint
        item.Document.FallbackText.ShouldBeNull();                // readable ⇒ no PII fallback text on the wire
        work.Meta.PendingProfiles.ShouldBe(1);
    }

    [Fact]
    public async Task Produced_result_applies_the_ai_profile()
    {
        var cv = PendingCv();
        var harness = new EnrichmentHarness(cvs: [cv], offers: []);
        var hash = (await harness.Service.GetPendingWorkAsync(25)).Items.OfType<CvProfileWorkItem>().Single().InputsHash;

        var response = await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"cv:{cv.Id.Value}:profile", "cvProfile", hash, "produced",
                Skills: ["C#", "Go"], Seniority: "Senior", Summary: "Backend engineer.")
        ]));

        response.Results.Single().Outcome.ShouldBe("produced");
        cv.ProfileState.ShouldBe(CvProcessingState.Produced);
        cv.Profile!.Skills.ShouldBe(["C#", "Go"]);
        cv.Profile.Seniority.ShouldBe("Senior");
        cv.Profile.Summary.ShouldBe("Backend engineer.");
    }

    [Fact]
    public async Task Unreadable_is_terminal_with_no_retry_and_counted_separately()
    {
        var cv = PendingCv();
        var harness = new EnrichmentHarness(cvs: [cv], offers: []);
        var hash = (await harness.Service.GetPendingWorkAsync(25)).Items.OfType<CvProfileWorkItem>().Single().InputsHash;

        var response = await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"cv:{cv.Id.Value}:profile", "cvProfile", hash, "unreadable")
        ]));

        response.Results.Single().Outcome.ShouldBe("unreadable");
        cv.ProfileState.ShouldBe(CvProcessingState.Unreadable);
        cv.ProfileAttempts.ShouldBe(0); // no retry consumed

        var status = await harness.Service.GetStatusAsync();
        status.PendingProfiles.ShouldBe(0); // unreadable is neither pending…
        status.FailedTotal.ShouldBe(0);     // …nor failed
    }

    [Fact]
    public async Task Profile_errors_become_terminal_failed_at_the_retry_limit()
    {
        var cv = PendingCv();
        var harness = new EnrichmentHarness(cvs: [cv], offers: [], retryLimit: 2);
        var hash = (await harness.Service.GetPendingWorkAsync(25)).Items.OfType<CvProfileWorkItem>().Single().InputsHash;

        var first = await harness.Service.SubmitResultsAsync(Error(cv, hash));
        first.Results.Single().Outcome.ShouldBe("pendingRetry");

        var second = await harness.Service.SubmitResultsAsync(Error(cv, hash));
        second.Results.Single().Outcome.ShouldBe("failed");
        cv.ProfileState.ShouldBe(CvProcessingState.Failed);
    }

    private static SubmitResultsRequest Error(CandidateCv cv, string hash) => new([
        new EnrichmentResultItem($"cv:{cv.Id.Value}:profile", "cvProfile", hash, "failed", Reason: "model error")
    ]);
}
