using JobOfferMatcher.Application.Backup;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// Unit test for the pure cross-version policy (003 T023, FR-017): <c>Same</c> when the backup's tip is
/// HEAD, <c>Older</c> when it is a known earlier migration, <c>Newer</c> (refuse) when unknown.
/// </summary>
public sealed class BackupCompatibilityTests
{
    private static readonly IReadOnlyList<string> Known = ["20260628131759_InitialSources", "20260629112020_LlmEnrichment", "20260629155706_AppliedFlag"];

    [Fact]
    public void Tip_equal_to_head_is_Same()
    {
        BackupCompatibilityPolicy.Decide("20260629155706_AppliedFlag", Known).ShouldBe(BackupCompatibility.Same);
    }

    [Fact]
    public void Known_earlier_tip_is_Older()
    {
        BackupCompatibilityPolicy.Decide("20260628131759_InitialSources", Known).ShouldBe(BackupCompatibility.Older);
        BackupCompatibilityPolicy.Decide("20260629112020_LlmEnrichment", Known).ShouldBe(BackupCompatibility.Older);
    }

    [Fact]
    public void Unknown_tip_is_Newer()
    {
        BackupCompatibilityPolicy.Decide("20270101000000_FutureMigration", Known).ShouldBe(BackupCompatibility.Newer);
    }

    [Fact]
    public void Empty_known_set_is_Newer()
    {
        BackupCompatibilityPolicy.Decide("anything", []).ShouldBe(BackupCompatibility.Newer);
    }
}
