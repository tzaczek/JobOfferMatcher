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

    /// <summary>Longest free-text application note the Domain accepts (rejected past this — Principle III).</summary>
    public const int MaxApplicationNoteLength = 2000;

    /// <summary>
    /// The user marked that they applied to this role. Orthogonal to <see cref="UserStatus"/> and
    /// availability — an offer can be both "interested" and "applied". <see cref="AppliedAt"/> and
    /// <see cref="ApplicationNote"/> are optional metadata, only meaningful while this is true.
    /// </summary>
    public bool Applied { get; private set; }

    /// <summary>When the user applied, if they recorded a date (optional).</summary>
    public DateTimeOffset? AppliedAt { get; private set; }

    /// <summary>Free-text note about the application, if the user added one (optional).</summary>
    public string? ApplicationNote { get; private set; }

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
    /// Silently refresh the denormalized Minor-tier display fields (company/location/description) that
    /// are NOT part of the Major fingerprint (ADR-3). Does not flag "updated", bump the version, or
    /// emit an event — feature-001 new-vs-seen is unchanged (FR-013). The scan calls this on the
    /// Unchanged branch so a description-only edit is persisted for the AI summary input hash (FR-006).
    /// </summary>
    public void RefreshMinorContent(OfferContent content)
    {
        Company = content.Company;
        Location = content.Location;
        DescriptionHtml = content.DescriptionHtml;
    }

    /// <summary>
    /// Set the offer's body/description (feature 006, US2) — a <b>Minor-tier</b> mutator: it updates only
    /// the denormalized <see cref="DescriptionHtml"/> and does NOT bump the fingerprint/version, emit an
    /// event, or set <see cref="HasUnseenUpdate"/> (the same tier as <see cref="RefreshMinorContent"/>).
    /// The scan captures the body here; the raw value is stored and sanitised at the read boundary.
    /// </summary>
    public void SetDescription(string? descriptionHtml) => DescriptionHtml = descriptionHtml;

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

    /// <summary>
    /// Mark this offer as applied-to, with an optional <paramref name="appliedAt"/> date and optional
    /// free-text <paramref name="note"/>. Re-marking overwrites both (used to edit them). The note is
    /// trimmed and a blank one becomes null; an over-long note is rejected in the Domain (Principle III).
    /// Leaves <see cref="UserStatus"/> and the unseen-update flag untouched — applying is a separate axis.
    /// </summary>
    public Result MarkApplied(DateTimeOffset? appliedAt, string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > MaxApplicationNoteLength })
        {
            return new Error("ApplicationNoteTooLong", $"The application note cannot exceed {MaxApplicationNoteLength} characters.");
        }

        Applied = true;
        AppliedAt = appliedAt;
        ApplicationNote = trimmed;
        return Result.Success();
    }

    /// <summary>
    /// Clear the applied mark and its optional date/note (the user un-applies). Returns true only when
    /// it actually transitioned, so the caller can skip appending a spurious timeline event when the
    /// offer was never applied (event-deep idempotent).
    /// </summary>
    public bool ClearApplied()
    {
        if (!Applied)
        {
            return false;
        }

        Applied = false;
        AppliedAt = null;
        ApplicationNote = null;
        return true;
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
