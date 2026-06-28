using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Domain.Offers;

/// <summary>
/// The collected, normalized content of an offer as seen in one scan. Carries both Major-tier
/// fields (title/salary/skills/work-mode/employment/seniority — drive the fingerprint and the
/// "updated" flag) and Minor-tier fields (description — versioned silently). Mapped from the
/// source payload by the adapter; consumed by <see cref="Offer"/> and <see cref="ContentFingerprint"/>.
/// </summary>
public sealed record OfferContent
{
    public required string Title { get; init; }
    public required string Company { get; init; }
    public IReadOnlyList<SalaryBand> SalaryBands { get; init; } = [];
    public string? Location { get; init; }
    public WorkMode WorkMode { get; init; } = WorkMode.Unknown;
    public string? EmploymentType { get; init; }
    public string? Seniority { get; init; }
    public IReadOnlyList<string> RequiredSkills { get; init; } = [];
    public IReadOnlyList<string> NiceToHaveSkills { get; init; } = [];

    /// <summary>Minor tier — sanitized for display, NOT part of the Major-tier fingerprint.</summary>
    public string? DescriptionHtml { get; init; }

    public required string CanonicalUrl { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset? LastPublishedAt { get; init; }
    public DateTimeOffset? ExpiredAt { get; init; }
}
