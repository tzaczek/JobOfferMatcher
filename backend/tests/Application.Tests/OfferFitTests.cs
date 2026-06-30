using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Matching;
using JobOfferMatcher.Domain.Settings;
using static JobOfferMatcher.Application.Tests.EnrichmentDoubles;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// US4 AI fit (T050): fit work is emitted only with a produced CV profile (profile-before-fit, FR-019);
/// a produced result stores score/matched/missing/rationale; an out-of-range score is rejected; a
/// weights/preferences change invalidates every fit (FR-007/SC-004); and a stale hash is ignored.
/// </summary>
public sealed class OfferFitTests
{
    [Fact]
    public async Task Fit_is_not_emitted_without_a_produced_profile()
    {
        var harness = new EnrichmentHarness(cvs: [PendingCv()], offers: [AvailableOffer("o1", Now)]);

        var work = await harness.Service.GetPendingWorkAsync(25);

        work.Items.OfType<OfferFitWorkItem>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Produced_fit_stores_score_matched_missing_and_rationale()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [ProducedCv()], offers: [offer]);
        var hash = (await harness.Service.GetPendingWorkAsync(25)).Items.OfType<OfferFitWorkItem>().Single().InputsHash;

        var response = await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{offer.Id.Value}:fit", "offerFit", hash, "produced",
                Score: 82, Matched: ["C#"], Missing: ["Kafka"], Rationale: "Strong overlap")
        ]));

        response.Results.Single().Outcome.ShouldBe("produced");
        var fit = harness.Enrichment.Fits[offer.Id];
        fit.State.ShouldBe(EnrichmentState.Produced);
        fit.Score.ShouldBe(82);
        fit.Matched.ShouldBe(["C#"]);
        fit.Missing.ShouldBe(["Kafka"]);
        fit.Rationale.ShouldBe("Strong overlap");
    }

    [Fact]
    public async Task Score_outside_0_to_100_is_rejected()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [ProducedCv()], offers: [offer], retryLimit: 1);
        var hash = (await harness.Service.GetPendingWorkAsync(25)).Items.OfType<OfferFitWorkItem>().Single().InputsHash;

        var response = await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{offer.Id.Value}:fit", "offerFit", hash, "produced",
                Score: 150, Matched: [], Missing: [])
        ]));

        response.Results.Single().Outcome.ShouldBe("failed");
        harness.Enrichment.Fits[offer.Id].State.ShouldBe(EnrichmentState.Failed);
    }

    [Fact]
    public async Task A_stale_fit_hash_is_ignored()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [ProducedCv()], offers: [offer]);

        var response = await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{offer.Id.Value}:fit", "offerFit", "SHA256:1:not-current", "produced",
                Score: 50, Matched: [], Missing: [])
        ]));

        response.Results.Single().Outcome.ShouldBe("stale");
        harness.Enrichment.Fits[offer.Id].State.ShouldBe(EnrichmentState.Pending);
    }

    [Fact]
    public async Task A_weights_change_invalidates_every_produced_fit()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [ProducedCv()], offers: [offer]);
        var hash = (await harness.Service.GetPendingWorkAsync(25)).Items.OfType<OfferFitWorkItem>().Single().InputsHash;
        await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{offer.Id.Value}:fit", "offerFit", hash, "produced", Score: 80, Matched: [], Missing: [])
        ]));
        harness.Enrichment.Fits[offer.Id].State.ShouldBe(EnrichmentState.Produced);

        var settingsService = new SettingsService(new FakeSettingsRepo(harness.Settings), harness.Enrichment, new FakeUnitOfWork());
        var result = await settingsService.UpdateWeightsAsync(new ScoringWeights(Skills: 60, Seniority: 10, WorkMode: 10, Employment: 10, Salary: 10));

        result.IsSuccess.ShouldBeTrue();
        harness.Enrichment.Fits[offer.Id].State.ShouldBe(EnrichmentState.Pending); // FR-007/SC-004
    }

    [Fact]
    public async Task A_preferences_change_invalidates_every_produced_fit()
    {
        var offer = AvailableOffer("o1", Now);
        var harness = new EnrichmentHarness(cvs: [ProducedCv()], offers: [offer]);
        var hash = (await harness.Service.GetPendingWorkAsync(25)).Items.OfType<OfferFitWorkItem>().Single().InputsHash;
        await harness.Service.SubmitResultsAsync(new SubmitResultsRequest([
            new EnrichmentResultItem($"offer:{offer.Id.Value}:fit", "offerFit", hash, "produced", Score: 80, Matched: [], Missing: [])
        ]));

        var profileService = new ProfileService(new FakeCvRepo([]), new FakeSettingsRepo(harness.Settings), harness.Enrichment, new FakeUnitOfWork());
        await profileService.UpdatePreferencesAsync(new ProfilePreferences
        {
            SalaryFloor = 16000,
            SalaryTarget = 22000,
            PreferredWorkModes = ["Remote"],
            PreferredEmployment = [],
        });

        harness.Enrichment.Fits[offer.Id].State.ShouldBe(EnrichmentState.Pending);
    }
}
