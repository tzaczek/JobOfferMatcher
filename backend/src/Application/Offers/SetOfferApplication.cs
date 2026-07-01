using System.Text.Json;
using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Applications;
using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Application.Offers;

/// <summary>
/// Mark/clear the user's "applied" flag on an offer, with an optional date and note. The flag is
/// orthogonal to <see cref="UserOfferStatus"/> (an offer can be "interested" AND "applied"); the
/// transition + note validation live INSIDE the aggregate (Principle III). Each change appends an
/// append-only <see cref="OfferEventType.Applied"/> / <see cref="OfferEventType.ApplicationCleared"/>
/// event so the offer timeline stays complete (FR-009/034).
/// <para>
/// Application tracking (005): marking applied now also CREATES the <see cref="JobApplication"/> at the
/// first pipeline stage if absent (seeding the first journal note from the note when the journal is empty),
/// so "applied ⟹ an application exists" is an invariant. Clearing prefers CLOSING over erasing (ADR-5):
/// when the application has accumulated history it returns <see cref="ApplicationHasHistory"/> (the UI
/// steers to <em>close as Withdrawn</em>); an application with no history is cleared + removed.
/// </para>
/// </summary>
public sealed class SetOfferApplication(
    IOfferRepository offers,
    IApplicationRepository applications,
    IPipelineStageRepository stages,
    IEnrichmentRepository enrichment,
    IUnitOfWork unitOfWork,
    TimeProvider time)
{
    public static readonly Error OfferNotFound = new("OfferNotFound", "Offer not found.");

    public static readonly Error ApplicationHasHistory = new(
        "ApplicationHasHistory",
        "This application has tracked history — close it (e.g. as Withdrawn) instead of clearing, or delete it permanently.");

    /// <summary>Mark the offer applied (or re-mark to edit its date/note). Idempotent on the flag.</summary>
    public async Task<Result> MarkAppliedAsync(OfferId offerId, DateTimeOffset? appliedAt, string? note, CancellationToken ct = default)
    {
        var offer = await offers.GetByIdAsync(offerId, ct);
        if (offer is null)
        {
            return OfferNotFound;
        }

        var wasApplied = offer.Applied;
        var applied = offer.MarkApplied(appliedAt, note);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        // The applied SET changed (a new member) → the affinity basis version changed → all affinity
        // pending (FR-002/FR-007, mirrors "weights change → all fits pending"). A pure re-mark that only
        // edits the date/note leaves the set unchanged, so it does NOT re-pend (006).
        if (!wasApplied)
        {
            await enrichment.InvalidateAllAffinityAsync(ct);
        }

        var payload = JsonSerializer.Serialize(new
        {
            appliedAt = offer.AppliedAt,
            note = offer.ApplicationNote,
        });
        await offers.AddEventAsync(OfferEvent.Create(offerId, time.GetUtcNow(), OfferEventType.Applied, payload), ct);

        // Create the application at the first stage if one doesn't exist yet (the invariant), seeding the
        // first journal note from the application note when the journal is empty.
        if (!await applications.ExistsAsync(offerId, ct))
        {
            var firstStage = (await stages.ListAsync(ct)).FirstOrDefault();
            if (firstStage is not null)
            {
                var application = JobApplication.Create(offerId, firstStage.Id, offer.AppliedAt, time.GetUtcNow());
                await applications.AddAsync(application, ct);

                if (!string.IsNullOrWhiteSpace(offer.ApplicationNote))
                {
                    var firstNote = ApplicationNote.Create(offerId, offer.ApplicationNote, offer.AppliedAt ?? time.GetUtcNow());
                    if (firstNote.IsSuccess)
                    {
                        await applications.AddNoteAsync(firstNote.Value, ct);
                    }
                }
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Clear the applied flag and its optional date/note. Prefers closing over erasing (ADR-5): if the
    /// application accumulated history, refuses with <see cref="ApplicationHasHistory"/> (steer-to-close).
    /// Otherwise clears the flag and removes the empty application. Idempotent: clearing a never-applied
    /// offer succeeds without appending a (misleading) <see cref="OfferEventType.ApplicationCleared"/> event.
    /// </summary>
    public async Task<Result> ClearAsync(OfferId offerId, CancellationToken ct = default)
    {
        var offer = await offers.GetByIdAsync(offerId, ct);
        if (offer is null)
        {
            return OfferNotFound;
        }

        var application = await applications.GetAsync(offerId, ct);
        if (application is not null && await applications.HasHistoryAsync(offerId, ct))
        {
            return ApplicationHasHistory;
        }

        var changed = offer.ClearApplied();
        if (changed)
        {
            await offers.AddEventAsync(OfferEvent.Create(offerId, time.GetUtcNow(), OfferEventType.ApplicationCleared), ct);
            // The applied SET shrank → the affinity basis version changed → all affinity pending (006).
            await enrichment.InvalidateAllAffinityAsync(ct);
        }

        if (application is not null)
        {
            applications.Remove(application); // empty subtree (no history) — safe to erase.
            changed = true;
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
