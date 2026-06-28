using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.RoleGroups;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// Attaches newly-collected offers to a cross-source <see cref="RoleGroup"/> when the conservative
/// gate matches (FR-016). Non-destructive: each offer keeps its identity; a group just lists members.
/// A persisted <see cref="RoleGroupOverride.NotSame"/> blocks auto-joining. Best-effort (SHOULD) — a
/// failure here never fails the scan.
/// </summary>
public sealed class RoleGroupingService(
    IOfferRepository offers,
    IRoleGroupRepository roleGroups,
    IUnitOfWork unitOfWork,
    ILogger<RoleGroupingService> logger)
{
    public async Task AttachAsync(IReadOnlyCollection<OfferId> touched, CancellationToken ct = default)
    {
        if (touched.Count == 0)
        {
            return;
        }

        try
        {
            await AttachCoreAsync(touched, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Role grouping failed (non-fatal — scan results are unaffected).");
        }
    }

    private async Task AttachCoreAsync(IReadOnlyCollection<OfferId> touched, CancellationToken ct)
    {
        var active = await offers.GetAllActiveAsync(ct);
        var byId = active.ToDictionary(o => o.Id);
        var groups = (await roleGroups.GetAllAsync(ct)).ToDictionary(g => g.Id);

        foreach (var offerId in touched)
        {
            if (!byId.TryGetValue(offerId, out var offer) || offer.RoleGroupId is not null)
            {
                continue; // gone, or already grouped — don't re-group
            }

            var candidate = ToCandidate(offer);
            Offer? bestOther = null;
            var best = MatchConfidence.None;

            foreach (var other in active)
            {
                if (other.Id == offer.Id)
                {
                    continue;
                }

                var confidence = RoleGroupMatcher.Evaluate(candidate, ToCandidate(other));
                if (confidence.MeetsMergeThreshold && confidence.Value > best.Value)
                {
                    best = confidence;
                    bestOther = other;
                }
            }

            if (bestOther is null)
            {
                continue;
            }

            if (bestOther.RoleGroupId is { } existingGroupId && groups.TryGetValue(existingGroupId, out var group))
            {
                if (group.UserOverride == RoleGroupOverride.NotSame)
                {
                    continue;
                }

                group.AddMember(offer.Id, best);
                offer.AttachToRoleGroup(existingGroupId);
            }
            else
            {
                var newGroup = RoleGroup.Create(RoleGroupId.New(), bestOther.Id, best);
                newGroup.AddMember(offer.Id, best);
                await roleGroups.AddAsync(newGroup, ct);
                groups[newGroup.Id] = newGroup;

                bestOther.AttachToRoleGroup(newGroup.Id);
                offer.AttachToRoleGroup(newGroup.Id);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static GroupingCandidate ToCandidate(Offer offer) =>
        new(offer.Company, offer.Title, offer.Location, offer.WorkMode);
}
