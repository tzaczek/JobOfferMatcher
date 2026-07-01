using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Enrichment;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>
/// AppliedBasisInputs / OfferAffinityInputs hash tests (T018): the ≥3 cold-start gate (null below),
/// order-independence, and re-flip on set / fingerprint / offer-content change (FR-002/FR-006/FR-007).
/// </summary>
public sealed class AffinityInputsTests
{
    private static (OfferId, string) Applied(string guid, string fp) => (OfferId.From(Guid.Parse(guid)), fp);

    private static readonly (OfferId, string) A = Applied("11111111-1111-1111-1111-111111111111", "fpA");
    private static readonly (OfferId, string) B = Applied("22222222-2222-2222-2222-222222222222", "fpB");
    private static readonly (OfferId, string) C = Applied("33333333-3333-3333-3333-333333333333", "fpC");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Version_is_null_below_three_applied(int count)
    {
        var applied = new[] { A, B }.Take(count).ToList();
        AppliedBasisInputs.Version(applied).ShouldBeNull();
    }

    [Fact]
    public void Version_is_present_at_three_applied()
    {
        AppliedBasisInputs.Version([A, B, C]).ShouldNotBeNull();
    }

    [Fact]
    public void Version_is_order_independent()
    {
        var one = AppliedBasisInputs.Version([A, B, C]);
        var two = AppliedBasisInputs.Version([C, A, B]);
        one!.Serialized.ShouldBe(two!.Serialized);
    }

    [Fact]
    public void Version_changes_when_the_applied_set_changes()
    {
        var d = Applied("44444444-4444-4444-4444-444444444444", "fpD");
        var before = AppliedBasisInputs.Version([A, B, C])!.Serialized;
        var after = AppliedBasisInputs.Version([A, B, C, d])!.Serialized;
        after.ShouldNotBe(before);
    }

    [Fact]
    public void Version_changes_when_an_applied_offer_fingerprint_changes()
    {
        var before = AppliedBasisInputs.Version([A, B, C])!.Serialized;
        var bChanged = (B.Item1, "fpB-v2");
        var after = AppliedBasisInputs.Version([A, bChanged, C])!.Serialized;
        after.ShouldNotBe(before);
    }

    [Fact]
    public void Affinity_hash_is_stable_for_the_same_inputs()
    {
        var basis = AppliedBasisInputs.Version([A, B, C])!;
        var offer = new InputHash(InputHash.Sha256, 1, "offer");
        OfferAffinityInputs.Hash(offer, basis).Serialized
            .ShouldBe(OfferAffinityInputs.Hash(offer, basis).Serialized);
    }

    [Fact]
    public void Affinity_hash_changes_when_the_basis_version_changes()
    {
        var offer = new InputHash(InputHash.Sha256, 1, "offer");
        var basis1 = AppliedBasisInputs.Version([A, B, C])!;
        var basis2 = AppliedBasisInputs.Version([A, B, C, Applied("44444444-4444-4444-4444-444444444444", "fpD")])!;
        OfferAffinityInputs.Hash(offer, basis2).Serialized
            .ShouldNotBe(OfferAffinityInputs.Hash(offer, basis1).Serialized);
    }

    [Fact]
    public void Affinity_hash_changes_when_the_offer_enrichment_hash_changes()
    {
        var basis = AppliedBasisInputs.Version([A, B, C])!;
        var offer1 = new InputHash(InputHash.Sha256, 1, "offer1");
        var offer2 = new InputHash(InputHash.Sha256, 1, "offer2");
        OfferAffinityInputs.Hash(offer2, basis).Serialized
            .ShouldNotBe(OfferAffinityInputs.Hash(offer1, basis).Serialized);
    }
}
