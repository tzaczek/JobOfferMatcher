using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.RoleGroups;

/// <summary>A user correction to grouping (data-model §RoleGroup); persisted, wins over the heuristic.</summary>
public enum RoleGroupOverride
{
    Same,
    NotSame,
}

/// <summary>
/// A non-destructive cross-source cluster of offers for the same role (FR-016). Each member keeps its
/// own identity/link/history; the UI shows one entry per group. A persisted user override beats the
/// heuristic confidence.
/// </summary>
public sealed class RoleGroup
{
    private IReadOnlyList<OfferId> _members = [];

    public RoleGroupId Id { get; private set; }
    public IReadOnlyList<OfferId> MemberOfferIds => _members;
    public MatchConfidence Confidence { get; private set; }
    public RoleGroupOverride? UserOverride { get; private set; }

    private RoleGroup()
    {
        // EF Core materialization.
    }

    public static RoleGroup Create(RoleGroupId id, OfferId firstMember, MatchConfidence confidence) => new()
    {
        Id = id,
        _members = [firstMember],
        Confidence = confidence,
    };

    public void AddMember(OfferId offerId, MatchConfidence confidence)
    {
        if (!_members.Contains(offerId))
        {
            _members = [.. _members, offerId];
        }

        if (confidence.Value > Confidence.Value)
        {
            Confidence = confidence;
        }
    }

    public void RemoveMember(OfferId offerId) => _members = [.. _members.Where(m => m != offerId)];

    public void SetOverride(RoleGroupOverride userOverride) => UserOverride = userOverride;

    public bool HasMember(OfferId offerId) => _members.Contains(offerId);
}
