using JobOfferMatcher.Application.Applications;
using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF adapter for the <see cref="JobApplication"/> satellite + its child tables (data-model §3/§4).
/// Single-row gets are tracked (for mutation); board/list reads are no-tracking; the service owns the
/// commit via <c>IUnitOfWork</c>. The board card aggregates (outstanding/overdue tasks, next interview)
/// are derived on read. Stage-change/close/reopen history lives in <c>offer_event</c> (appended by the
/// service via <c>IOfferRepository</c>); this adapter reads those rows back for the timeline.
/// </summary>
internal sealed class ApplicationRepository(AppDbContext db) : IApplicationRepository
{
    private static readonly OfferEventType[] ApplicationEventTypes =
    [
        OfferEventType.ApplicationStageChanged,
        OfferEventType.ApplicationClosed,
        OfferEventType.ApplicationReopened,
    ];

    public Task<JobApplication?> GetAsync(OfferId offerId, CancellationToken ct = default) =>
        db.Applications.FirstOrDefaultAsync(a => a.OfferId == offerId, ct);

    public Task<bool> ExistsAsync(OfferId offerId, CancellationToken ct = default) =>
        db.Applications.AnyAsync(a => a.OfferId == offerId, ct);

    public async Task AddAsync(JobApplication application, CancellationToken ct = default) =>
        await db.Applications.AddAsync(application, ct);

    public void Remove(JobApplication application) => db.Applications.Remove(application);

    public async Task<ApplicationBoard> GetBoardAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var stages = await db.PipelineStages.AsNoTracking().OrderBy(s => s.Position).ToListAsync(ct);

        var apps = await (
            from a in db.Applications.AsNoTracking()
            join o in db.Offers.AsNoTracking() on a.OfferId equals o.Id
            select new BoardRow(a.OfferId, o.Title, o.Company, a.CurrentStageId, a.Status, a.Outcome, a.AppliedAt))
            .ToListAsync(ct);

        var tasks = await db.ApplicationTasks.AsNoTracking().ToListAsync(ct);
        var upcoming = await db.ApplicationInterviews.AsNoTracking()
            .Where(i => i.ScheduledAt != null && i.ScheduledAt > now)
            .ToListAsync(ct);

        var tasksByOffer = tasks.GroupBy(t => t.OfferId).ToDictionary(g => g.Key, g => g.ToList());
        var nextByOffer = upcoming.GroupBy(i => i.OfferId).ToDictionary(g => g.Key, g => g.Min(i => i.ScheduledAt));

        ApplicationCard ToCard(BoardRow r)
        {
            List<ApplicationTask> ts = tasksByOffer.TryGetValue(r.OfferId, out var list) ? list : [];
            var outstanding = ts.Count(t => t.CompletedAt is null);
            var overdue = ts.Count(t => t.IsOverdue(now));
            nextByOffer.TryGetValue(r.OfferId, out var next);
            return new ApplicationCard(
                r.OfferId, r.Title, r.Company, r.StageId, r.Status, r.Outcome, r.AppliedAt, outstanding, overdue, next);
        }

        var cards = apps.Select(ToCard).ToList();

        var byStage = cards
            .Where(c => c.Status == ApplicationStatus.Active)
            .GroupBy(c => c.StageId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ApplicationCard>)g.OrderByDescending(SortKey).ToList());

        var boardStages = stages
            .Select(s => new ApplicationBoardStage(
                s.Id, s.Name, s.Position,
                byStage.TryGetValue(s.Id, out var inStage) ? inStage : []))
            .ToList();

        var closed = cards
            .Where(c => c.Status == ApplicationStatus.Closed)
            .OrderByDescending(SortKey)
            .ToList();

        return new ApplicationBoard(boardStages, closed);

        static DateTimeOffset SortKey(ApplicationCard c) => c.AppliedAt ?? DateTimeOffset.MinValue;
    }

    public async Task<ApplicationHeader?> GetHeaderAsync(OfferId offerId, CancellationToken ct = default) =>
        await (
            from a in db.Applications.AsNoTracking()
            join o in db.Offers.AsNoTracking() on a.OfferId equals o.Id
            where a.OfferId == offerId
            select new ApplicationHeader(a.OfferId, o.Title, o.Company, a.CurrentStageId, a.Status, a.Outcome, a.AppliedAt, a.ClosedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<OfferEvent>> GetApplicationEventsAsync(OfferId offerId, CancellationToken ct = default) =>
        await db.OfferEvents.AsNoTracking()
            .Where(e => e.OfferId == offerId && ApplicationEventTypes.Contains(e.Type))
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);

    public async Task AddNoteAsync(ApplicationNote note, CancellationToken ct = default) =>
        await db.ApplicationNotes.AddAsync(note, ct);

    public async Task<IReadOnlyList<ApplicationNote>> GetNotesAsync(OfferId offerId, CancellationToken ct = default) =>
        await db.ApplicationNotes.AsNoTracking()
            .Where(n => n.OfferId == offerId)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);

    public Task<bool> HasNotesAsync(OfferId offerId, CancellationToken ct = default) =>
        db.ApplicationNotes.AnyAsync(n => n.OfferId == offerId, ct);

    public async Task AddTaskAsync(ApplicationTask task, CancellationToken ct = default) =>
        await db.ApplicationTasks.AddAsync(task, ct);

    public Task<ApplicationTask?> GetTaskAsync(OfferId offerId, ApplicationTaskId id, CancellationToken ct = default) =>
        db.ApplicationTasks.FirstOrDefaultAsync(t => t.OfferId == offerId && t.Id == id, ct);

    public void RemoveTask(ApplicationTask task) => db.ApplicationTasks.Remove(task);

    public async Task<IReadOnlyList<ApplicationTask>> GetTasksAsync(OfferId offerId, CancellationToken ct = default) =>
        await db.ApplicationTasks.AsNoTracking()
            .Where(t => t.OfferId == offerId)
            .OrderBy(t => t.DueAt == null)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task AddDocumentAsync(ApplicationDocument document, CancellationToken ct = default) =>
        await db.ApplicationDocuments.AddAsync(document, ct);

    public Task<ApplicationDocument?> GetDocumentAsync(OfferId offerId, ApplicationDocumentId id, CancellationToken ct = default) =>
        db.ApplicationDocuments.FirstOrDefaultAsync(d => d.OfferId == offerId && d.Id == id, ct);

    public void RemoveDocument(ApplicationDocument document) => db.ApplicationDocuments.Remove(document);

    public async Task<IReadOnlyList<ApplicationDocument>> GetDocumentsAsync(OfferId offerId, CancellationToken ct = default) =>
        await db.ApplicationDocuments.AsNoTracking()
            .Where(d => d.OfferId == offerId)
            .OrderByDescending(d => d.AddedAt)
            .ToListAsync(ct);

    public async Task AddCommunicationAsync(ApplicationCommunication communication, CancellationToken ct = default) =>
        await db.ApplicationCommunications.AddAsync(communication, ct);

    public async Task<IReadOnlyList<ApplicationCommunication>> GetCommunicationsAsync(OfferId offerId, CancellationToken ct = default) =>
        await db.ApplicationCommunications.AsNoTracking()
            .Where(c => c.OfferId == offerId)
            .OrderByDescending(c => c.OccurredAt)
            .ToListAsync(ct);

    public async Task AddInterviewAsync(ApplicationInterview interview, CancellationToken ct = default) =>
        await db.ApplicationInterviews.AddAsync(interview, ct);

    public Task<ApplicationInterview?> GetInterviewAsync(OfferId offerId, ApplicationInterviewId id, CancellationToken ct = default) =>
        db.ApplicationInterviews.FirstOrDefaultAsync(i => i.OfferId == offerId && i.Id == id, ct);

    public void RemoveInterview(ApplicationInterview interview) => db.ApplicationInterviews.Remove(interview);

    public async Task<IReadOnlyList<ApplicationInterview>> GetInterviewsAsync(OfferId offerId, CancellationToken ct = default) =>
        await db.ApplicationInterviews.AsNoTracking()
            .Where(i => i.OfferId == offerId)
            .OrderBy(i => i.ScheduledAt == null)
            .ThenBy(i => i.ScheduledAt)
            .ThenBy(i => i.CreatedAt)
            .ToListAsync(ct);

    public async Task<bool> HasHistoryAsync(OfferId offerId, CancellationToken ct = default) =>
        // The clear-guard protects genuine interview-process tracking (tasks/documents/communications/
        // interviews). The journal note is deliberately NOT counted: an offer applied with a note must
        // still be un-markable, exactly as it was before 005 (FR-016) — clearing prefers closing only
        // once real process history has accumulated (ADR-5).
        await db.ApplicationTasks.AnyAsync(t => t.OfferId == offerId, ct)
        || await db.ApplicationDocuments.AnyAsync(d => d.OfferId == offerId, ct)
        || await db.ApplicationCommunications.AnyAsync(c => c.OfferId == offerId, ct)
        || await db.ApplicationInterviews.AnyAsync(i => i.OfferId == offerId, ct);

    // EF projection target for the board join (offer title/company + application fields).
    private sealed record BoardRow(
        OfferId OfferId,
        string Title,
        string Company,
        PipelineStageId StageId,
        ApplicationStatus Status,
        ApplicationOutcome? Outcome,
        DateTimeOffset? AppliedAt);
}
