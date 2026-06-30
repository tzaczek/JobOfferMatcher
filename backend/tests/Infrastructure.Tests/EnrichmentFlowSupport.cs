using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Infrastructure.Persistence;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Shared wire DTOs + seeding helpers for the US2–US5 real-Postgres enrichment flow tests
/// (T033/T042/T051/T058). Records are PascalCase; the host uses Web JSON defaults (case-insensitive),
/// so they bind the camelCase contract payloads.
/// </summary>
internal static class EnrichmentFlow
{
    public static Offer SeedOffer(string nativeKey, string description = "<p>Build things.</p>")
    {
        var content = new OfferContent
        {
            Title = $"Role {nativeKey}",
            Company = "Acme",
            CanonicalUrl = $"https://example.test/o/{nativeKey}",
            RequiredSkills = ["C#", ".NET"],
            NiceToHaveSkills = ["Kafka"],
            DescriptionHtml = description,
            PublishedAt = new DateTimeOffset(2026, 6, 20, 8, 0, 0, TimeSpan.Zero),
        };
        var externalRef = ExternalRef.Create(SourceId.New(), nativeKey, IdentityKind.NativeId).Value;
        return Offer.Create(OfferId.New(), externalRef, content, ContentFingerprint.Compute(content), DateTimeOffset.UtcNow);
    }

    /// <summary>Add an offer plus its invariant Pending satellite rows (mirrors the scan/backfill path).</summary>
    public static Offer AddOfferWithSatellites(AppDbContext db, string nativeKey, string description = "<p>Build things.</p>")
    {
        var offer = SeedOffer(nativeKey, description);
        db.Offers.Add(offer);
        db.OfferEnrichments.Add(OfferEnrichment.CreatePending(offer.Id));
        db.OfferFits.Add(OfferFit.CreatePending(offer.Id));
        return offer;
    }

    /// <summary>A CV whose AI profile is already produced (so fits become eligible).</summary>
    public static CandidateCv AddProducedCv(AppDbContext db)
    {
        var cv = CandidateCv.Create(CvId.New(), "cv.pdf");
        cv.SetExtractionGauge(true, "SHA256:1:bytes", DateTimeOffset.UtcNow);
        cv.ApplyProfile(new CvProfile(["C#", ".NET"], "Senior", "Backend engineer."), "SHA256:1:bytes", DateTimeOffset.UtcNow);
        db.CandidateCvs.Add(cv);
        return cv;
    }

    // ---- Wire DTOs ----------------------------------------------------------------------------

    public sealed record PendingEnvelope(PendingMetaDto Meta, List<PendingItemDto> Items);

    public sealed record PendingMetaDto(
        int PendingTotal,
        int PendingProfiles,
        int PendingSummaries,
        int PendingFits,
        int FailedTotal,
        bool HasProducedProfile);

    public sealed record PendingItemDto(string Kind, string WorkItemId, string InputsHash, Guid? OfferId, Guid? CvId);

    public sealed record StatusDto(
        int PendingTotal,
        int PendingProfiles,
        int PendingSummaries,
        int PendingFits,
        int FailedTotal,
        bool HasProducedProfile,
        DateTimeOffset? LastResultAt);

    public sealed record SubmitEnvelope(int Accepted, int Rejected, List<OutcomeDto> Results);

    public sealed record OutcomeDto(string WorkItemId, string Outcome, int Attempt, string State);

    public sealed record OffersEnvelope(List<OfferItem> Data, OffersMetaDto Meta);

    public sealed record OffersMetaDto(int Total, int New, bool HasProducedProfile, int PendingEnrichment, int FailedEnrichment);

    public sealed record OfferItem(
        string Title,
        string EnrichmentState,
        string? Summary,
        List<string> KeySkills,
        FitItem? Fit,
        string? FitState);

    public sealed record FitItem(string State, int? Score, List<string> Matched, List<string> Missing, string? Rationale);

    public sealed record CvEnvelope(List<CvItem> Data);

    public sealed record CvItem(string Id, string FileName, string State, string? Summary, List<string> Skills, string? Seniority);

    public sealed record RerunBody(string Scope);
}
