using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Real-Postgres migration test (T018, Principle V): the <c>LlmEnrichment</c> migration applies and
/// the new satellite tables + AI-profile/enrichment jsonb columns round-trip (lists as jsonb arrays).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LlmEnrichmentMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Satellites_and_jsonb_columns_round_trip()
    {
        await postgres.ResetAsync();
        await using var db = postgres.CreateContext();

        var offer = SeedOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var enrichment = OfferEnrichment.CreatePending(offer.Id);
        enrichment.MarkProduced("A concise summary.", ["C#", "EF Core", ".NET"], "SHA256:1:abc", DateTimeOffset.UtcNow);
        db.OfferEnrichments.Add(enrichment);

        var fit = OfferFit.CreatePending(offer.Id);
        fit.MarkProduced(82, ["C#", "EF Core"], ["Kafka"], "Strong backend match.", "SHA256:1:def", DateTimeOffset.UtcNow);
        db.OfferFits.Add(fit);

        await db.SaveChangesAsync();

        await using var verify = postgres.CreateContext();
        var loadedEnrichment = await verify.OfferEnrichments.AsNoTracking().SingleAsync(e => e.OfferId == offer.Id);
        loadedEnrichment.State.ShouldBe(EnrichmentState.Produced);
        loadedEnrichment.Summary.ShouldBe("A concise summary.");
        loadedEnrichment.KeySkills.ShouldBe(["C#", "EF Core", ".NET"]);

        var loadedFit = await verify.OfferFits.AsNoTracking().SingleAsync(f => f.OfferId == offer.Id);
        loadedFit.Score.ShouldBe(82);
        loadedFit.Matched.ShouldBe(["C#", "EF Core"]);
        loadedFit.Missing.ShouldBe(["Kafka"]);
        loadedFit.Rationale.ShouldBe("Strong backend match.");
    }

    [Fact]
    public async Task Cv_profile_and_enrichment_settings_round_trip()
    {
        await postgres.ResetAsync();
        await using var db = postgres.CreateContext();

        var cv = CandidateCv.Create(CvId.New(), "cv.pdf");
        cv.SetExtractionGauge(true, "SHA256:1:bytes", DateTimeOffset.UtcNow);
        cv.ApplyProfile(new CvProfile(["C#", "Azure"], "Senior", "Backend engineer."), "SHA256:1:bytes", DateTimeOffset.UtcNow);
        db.CandidateCvs.Add(cv);

        var settings = AppSettings.CreateDefault();
        settings.UpdateEnrichment(new EnrichmentSettings { MaxKeySkills = 7, RetryLimit = 5 });
        db.AppSettings.Add(settings);
        await db.SaveChangesAsync();

        await using var verify = postgres.CreateContext();
        var loadedCv = await verify.CandidateCvs.AsNoTracking().SingleAsync(c => c.Id == cv.Id);
        loadedCv.ProfileState.ShouldBe(CvProcessingState.Produced);
        loadedCv.Profile.ShouldNotBeNull();
        loadedCv.Profile!.Skills.ShouldBe(["C#", "Azure"]);
        loadedCv.Profile.Seniority.ShouldBe("Senior");

        var loadedSettings = await verify.AppSettings.AsNoTracking().SingleAsync();
        loadedSettings.Enrichment.MaxKeySkills.ShouldBe(7);
        loadedSettings.Enrichment.RetryLimit.ShouldBe(5);
    }

    internal static Offer SeedOffer(string nativeKey = "offer-1")
    {
        var content = new OfferContent
        {
            Title = "Backend Engineer",
            Company = "Acme",
            CanonicalUrl = $"https://example.test/o/{nativeKey}",
            RequiredSkills = ["C#"],
            DescriptionHtml = "<p>Build things.</p>",
        };
        var externalRef = ExternalRef.Create(SourceId.New(), nativeKey, IdentityKind.NativeId).Value;
        return Offer.Create(OfferId.New(), externalRef, content, ContentFingerprint.Compute(content), DateTimeOffset.UtcNow);
    }
}
