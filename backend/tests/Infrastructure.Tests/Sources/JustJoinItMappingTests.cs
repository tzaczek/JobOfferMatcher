using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Infrastructure.Sources.JustJoinIt;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>Contract test (T021): recorded LIST/DETAIL fixtures → CollectedOffer (offline).</summary>
public sealed class JustJoinItMappingTests
{
    private const string SiteTemplate = "https://justjoin.it/job-offer/{slug}";
    private static readonly SourceId Source = SourceId.New();

    private static System.Text.Json.JsonElement FirstListItem()
    {
        using var doc = FixtureLoader.Load("justjoinit/list.json");
        return doc.RootElement.GetProperty("data")[0].Clone();
    }

    [Fact]
    public void Maps_identity_link_and_core_fields()
    {
        var result = JustJoinItMapper.MapListItem(FirstListItem(), Source, SiteTemplate);

        result.IsSuccess.ShouldBeTrue();
        var offer = result.Value;

        offer.ExternalRef.NativeKey.ShouldBe("11111111-aaaa-bbbb-cccc-000000000001");
        offer.ExternalRef.Kind.ShouldBe(IdentityKind.NativeId);
        offer.ExternalRef.SourceId.ShouldBe(Source);

        offer.Content.Title.ShouldBe("Senior .NET Engineer");
        offer.Content.Company.ShouldBe("Acme Software");
        offer.Content.WorkMode.ShouldBe(WorkMode.Remote);
        offer.Content.Seniority.ShouldBe("senior");
        offer.Content.Location.ShouldBe("Kraków");
        offer.Content.CanonicalUrl.ShouldBe("https://justjoin.it/job-offer/senior-dotnet-engineer-acme-krakow");
        offer.Content.RequiredSkills.ShouldBe(["C#", ".NET", "Azure"]);
        offer.Content.NiceToHaveSkills.ShouldBe(["Kubernetes"]);
        offer.Content.PublishedAt.ShouldNotBeNull();
        offer.Content.ExpiredAt.ShouldNotBeNull();
    }

    [Fact]
    public void Maps_both_salary_bands_with_basis_and_tax()
    {
        var offer = JustJoinItMapper.MapListItem(FirstListItem(), Source, SiteTemplate).Value;

        offer.Content.SalaryBands.Count.ShouldBe(2);

        var b2b = offer.Content.SalaryBands.Single(b => b.Basis == EmploymentBasis.B2B);
        b2b.AmountMin.ShouldBe(18000m);
        b2b.AmountMax.ShouldBe(22000m);
        b2b.Currency!.Code.ShouldBe("PLN");
        b2b.Period.ShouldBe(SalaryPeriod.Monthly);
        b2b.Tax.ShouldBe(TaxTreatment.Net); // gross:false

        var permanent = offer.Content.SalaryBands.Single(b => b.Basis == EmploymentBasis.Permanent);
        permanent.AmountMin.ShouldBe(15000m);
        permanent.Tax.ShouldBe(TaxTreatment.Gross); // gross:true
    }

    [Fact]
    public void Hidden_salary_maps_to_empty_band_list_not_a_zero_band()
    {
        using var doc = FixtureLoader.Load("justjoinit/list.json");
        var noSalary = doc.RootElement.GetProperty("data")[3].Clone(); // Stealth Co — empty employmentTypes

        var offer = JustJoinItMapper.MapListItem(noSalary, Source, SiteTemplate).Value;

        offer.Content.SalaryBands.ShouldBeEmpty();
        offer.Content.RequiredSkills.ShouldBeEmpty(); // empty skills allowed → unknown (FR-010)
    }

    [Fact]
    public void Detail_body_enriches_description()
    {
        var offer = JustJoinItMapper.MapListItem(FirstListItem(), Source, SiteTemplate).Value;
        offer.Content.DescriptionHtml.ShouldBeNull();

        using var detail = FixtureLoader.Load("justjoinit/detail.json");
        var enriched = JustJoinItMapper.WithDescription(offer, detail.RootElement);

        enriched.Content.DescriptionHtml.ShouldNotBeNull();
        enriched.Content.DescriptionHtml!.ShouldContain("Senior .NET Engineer");
    }
}
