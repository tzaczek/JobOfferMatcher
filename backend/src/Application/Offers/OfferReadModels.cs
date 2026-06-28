namespace JobOfferMatcher.Application.Offers;

/// <summary>Read-side DTOs shaped to the REST contract (contracts/rest-api.md §Offers).</summary>
public sealed record SalaryBandView(
    decimal? Min,
    decimal? Max,
    string? Currency,
    string? Period,
    string Basis,
    string Tax);

public sealed record NormalizedSalaryView(
    decimal Amount,
    string Currency,
    string Quality,
    IReadOnlyList<string> Assumptions);

public sealed record FitView(int Score, IReadOnlyList<string> Matched, IReadOnlyList<string> Missing);

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
    FitView? Fit,
    string CanonicalUrl,
    bool IsNew,
    bool IsUpdated,
    string Availability,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? FirstSuggestedAt,
    DateTimeOffset LastSeenAt,
    string UserStatus,
    IReadOnlyList<OfferGroupMemberView> GroupMembers);

public sealed record OfferListMeta(int Total, int New, bool NoReadableCv);

public sealed record OfferListResult(IReadOnlyList<OfferListItem> Data, OfferListMeta Meta);

public sealed record OfferEventView(DateTimeOffset OccurredAt, string Type);

public sealed record OfferVersionView(DateTimeOffset CreatedAt, string ChangeTier);

public sealed record OfferDetail(
    OfferListItem Offer,
    string? DescriptionHtml,
    IReadOnlyList<OfferVersionView> Versions,
    IReadOnlyList<OfferEventView> Events);
