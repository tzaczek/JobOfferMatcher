using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>
/// SetDescription Minor-tier test (T033, feature 006 US2): capturing the body must NOT change the
/// Major fingerprint, its version, or the unseen-update flag — the body is excluded from identity
/// (only summary/fit/affinity inputs consume it).
/// </summary>
public sealed class OfferDescriptionTests
{
    private static Offer NewOffer()
    {
        var content = new OfferContent
        {
            Title = "Senior .NET Engineer",
            Company = "Acme",
            CanonicalUrl = "https://example.test/x",
            RequiredSkills = ["C#"],
            DescriptionHtml = null,
        };
        var externalRef = ExternalRef.Create(SourceId.New(), "k1", IdentityKind.NativeId).Value;
        return Offer.Create(OfferId.New(), externalRef, content, ContentFingerprint.Compute(content), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void SetDescription_is_minor_tier_and_leaves_identity_untouched()
    {
        var offer = NewOffer();
        var fingerprintBefore = offer.CurrentFingerprint.Hash;
        var versionBefore = offer.FingerprintVersion;
        offer.HasUnseenUpdate.ShouldBeFalse();

        offer.SetDescription("<p>Full description &amp; requirements.</p>");

        offer.DescriptionHtml.ShouldBe("<p>Full description &amp; requirements.</p>");
        offer.CurrentFingerprint.Hash.ShouldBe(fingerprintBefore);
        offer.FingerprintVersion.ShouldBe(versionBefore);
        offer.HasUnseenUpdate.ShouldBeFalse(); // never flags "Updated"
    }

    [Fact]
    public void SetDescription_accepts_null_to_clear_the_body()
    {
        var offer = NewOffer();
        offer.SetDescription("<p>Something.</p>");

        offer.SetDescription(null);

        offer.DescriptionHtml.ShouldBeNull();
        offer.HasUnseenUpdate.ShouldBeFalse();
    }
}
