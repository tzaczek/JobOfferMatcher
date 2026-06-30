using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Matching;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Settings;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>
/// Input-hash composer unit tests (T013): determinism, the deliberate description-inclusion that
/// distinguishes <see cref="OfferEnrichmentInputs"/> from <see cref="ContentFingerprint"/> (FR-006),
/// and profile/weights composition for fit (FR-007).
/// </summary>
public sealed class EnrichmentInputsTests
{
    private static OfferContent Content(string description = "Build cool things.", string title = "Backend Engineer") => new()
    {
        Title = title,
        Company = "Acme",
        Location = "Remote",
        WorkMode = WorkMode.Remote,
        EmploymentType = "b2b",
        Seniority = "senior",
        RequiredSkills = ["C#", ".NET"],
        NiceToHaveSkills = ["EF Core"],
        DescriptionHtml = description,
        CanonicalUrl = "https://example.test/o/1",
    };

    [Fact]
    public void Offer_enrichment_hash_is_deterministic()
    {
        OfferEnrichmentInputs.Hash(Content()).Serialized.ShouldBe(OfferEnrichmentInputs.Hash(Content()).Serialized);
    }

    [Fact]
    public void Offer_enrichment_hash_includes_the_description_unlike_the_content_fingerprint()
    {
        var a = Content(description: "Description A");
        var b = Content(description: "Description B — totally different");

        // ContentFingerprint deliberately EXCLUDES the description (Minor tier) → equal.
        ContentFingerprint.Compute(a).Hash.ShouldBe(ContentFingerprint.Compute(b).Hash);
        // OfferEnrichmentInputs INCLUDES it (FR-006) → differs, re-flipping the summary.
        OfferEnrichmentInputs.Hash(a).Serialized.ShouldNotBe(OfferEnrichmentInputs.Hash(b).Serialized);
    }

    [Fact]
    public void Effective_profile_version_changes_when_skills_change_and_is_order_insensitive()
    {
        var prefs = new ProfilePreferences();
        var p1 = new CvProfile(["C#", "EF Core"], "Senior", "A backend dev.");
        var p2 = new CvProfile(["EF Core", "C#"], "Senior", "A backend dev."); // reordered
        var p3 = new CvProfile(["C#", "EF Core", "Kafka"], "Senior", "A backend dev.");

        EffectiveProfile.Version(p1, prefs).Serialized.ShouldBe(EffectiveProfile.Version(p2, prefs).Serialized);
        EffectiveProfile.Version(p1, prefs).Serialized.ShouldNotBe(EffectiveProfile.Version(p3, prefs).Serialized);
    }

    [Fact]
    public void Effective_profile_version_changes_when_preferences_change()
    {
        var profile = new CvProfile(["C#"], "Senior", "x");
        var a = EffectiveProfile.Version(profile, new ProfilePreferences { SalaryFloor = 16000 });
        var b = EffectiveProfile.Version(profile, new ProfilePreferences { SalaryFloor = 20000 });
        a.Serialized.ShouldNotBe(b.Serialized);
    }

    [Fact]
    public void Offer_fit_hash_changes_when_weights_change()
    {
        var offerHash = OfferEnrichmentInputs.Hash(Content());
        var profileVersion = EffectiveProfile.Version(new CvProfile(["C#"], "Senior", "x"), new ProfilePreferences());

        var a = OfferFitInputs.Hash(offerHash, profileVersion, ScoringWeights.Default);
        var b = OfferFitInputs.Hash(offerHash, profileVersion, ScoringWeights.Default with { Skills = 60, Salary = 0 });

        a.Serialized.ShouldNotBe(b.Serialized);
    }

    [Fact]
    public void Cv_profile_hash_tracks_the_bytes()
    {
        ReadOnlySpan<byte> bytes1 = [1, 2, 3, 4];
        ReadOnlySpan<byte> bytes2 = [1, 2, 3, 5];
        CvProfileInputs.Hash(bytes1).Serialized.ShouldBe(CvProfileInputs.Hash([1, 2, 3, 4]).Serialized);
        CvProfileInputs.Hash(bytes1).Serialized.ShouldNotBe(CvProfileInputs.Hash(bytes2).Serialized);
    }
}
