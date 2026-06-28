using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Domain.Offers;

/// <summary>
/// A single open position at a source (data-model §Offer): a denormalized current snapshot plus
/// append-only children (observations/versions/events, added across phases). New-vs-seen is decided
/// purely by identity existence (<see cref="FirstSuggestedAt"/> set once), never by source dates
/// (research §6). Mutated only through its methods (aggregate root).
/// </summary>
public sealed class Offer
{
    private IReadOnlyList<SalaryBand> _salaryBands = [];
    private IReadOnlyList<string> _requiredSkills = [];
    private IReadOnlyList<string> _niceToHaveSkills = [];

    public OfferId Id { get; private set; }
    public ExternalRef ExternalRef { get; private set; } = null!;
    public SourceId SourceId => ExternalRef.SourceId;

    public string Title { get; private set; } = string.Empty;
    public string Company { get; private set; } = string.Empty;
    public IReadOnlyList<SalaryBand> SalaryBands => _salaryBands;
    public string? Location { get; private set; }
    public WorkMode WorkMode { get; private set; }
    public string? EmploymentType { get; private set; }
    public string? Seniority { get; private set; }
    public IReadOnlyList<string> RequiredSkills => _requiredSkills;
    public IReadOnlyList<string> NiceToHaveSkills => _niceToHaveSkills;
    public string? DescriptionHtml { get; private set; }
    public string CanonicalUrl { get; private set; } = string.Empty;

    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? LastPublishedAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }

    public ContentFingerprint CurrentFingerprint { get; private set; } = null!;
    public int FingerprintVersion => CurrentFingerprint.Version;

    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset FirstSuggestedAt { get; private set; }

    public AvailabilityStatus Availability { get; private set; }
    public DateTimeOffset? DisappearedAt { get; private set; }
    public RoleGroupId? RoleGroupId { get; private set; }
    public UserOfferStatus UserStatus { get; private set; }

    /// <summary>True when content changed since the user last acted — drives the "Updated" badge (FR-014).</summary>
    public bool HasUnseenUpdate { get; private set; }

    private Offer()
    {
        // EF Core materialization.
    }

    /// <summary>Create a newly-discovered offer (first time its identity is seen → it is "new").</summary>
    public static Offer Create(
        OfferId id,
        ExternalRef externalRef,
        OfferContent content,
        ContentFingerprint fingerprint,
        DateTimeOffset seenAt)
    {
        var offer = new Offer
        {
            Id = id,
            ExternalRef = externalRef,
            CurrentFingerprint = fingerprint,
            FirstSeenAt = seenAt,
            LastSeenAt = seenAt,
            FirstSuggestedAt = seenAt,
            Availability = AvailabilityStatus.Available,
            UserStatus = UserOfferStatus.New,
        };
        offer.SetContent(content);
        return offer;
    }

    /// <summary>Record that the offer was seen again with unchanged content — bumps last-seen only.</summary>
    public void RegisterSighting(DateTimeOffset seenAt)
    {
        if (seenAt > LastSeenAt)
        {
            LastSeenAt = seenAt;
        }

        if (Availability == AvailabilityStatus.NoLongerAvailable)
        {
            // Seen again → it reappeared (FR-015); a richer event is appended by the orchestrator (US2).
            Availability = AvailabilityStatus.Available;
            DisappearedAt = null;
        }
    }

    /// <summary>
    /// Apply a changed content snapshot (Major-tier fingerprint differs). Updates the denormalized
    /// snapshot and current fingerprint, bumps last-seen, and returns true (content changed).
    /// </summary>
    public bool ApplyUpdate(OfferContent content, ContentFingerprint fingerprint, DateTimeOffset seenAt)
    {
        var changed = OfferClassifier.Classify(CurrentFingerprint, fingerprint) == OfferChangeKind.Updated;
        SetContent(content);
        CurrentFingerprint = fingerprint;
        if (changed)
        {
            // Content changed → flag for the feed; the user's status is untouched (FR-014/SC-002:
            // a dismissed offer that changes stays dismissed and never re-appears as new).
            HasUnseenUpdate = true;
        }

        RegisterSighting(seenAt);
        return changed;
    }

    public void MarkUnavailable(DateTimeOffset at)
    {
        Availability = AvailabilityStatus.NoLongerAvailable;
        DisappearedAt = at;
    }

    /// <summary>
    /// Apply a user-set disposition (FR-031). Rejects illegal transitions in the Domain via
    /// <see cref="Result"/> (Principle III): status is never set back to <see cref="UserOfferStatus.New"/>.
    /// Acting on an offer clears its unseen-update flag.
    /// </summary>
    public Result ChangeUserStatus(UserOfferStatus newStatus)
    {
        if (newStatus == UserOfferStatus.New)
        {
            return new Error("InvalidStatusTransition", "An offer cannot be set back to 'new'.");
        }

        UserStatus = newStatus;
        HasUnseenUpdate = false;
        return Result.Success();
    }

    public void AttachToRoleGroup(RoleGroupId roleGroupId) => RoleGroupId = roleGroupId;

    public void DetachFromRoleGroup() => RoleGroupId = null;

    private void SetContent(OfferContent content)
    {
        Title = content.Title;
        Company = content.Company;
        _salaryBands = [.. content.SalaryBands];
        Location = content.Location;
        WorkMode = content.WorkMode;
        EmploymentType = content.EmploymentType;
        Seniority = content.Seniority;
        _requiredSkills = [.. content.RequiredSkills];
        _niceToHaveSkills = [.. content.NiceToHaveSkills];
        DescriptionHtml = content.DescriptionHtml;
        CanonicalUrl = content.CanonicalUrl;
        PublishedAt = content.PublishedAt;
        LastPublishedAt = content.LastPublishedAt;
        ExpiredAt = content.ExpiredAt;
    }
}
