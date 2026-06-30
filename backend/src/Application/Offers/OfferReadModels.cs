namespace JobOfferMatcher.Application.Offers;

/// <summary>Read-side DTOs shaped to the REST contract (contracts/rest-api.md §Offers).</summary>
public sealed record SalaryBandView(
    decimal? Min,
    decimal? Max,
    string? Currency,
    string? Period,
    string Basis,
    string Tax);

public sealed record ComparableMonthlyView(decimal Amount, string Currency);

public sealed record NormalizedSalaryView(
    ComparableMonthlyView ComparableMonthly,
    string Quality,
    IReadOnlyList<string> Assumptions);

/// <summary>
/// AI fit (FR-004/005). A numeric <see cref="Score"/> + matched/missing/rationale appear ONLY under
/// <c>state == "produced"</c>; <c>pending</c>/<c>failed</c> carry no score (never a non-AI fallback).
/// Fit-absence (no current produced CV profile) is modeled as a null <see cref="OfferListItem.Fit"/>.
/// </summary>
public sealed record FitView(
    string State,
    int? Score,
    IReadOnlyList<string> Matched,
    IReadOnlyList<string> Missing,
    string? Rationale);

public sealed record OfferGroupMemberView(Guid OfferId, string SourceName, string CanonicalUrl);

public sealed record OfferListItem(
    Guid OfferId,
    Guid? RoleGroupId,
    string Title,
    string Company,
    string? Location,
    string? WorkMode,
    string? EmploymentType,
    string? Seniority,
    IReadOnlyList<string> RequiredSkills,
    IReadOnlyList<string> NiceToHaveSkills,
    IReadOnlyList<SalaryBandView> SalaryBands,
    NormalizedSalaryView? NormalizedSalary,
    string? Summary,
    IReadOnlyList<string> KeySkills,
    string EnrichmentState,
    FitView? Fit,
    string? FitState,
    string CanonicalUrl,
    bool IsNew,
    bool IsUpdated,
    string Availability,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? FirstSuggestedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? PublishedAt,
    string UserStatus,
    bool Applied,
    DateTimeOffset? AppliedAt,
    string? ApplicationNote,
    IReadOnlyList<OfferGroupMemberView> GroupMembers);

public sealed record OfferListMeta(int Total, int New, bool HasProducedProfile, int PendingEnrichment, int FailedEnrichment);

public sealed record OfferListResult(IReadOnlyList<OfferListItem> Data, OfferListMeta Meta);

public sealed record OfferEventView(DateTimeOffset OccurredAt, string Type);

public sealed record OfferVersionView(DateTimeOffset CreatedAt, string ChangeTier);

public sealed record OfferDetail(
    OfferListItem Offer,
    string? DescriptionHtml,
    IReadOnlyList<OfferVersionView> Versions,
    IReadOnlyList<OfferEventView> Events);
