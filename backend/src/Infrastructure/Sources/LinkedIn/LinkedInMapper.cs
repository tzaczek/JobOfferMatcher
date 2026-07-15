using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Infrastructure.Sources.LinkedIn;

/// <summary>
/// Pure mapping from a scraped <see cref="LinkedInJobCard"/> → source-agnostic <see cref="CollectedOffer"/>
/// (feature 008). Identity is <c>(source_id, LinkedIn job id)</c>; salary is left empty (LinkedIn rarely
/// discloses it → renders "not available", Principle III). The body is fetched separately on demand.
/// </summary>
internal static class LinkedInMapper
{
    public static Result<CollectedOffer> MapCard(LinkedInJobCard card, SourceId sourceId)
    {
        var externalRef = ExternalRef.Create(sourceId, card.JobId, IdentityKind.NativeId);
        if (externalRef.IsFailure)
        {
            return externalRef.Error;
        }

        var content = new OfferContent
        {
            Title = string.IsNullOrWhiteSpace(card.Title) ? "(untitled)" : card.Title,
            Company = string.IsNullOrWhiteSpace(card.Company) ? "(unknown)" : card.Company,
            SalaryBands = [],
            Location = card.Location,
            WorkMode = card.WorkMode,
            RequiredSkills = [],
            NiceToHaveSkills = [],
            DescriptionHtml = null, // fetched on demand via FetchBodyAsync
            CanonicalUrl = card.CanonicalUrl,
        };

        return new CollectedOffer(externalRef.Value, content);
    }
}
