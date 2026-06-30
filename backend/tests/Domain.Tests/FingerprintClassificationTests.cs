using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>Unit tests (T036): fingerprint diff → new / updated / unchanged; never re-flag unchanged as new.</summary>
public sealed class FingerprintClassificationTests
{
    private static OfferContent Content(decimal? salaryMax = 22000, string? description = null) => new()
    {
        Title = "Senior .NET Engineer",
        Company = "Acme",
        CanonicalUrl = "https://justjoin.it/job-offer/acme",
        WorkMode = WorkMode.Remote,
        Seniority = "senior",
        RequiredSkills = ["C#", ".NET"],
        SalaryBands = salaryMax is null
            ? []
            : [new SalaryBand { AmountMin = 18000, AmountMax = salaryMax, Currency = Currency.Pln, Period = SalaryPeriod.Monthly, Basis = EmploymentBasis.B2B }],
        DescriptionHtml = description,
    };

    [Fact]
    public void Fingerprint_is_deterministic_for_equal_content()
    {
        ContentFingerprint.Compute(Content()).Hash.ShouldBe(ContentFingerprint.Compute(Content()).Hash);
    }

    [Fact]
    public void Skill_order_does_not_change_the_fingerprint()
    {
        var a = Content() with { RequiredSkills = ["C#", ".NET"] };
        var b = Content() with { RequiredSkills = [".NET", "C#"] };
        ContentFingerprint.Compute(a).Hash.ShouldBe(ContentFingerprint.Compute(b).Hash);
    }

    [Fact]
    public void Description_change_is_minor_and_does_not_change_the_fingerprint()
    {
        var withDesc = ContentFingerprint.Compute(Content(description: "<p>Hello</p>"));
        var withoutDesc = ContentFingerprint.Compute(Content(description: null));
        withDesc.Hash.ShouldBe(withoutDesc.Hash);
    }

    [Fact]
    public void Salary_change_changes_the_fingerprint()
    {
        ContentFingerprint.Compute(Content(salaryMax: 22000)).Hash
            .ShouldNotBe(ContentFingerprint.Compute(Content(salaryMax: 25000)).Hash);
    }

    [Fact]
    public void Classify_returns_new_when_no_existing_identity()
    {
        OfferClassifier.Classify(null, ContentFingerprint.Compute(Content())).ShouldBe(OfferChangeKind.New);
    }

    [Fact]
    public void Classify_returns_unchanged_for_identical_fingerprints_never_new()
    {
        var fp = ContentFingerprint.Compute(Content());
        OfferClassifier.Classify(fp, fp).ShouldBe(OfferChangeKind.Unchanged);
    }

    [Fact]
    public void Classify_returns_updated_when_content_differs()
    {
        var existing = ContentFingerprint.Compute(Content(salaryMax: 22000));
        var incoming = ContentFingerprint.Compute(Content(salaryMax: 25000));
        OfferClassifier.Classify(existing, incoming).ShouldBe(OfferChangeKind.Updated);
    }

    [Fact]
    public void Classify_suppresses_updated_for_algorithm_version_only_change()
    {
        var existing = ContentFingerprint.Compute(Content());
        var differentVersion = existing with { Version = existing.Version + 1, Hash = "deadbeef" };
        OfferClassifier.Classify(existing, differentVersion).ShouldBe(OfferChangeKind.Unchanged);
    }
}
