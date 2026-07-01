# Feature Specification: Application & Interview Process Tracking

**Feature Branch**: `005-application-tracking` (spec directory; no git branch created — repo has no `before_specify` hook)

**Created**: 2026-07-01

**Status**: Draft

**Input**: User description: "Add new feature for managing my applications. Once I applied for som job there might be multi step interview process, communcation and generally interview process. I want to have an option to note where in the process I am with my notes and potential document, interview tasks and other interview related data. Make sure no data thaht exists now is lost."

## Clarifications

### Session 2026-07-01

- Q: Which interview-process lifecycle (stage set) should an application move through? → A: **User-defined stages** — the user can create, rename, reorder, and remove their own pipeline stages. A sensible default set (derived from Constitution Principle III) is seeded on first run so tracking works immediately, but the app hard-codes no fixed stage vocabulary.
- Q: How should a final outcome relate to the user-defined pipeline stages? → A: **Separate fixed outcome dimension** — an application is either *active* (in one user-defined stage) or *closed* with a fixed outcome (Accepted / Rejected / Withdrawn / No response). Movement between pipeline stages is unrestricted; outcomes stay consistent for reporting/export regardless of stage names; a closed application can be reopened.
- Q: When clearing the "applied" state on an offer that has interview data, what happens? → A: **Prefer closing over erasing** — the user is steered to close the application (e.g. Withdrawn), keeping all history; clearing "applied" never silently destroys accumulated data. Permanently deleting an application is a separate, explicit, confirmed action, recoverable from a prior backup.
- Q: What file types and per-file size limit should document attachments allow? → A: **Any file type, ~50 MB per file** — no file-type allow-list; a file exceeding ~50 MB is rejected with a clear message. Attachments remain local and backup-covered.

## User Scenarios & Testing *(mandatory)*

<!--
  Prioritised as independently testable slices. US1 alone is a viable MVP: it answers
  "where does each of my applications stand right now?" — the core of the request.
  Later stories layer notes, tasks, documents, and communication onto that spine.
-->

### User Story 1 - Track where each application stands in the interview process (Priority: P1)

As a job seeker who has applied to several roles, I want to record and see **where each application is in its interview process** — from just-applied through screening, interviews, offer, and a final outcome — so I can tell at a glance what is active, what is waiting on me, and what has closed, without keeping it all in my head or a spreadsheet.

**Why this priority**: This is the heart of the request ("note where in the process I am") and the smallest slice that delivers standalone value. Once an applied offer can carry a stage and I can see all applications organised by stage, the feature already replaces the mental/spreadsheet tracking. Every other slice (notes, tasks, documents, communication) hangs off having an application with a stage.

**Independent Test**: Mark an offer as applied, advance it through the interview stages, and confirm a consolidated view shows every in-progress application under its current stage; confirm an already-applied offer from before this feature appears as an application at the "applied" stage with its original applied date and note intact.

**Acceptance Scenarios**:

1. **Given** an offer I have applied to, **When** I open its application, **Then** I see its current stage in the interview process and can move it to the next stage (e.g. from "applied" to "screening").
2. **Given** several applications at different stages, **When** I open the consolidated applications view, **Then** I see each one grouped/labelled by its current stage so I can see the whole pipeline at once.
3. **Given** an offer that was already marked "applied" before this feature existed, **When** the feature is in place, **Then** that offer appears as an application at the "applied" stage, preserving its recorded applied date and note — nothing is lost.
4. **Given** an application that has reached a conclusion, **When** I close it, **Then** I record an outcome (e.g. accepted, rejected, withdrawn, no response) and the closed application remains viewable rather than disappearing.
5. **Given** applications that are active and applications I have closed, **When** I view the pipeline, **Then** active applications appear under their current stage while closed ones are shown with their recorded outcome (accepted / rejected / withdrawn / no response) rather than cluttering the active pipeline.
6. **Given** my pipeline stages, **When** I add, rename, reorder, or remove a stage, **Then** the applications view and every application reflect my customized stages, and any application already sitting in a changed or removed stage remains valid and accounted for (not orphaned or lost).

---

### User Story 2 - Keep running notes and a full timeline per application (Priority: P1)

As a job seeker, I want to add my own **notes over time** to an application and see a **single chronological timeline** of everything that has happened — stage changes and my notes together — so I can answer "when did I apply, when did I hear back, what was said" without reconstructing it from memory.

**Why this priority**: The request explicitly wants "my notes" alongside knowing where I am. A running journal plus a timeline is what turns a bare stage into a usable record, and it is independently demonstrable once an application exists (US1). It is P1 because notes are called out directly and add little risk on top of US1.

**Independent Test**: Add two dated notes to an application at different stages, then open its timeline and confirm the stage changes and both notes appear in chronological order, each with when it happened.

**Acceptance Scenarios**:

1. **Given** an application, **When** I add a note, **Then** the note is saved with its timestamp and shown against that application without overwriting earlier notes.
2. **Given** an application with several notes and stage changes, **When** I view its timeline, **Then** I see all entries (stage changes and notes) in chronological order with their dates.
3. **Given** an existing already-applied offer whose original single application note was recorded before this feature, **When** I view the application, **Then** that original note is preserved and shown as the first entry, not discarded.
4. **Given** a past event I forgot to record, **When** I add a note describing it, **Then** the timeline remains an accurate, append-only history (earlier entries are never silently rewritten).

---

### User Story 3 - Track interview tasks and deadlines (Priority: P2)

As a job seeker in a multi-step process, I want to record **interview tasks** — take-home assignments, things to prepare, follow-ups — with a due date and a done/not-done status, so I never miss a deadline or forget a step.

**Why this priority**: Interview tasks are named explicitly in the request and are a distinct, valuable slice, but they depend on an application existing (US1). Missing a take-home deadline is a concrete failure this prevents.

**Independent Test**: Add a task with a due date to an application, mark it done, add a second task due in the past, and confirm the application clearly distinguishes outstanding, overdue, and completed tasks.

**Acceptance Scenarios**:

1. **Given** an application, **When** I add a task with a title and an optional due date, **Then** the task appears as outstanding against that application.
2. **Given** an outstanding task, **When** I mark it done, **Then** it is shown as completed and no longer counts as outstanding.
3. **Given** a task whose due date has passed and is not done, **When** I view the application (or the applications overview), **Then** the task is clearly flagged as overdue.
4. **Given** tasks across several applications, **When** I look at the applications overview, **Then** I can tell which applications have something outstanding or overdue waiting on me.

---

### User Story 4 - Attach documents to an application (Priority: P2)

As a job seeker, I want to **attach documents** to an application — an interview task brief, my submission, a job description snapshot, an offer letter — so every relevant file lives with the application it belongs to instead of scattered across my downloads folder and email.

**Why this priority**: Documents ("potential document") are named in the request and are independently valuable, but they build on an application existing (US1) and are lower-frequency than notes/tasks.

**Independent Test**: Attach a file to an application, confirm it is listed with its name, then retrieve/download it later and confirm the retrieved file is intact and matches what was attached.

**Acceptance Scenarios**:

1. **Given** an application, **When** I attach a document, **Then** it is listed against that application with a recognisable name and when it was added.
2. **Given** an attached document, **When** I later open or download it, **Then** I receive the same file I attached, intact.
3. **Given** an attached document, **When** I remove it, **Then** removal is deliberate (confirmed) and does not affect the rest of the application's data.
4. **Given** attached documents, **When** they are stored, **Then** they are kept locally on my machine and never committed to source control (they hold personal material).

---

### User Story 5 - Log communication and interviews (Priority: P3)

As a job seeker, I want to log **communication and interview events** for an application — who I spoke with, when, through which channel, a short summary, and upcoming or past interview rounds — so I have the full interaction history and know what is scheduled next.

**Why this priority**: This is the "communication and generally interview process … other interview related data" catch-all. It enriches the record but is the least critical slice for an MVP and depends on US1/US2 being in place.

**Independent Test**: Log a recruiter call and a scheduled technical interview against an application, then open its timeline and confirm both appear in order with their details, and an upcoming interview is distinguishable from a past one.

**Acceptance Scenarios**:

1. **Given** an application, **When** I log a communication (date, direction/channel, short summary), **Then** it is recorded against the application and appears in its timeline.
2. **Given** an application, **When** I record an interview (type, date/time, optional interviewer, optional outcome), **Then** it is recorded and, if in the future, shown as upcoming.
3. **Given** an interview that has happened, **When** I record its outcome, **Then** the outcome is captured and contributes to deciding the application's next stage.
4. **Given** an application with communications and interviews, **When** I view its timeline, **Then** these entries interleave chronologically with notes and stage changes into one coherent history.

### Edge Cases

- **Existing data on upgrade**: every offer already marked "applied" — with its applied date and note and its existing timeline events — MUST survive intact and become an application; no applied offer, date, note, or historical event is dropped or altered (the request's explicit "no data lost" requirement).
- **Un-applying an offer that now has interview data**: clearing the "applied" state on an offer that has accumulated notes, tasks, documents, or interviews does not silently erase it — the user is steered to close the application (e.g. Withdrawn) instead, keeping the history. Permanent deletion is a separate, explicit, confirmed action and stays recoverable from a prior backup.
- **Offer becomes unavailable/delisted after applying**: the application and all its interview data persist and stay fully accessible even if the source offer is no longer available in the feed (mirrors the tailored-CV persistence rule).
- **Correcting a mistaken stage**: the user can move an application back to an earlier stage or fix a wrong entry; the correction is itself recorded, so the timeline stays honest rather than being retro-edited.
- **Overdue task**: a task past its due date and not marked done is surfaced as overdue rather than silently ignored.
- **Task or interview with no date**: a due date / interview time is optional; an application can hold "someday" tasks or a to-be-scheduled interview without a fabricated date.
- **Large or unusual document**: any file type is accepted; a file larger than the ~50 MB per-file limit is rejected with a clear message rather than corrupting the record (there is no file-type restriction).
- **Closing then reopening**: an application closed with an outcome can be reopened if the process restarts (e.g. the company comes back), and the reopening is recorded.
- **Multiple interview rounds**: a single application can hold several interview rounds over time without needing a distinct lifecycle stage per round.
- **Editing pipeline stages with applications in flight**: renaming a stage keeps its applications; removing a stage that still holds applications is **refused until the user reassigns those applications to another stage** (removal names a reassignment target) — applications are never silently dropped, auto-moved without the user choosing, or left pointing at a stage that no longer exists.
- **Backup/restore**: all application data (stages, notes, tasks, documents, communications) is captured by the existing backup and restored intact, alongside offers and CVs.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST elevate an applied offer into a tracked **application** that carries a position in a **user-configurable** interview-process pipeline, building on (not replacing) the existing per-offer "applied" action.
- **FR-002**: Users MUST be able to set and change an application's current stage among the user's **defined pipeline stages**, and to record a final outcome when closing (e.g. accepted, rejected, withdrawn, no response).
- **FR-003**: An application MUST always be in a valid state — **active** (sitting in one of the user's existing pipeline stages) or **closed** (with a recorded fixed outcome: accepted / rejected / withdrawn / no response). Moving an application between pipeline stages is unrestricted (any defined stage to any other); closing MUST record an outcome; a closed application MAY be reopened. Every stage change, close, and reopen MUST be recorded as an append-only, timestamped history entry, and a recorded stage MUST always reference an existing pipeline stage (Constitution Principle III).
- **FR-004**: The system MUST provide a consolidated **applications view** that shows all in-progress applications organised by their current stage, so the user can see the whole pipeline at a glance, and MUST let the user open any application from there.
- **FR-005**: The system MUST preserve **all existing data** with no loss: every offer already marked "applied" becomes an application retaining its recorded applied date and note, and every existing offer timeline event is kept unchanged. Data migration MUST be additive/append-only (Constitution Principle IX) — existing records are never dropped, overwritten, or rewritten.
- **FR-006**: Users MUST be able to add **notes** to an application over time as a running journal (multiple dated entries), each stored with its timestamp; adding a note MUST NOT overwrite earlier notes, and the original single application note captured before this feature MUST be retained.
- **FR-007**: The system MUST present, per application, a **single chronological timeline** that merges stage changes, notes, tasks, documents, communications, and interviews, so the user can answer "when did X happen" from one place.
- **FR-008**: Users MUST be able to record **interview tasks** on an application (a title, optional description, optional due date, and done/not-done status), mark them complete, and MUST see outstanding and **overdue** tasks clearly distinguished.
- **FR-009**: The applications view MUST surface which applications have outstanding or overdue tasks, so the user can see what is waiting on them without opening each application.
- **FR-010**: Users MUST be able to **attach documents** (files) to an application, see them listed with a recognisable name and when they were added, and retrieve/download them later intact. **Any file type** is accepted, up to **~50 MB per file**; a file exceeding the limit MUST be rejected with a clear message rather than corrupting the record. Document removal MUST be deliberate (confirmed).
- **FR-011**: Users MUST be able to log **communications** (date, direction/channel, short summary) and **interviews** (type, date/time, optional interviewer, optional outcome) against an application, including upcoming interviews distinguishable from past ones.
- **FR-012**: Applications and all their interview data MUST persist and remain fully accessible even if the source offer later becomes unavailable/delisted or is removed from the feed.
- **FR-013**: When an application has accumulated interview data, clearing the offer's "applied" state MUST NOT silently erase that history: the user MUST be guided to **close** the application instead (e.g. outcome = Withdrawn), preserving all history. Permanently deleting an application MUST be a separate, explicit, confirmed action and MUST remain recoverable from a prior backup (Constitution Principles III & IX).
- **FR-014**: All application and interview data — stages, notes, tasks, documents, communications, interviews — MUST be included in the existing on-demand **backup and restore** scope (feature 003) so it is recoverable alongside the database and CV files.
- **FR-015**: All personal application data (notes, recruiter/interviewer names, communication summaries, attached documents) MUST stay **local** to the user's machine and MUST NOT be committed to source control or transmitted to any external service (Constitution Principle IV); document files MUST live within the gitignored local data area.
- **FR-016**: The system MUST keep existing behaviour unchanged — offer collection, deduplication, availability/reconciliation, enrichment, fit scoring, salary normalisation, tailored-CV generation, export, and backup/restore — adding application tracking alongside them without altering them.
- **FR-017**: The user's existing pre-application triage disposition (new / viewed / interested / dismissed) MUST remain available and orthogonal to the new application lifecycle — an offer's triage state and its application stage are independent.
- **FR-018**: The human-readable data export (feature 001) MUST include each offer's application state and key interview history so the tracked pursuit is portable and recoverable, not trapped in one app version (Constitution Principle IX).
- **FR-019**: Users MUST be able to **configure their pipeline stages** — create, rename, reorder, and remove stages that applications move through. A sensible default stage set MUST be seeded on first use so tracking works immediately without setup. Removing or renaming a stage MUST NOT lose or orphan applications currently in that stage (Constitution Principle IX); the pipeline configuration itself MUST be covered by backup/restore and stay local.

### Key Entities *(include if feature involves data)*

- **Application**: the user's tracked pursuit of a specific offer after applying — its current pipeline stage (while active), an **active/closed status**, the applied date, the **final outcome** recorded on closing (accepted / rejected / withdrawn / no response), and created/updated timestamps. One application per offer; it comes into being when the offer is marked applied and preserves the offer's prior applied date/note on upgrade.
- **Pipeline stage (user-defined)**: a stage in the user's configurable interview pipeline — a name and an order position, created/renamed/reordered/removed by the user. The ordered set of these stages is the source of truth for "where am I"; a sensible default set is seeded on first use. Terminal outcomes used when closing are a separate, fixed dimension (accepted / rejected / withdrawn / no response), **not** pipeline stages.
- **Timeline entry**: an append-only, timestamped record of something that happened on an application (a stage change, a note, a task change, a document attachment, a communication, an interview), unified into one chronological history.
- **Note**: a dated free-text journal entry the user adds to an application over time; the pre-feature single application note becomes the first such entry.
- **Interview task**: an actionable item on an application — title, optional description, optional due date, and completion status — with overdue derived from the due date and completion.
- **Interview / event**: a recorded interview round on an application — type (e.g. phone screen, technical, on-site), date/time (optional/upcoming allowed), optional interviewer, and optional outcome.
- **Communication log entry**: a recorded interaction on an application — date, direction/channel, and a short summary.
- **Document / attachment**: a file attached to an application (name, type, when added), stored locally in the gitignored data area and covered by backup/restore.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of offers that were already marked "applied" before this feature are present afterwards as applications with their original applied date and note intact — **zero** applied offers, dates, notes, or historical events are lost (the explicit "no data lost" requirement).
- **SC-002**: From a single consolidated view, the user can see the current stage of every in-progress application without opening each one.
- **SC-003**: For any application, the user can answer "when did I apply / move to a stage / hear back" from one chronological timeline in a single view.
- **SC-004**: The user can move an application freely among their pipeline stages and close it with one of the fixed outcomes (accepted / rejected / withdrawn / no response); an application is always in a valid state — active in an existing stage, or closed with a recorded outcome — and closed applications remain viewable and reopenable.
- **SC-005**: Interview tasks with due dates are surfaced such that overdue and outstanding items are visible both on the application and in the applications overview — no due-date item is silently hidden.
- **SC-006**: 100% of attached documents can be retrieved later byte-for-byte intact and are stored only locally (never committed to source control).
- **SC-007**: All application and interview data (stages, notes, tasks, documents, communications, interviews) survives a backup → restore cycle intact (feature 003).
- **SC-008**: 0 items of personal application data are transmitted to any external service, and 0 are committed to source control (Constitution Principle IV).
- **SC-009**: Existing behaviour — feed, enrichment, fit, tailored CV, export, and backup/restore — continues to work unchanged after this feature is added.

## Assumptions

- **Offer-linked, one per offer**: an application is tied 1:1 to an offer already tracked in the app and is the evolution of the existing per-offer "applied" flag. Tracking applications for jobs found entirely outside the tracker (never in the feed) is **out of scope** for this version.
- **User-configurable pipeline, seeded default** *(clarified 2026-07-01)*: pipeline stages are **defined by the user** (create / rename / reorder / remove); the app hard-codes no fixed stage vocabulary. On first use a sensible default set is seeded — drawn from Constitution Principle III (Applied → Screening → Interviewing → Offer → Closed) — so the feature works immediately without setup. Multiple interview rounds live within a single stage as repeated interview entries rather than as separate stages.
- **Notes are an append-only journal**: the user accumulates multiple dated notes over time (not one overwritten field); this is consistent with the app's append-only history (Principle III). The existing single application note is migrated as the first journal entry.
- **Documents stored as real local files**: attachments are actual files stored in the gitignored local data area (the same area feature 003 backs up) — not external links — so they are private, recoverable, and never committed (Principle IV).
- **Active/closed is separate from stage** *(clarified 2026-07-01)*: an application is either active (in one user-defined pipeline stage) or closed with a fixed outcome (Accepted / Rejected / Withdrawn / No response); outcomes are a small fixed vocabulary independent of the user's stage names so they stay consistent for reporting/export. Movement between pipeline stages is unrestricted; a closed application can be reopened.
- **Triage stays separate**: the existing new/viewed/interested/dismissed disposition and the "applied" flag are retained; the lifecycle stage is a new, orthogonal dimension layered on top of "applied".
- **Reuse of existing capabilities**: this feature reuses the existing offer timeline/event history, the apply flow and its modal, the local-first storage, and the feature-003 backup/restore mechanism rather than introducing parallel infrastructure.
- **Stack retained**: local-first, single-user web app — React (Vite, TypeScript) front end + .NET 10 (ASP.NET Core) back end + PostgreSQL (EF Core, **append-only** migrations), layered `Domain → Application → Infrastructure → Web`. Exactly the changes needed for application tracking; existing features preserved unchanged.
- **NON-GOALS**: multi-user or sharing; automated reminders, email, push, or calendar synchronisation (overdue/upcoming items are surfaced in-app only); scraping or importing recruiter emails automatically; applications not linked to a tracked offer; analytics/reporting dashboards over the application pipeline; sending any application data to an external service.
