---
description: "Task list for Application & Interview Process Tracking (005)"
---

# Tasks: Application & Interview Process Tracking

**Input**: Design documents from `specs/005-application-tracking/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/applications-api.md, quickstart.md

**Tests**: INCLUDED — the constitution mandates real-DB integration tests (Principle V) and green-before-done
(Principle VI); the plan ships a test inventory. Test tasks are first-class here.

**Organization**: grouped by user story (US1–US5) so each is an independently testable increment. US1 is the MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5 (Setup/Foundational/Polish carry no story label)
- Paths are repo-relative; backend = `backend/src` + `backend/tests`, frontend = `frontend/src` + `frontend/tests`.

**Load-bearing constraints (from plan.md):** ONE append-only migration adds all **7 tables** (schema only) —
so all Domain entities + EF configs exist before it is generated. **No existing table/column/migration is
edited**; `offer_event` gains 3 enum values (string column, no migration). **No data lost** is guaranteed by
an idempotent seed (default stages) + `BackfillApplicationsAsync` running at **startup AND on older-restore**.

---

## Phase 1: Setup (Shared)

**Purpose**: prerequisites that aren't code behaviour.

- [X] T001 [P] Confirm **no new dependency** is needed (no NuGet, no npm, no AI SDK) and that document
  attachments reuse the existing `cv-data` root + raised Kestrel body limit (`backend/src/Web/Program.cs`,
  already 4 GB) — note it in `specs/005-application-tracking/quickstart.md` (already documented).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the shared Domain + the single migration + ports/adapters + seeding + backup inclusion + FE
scaffolding every story needs. No user-facing behaviour ships here.

**⚠️ CRITICAL**: no user story can begin until this phase is complete. Because all 7 tables ship in **one**
migration, every Domain entity + EF config below must exist before T013 generates it.

### Domain (framework-free)

- [X] T002 [P] Add 6 wrapped ids (`PipelineStageId`, `ApplicationNoteId`, `ApplicationTaskId`,
  `ApplicationDocumentId`, `ApplicationCommunicationId`, `ApplicationInterviewId`) to
  `backend/src/Domain/Common/Ids/Ids.cs` (the `JobApplication` root reuses `OfferId`).
- [X] T003 [P] Create `ApplicationStatus {Active,Closed}`, `ApplicationOutcome {Accepted,Rejected,Withdrawn,NoResponse}`,
  `CommunicationDirection {Inbound,Outbound}` in `backend/src/Domain/Applications/ApplicationEnums.cs`.
- [X] T004 Add `ApplicationStageChanged`, `ApplicationClosed`, `ApplicationReopened` to the `OfferEventType`
  enum in `backend/src/Domain/Offers/OfferEvent.cs` (no migration — `offer_event.type` is varchar(40)).
- [X] T005 Create the `JobApplication` aggregate (key `OfferId`; `CurrentStageId`, `Status`, `Outcome`,
  `AppliedAt`, `ClosedAt`, `CreatedAt`, `UpdatedAt`; methods `Create`, `MoveToStage`, `Close`, `Reopen` →
  `Result` per data-model §3/§5; private ctor for EF) in `backend/src/Domain/Applications/JobApplication.cs`
  (depends T002, T003).
- [X] T006 [P] Create the `PipelineStage` entity (`Id`, `Name`≤80, `Position`, `CreatedAt`; `Create`,
  `Rename`, `MoveTo`) in `backend/src/Domain/Applications/PipelineStage.cs` (depends T002).
- [X] T007 [P] Create `ApplicationNote` (immutable `Body`+`CreatedAt`) and `ApplicationTask` (`Title`,
  `Description?`, `DueAt?`, `CompletedAt?`; `Complete`/`Reopen`/`Edit`; pure `IsOverdue(now)`) in
  `backend/src/Domain/Applications/ApplicationNote.cs` + `ApplicationTask.cs` (depends T002).
- [X] T008 [P] Create `ApplicationDocument` (`StoredFileName`, `OriginalFileName`, `ContentType?`,
  `SizeBytes`, `AddedAt`), `ApplicationCommunication` (`OccurredAt`, `Direction`, `Channel`, `Summary`),
  `ApplicationInterview` (`Kind`, `ScheduledAt?`, `Interviewer?`, `Outcome?`, `Notes?`; `RecordOutcome`/`Edit`;
  pure `IsUpcoming(now)`) in `backend/src/Domain/Applications/` (depends T002, T003).

### Persistence (EF Core + the single migration)

- [X] T009 [P] Add 6 `ValueConverter`s for the new ids to
  `backend/src/Infrastructure/Persistence/Converters/StronglyTypedIdConverters.cs` (depends T002).
- [X] T010 Create `PipelineStageConfiguration` (table `pipeline_stage`, index `position`) + `ApplicationConfiguration`
  (table `application`, PK `offer_id` `ValueGeneratedNever` FK→`offers` cascade, `current_stage_id` FK→`pipeline_stage`
  **RESTRICT**, `status`/`outcome` `HasConversion<string>`, indexes) in
  `backend/src/Infrastructure/Persistence/Configurations/` (depends T005, T006).
- [X] T011 Create the 5 child configs (`ApplicationNoteConfiguration`, `ApplicationTaskConfiguration`,
  `ApplicationDocumentConfiguration`, `ApplicationCommunicationConfiguration`, `ApplicationInterviewConfiguration`
  — each FK→`application(offer_id)` cascade, `text`/jsonb columns, per-table indexes per data-model §4) in
  `backend/src/Infrastructure/Persistence/Configurations/` (depends T007, T008).
- [X] T012 Add the 7 `DbSet<>`s and register the 6 id converters in `ConfigureConventions` in
  `backend/src/Infrastructure/Persistence/AppDbContext.cs` (depends T005–T008, T009).
- [X] T013 Generate the append-only migration `ApplicationTracking` (`dotnet ef migrations add ApplicationTracking`)
  — 7 tables, non-identity uuid PKs, safe DDL defaults (data-model §4) — committing `*_ApplicationTracking.cs`
  + designer + updated `AppDbContextModelSnapshot.cs` under `backend/src/Infrastructure/Persistence/Migrations/`
  (depends T010, T011, T012).

### Ports + adapters + seeding

- [X] T014 [P] Define ports + read-model/DTO records: `IApplicationRepository`, `IPipelineStageRepository`,
  `IApplicationDocumentFileStore`, `IApplicationBackfill`; `ApplicationBoard`/`ApplicationCard`/
  `ApplicationDetail`/`ApplicationTimelineEntry` read models; the wire DTOs per contracts, in
  `backend/src/Application/Applications/` (depends T005–T008).
- [X] T015 Implement `ApplicationRepository` (tracked `GetAsync(offerId)`, `AddAsync`, child add/get/remove,
  the board query, the derived timeline union query) in
  `backend/src/Infrastructure/Persistence/Repositories/ApplicationRepository.cs` (depends T014, T012).
- [X] T016 [P] Implement `PipelineStageRepository` (list-ordered, add, rename, reorder, remove, count-applications-in-stage)
  in `backend/src/Infrastructure/Persistence/Repositories/PipelineStageRepository.cs` (depends T014, T012).
- [X] T017 [P] Implement `LocalApplicationDocumentFileStore` — flat `appdoc-{ApplicationDocumentId:N}{ext}` in the
  **same** `Cv:StoragePath ?? {BaseDirectory}/cv-data` root (save, get-path, delete) in
  `backend/src/Infrastructure/Applications/LocalApplicationDocumentFileStore.cs` (depends T014).
- [X] T018 Add `SeedPipelineStagesAsync` (**seed-if-empty**: Applied(0)→Screening(1)→Interviewing(2)→Offer(3))
  to `backend/src/Infrastructure/Persistence/Seed/DatabaseSeeder.cs` and call it from `SeedAsync` (depends T012).

### DI + endpoint skeleton + backup inclusion

- [X] T019 Register Infrastructure adapters (`IApplicationRepository`→`ApplicationRepository` scoped;
  `IPipelineStageRepository`→`PipelineStageRepository` scoped; `IApplicationDocumentFileStore`→`LocalApplicationDocumentFileStore`
  **singleton**, like `LocalCvFileStore`) in `backend/src/Infrastructure/DependencyInjection.cs` (depends T015, T016, T017).
- [X] T020 Create the `ApplicationEndpoints` group skeleton (`api.MapGroup("/applications")` — UI-local, **no**
  loopback filter, same model as `/api/offers`) in `backend/src/Web/Endpoints/ApplicationEndpoints.cs` and wire
  `api.MapApplicationEndpoints();` in `backend/src/Web/Endpoints/FeatureEndpoints.cs`.
- [X] T021 Add the 7 tables to `BackupTables.InsertOrder` in FK order — `pipeline_stage` (Tier 1, before `offers`);
  `application` (Tier 3, after `offers` **and** `pipeline_stage`); the 5 child tables (Tier 4, after `application`)
  — in `backend/src/Application/Backup/BackupTables.cs`.
- [X] T022 Confirm the existing **`BackupTablesCompletenessTests`** (model tables == `InsertOrder`) is green with the
  7 new tables present (it fails until T021 lands — the guard doing its job) in
  `backend/tests/Infrastructure.Tests/Backup/BackupTablesCompletenessTests.cs` (depends T012, T021).

### Frontend scaffolding (shared)

- [X] T023 [P] Add the DTO/union types (`ApplicationStatus`, `ApplicationOutcome`, `PipelineStageDto`,
  `ApplicationBoardDto`, `ApplicationCardDto`, `ApplicationDetailDto`, `TimelineEntryDto`, `NoteDto`, `TaskDto`,
  `DocumentDto`, `CommunicationDto`, `InterviewDto`) to `frontend/src/api/types.ts` (mirror the `TailoredCvDto` style).
- [X] T024 [P] Create the `frontend/src/api/applications.ts` client skeleton (stages CRUD, `getBoard`, `getApplication`,
  `moveStage`, `close`, `reopen`, `deleteApplication`; note/task/document/communication/interview calls filled per story)
  using `api.*` from `client.ts` (depends T023).
- [X] T025 [P] Add `{ to:'/applications', label:'Applications' }` to `NAV`, the import, and `<Route path="/applications" .../>`
  in `frontend/src/App.tsx`, plus a placeholder `frontend/src/pages/Applications/ApplicationsPage.tsx` (depends T023).

**Checkpoint**: foundation ready — schema migrated, default stages seeded, adapters wired, backup covers the 7
tables, endpoint group mounted, FE scaffolding in place. User stories can begin.

---

## Phase 3: User Story 1 — Track where each application stands (Priority: P1) 🎯 MVP

**Goal**: an applied offer becomes an application on a user-configurable pipeline; move it between stages; close
with an outcome and reopen; see the whole pipeline on one board; **and no existing applied data is lost**.

**Independent Test**: mark an offer applied → it appears on the **Applications** board at *Applied* → move to
*Screening* → close *Rejected* (shows in the closed section) → reopen. A pre-existing applied offer appears as an
application at *Applied* with its original date + note (as the first journal entry). Rename/reorder/remove stages;
removing an occupied stage is blocked/reassigned.

### Implementation — backend

- [X] T026 [P] [US1] Domain tests: `JobApplication` state machine (free `MoveToStage`; `Close` requires outcome +
  rejects double-close; `Reopen` requires closed) in `backend/tests/Domain.Tests/Applications/JobApplicationTests.cs` (depends T005).
- [X] T027 [P] [US1] Domain tests: `PipelineStage` rename + `MoveTo` ordering in
  `backend/tests/Domain.Tests/Applications/PipelineStageTests.cs` (depends T006).
- [X] T028 [US1] Implement `PipelineStageService` (list, create-at-end, rename, reorder, **remove with reassign
  guard** — reject/`StageInUse` when applications reference it and no valid `reassignTo`) in
  `backend/src/Application/Applications/PipelineStageService.cs` (depends T016).
- [X] T029 [US1] Implement `ApplicationTrackingService` core: `GetBoardAsync` (active by stage + closed by outcome +
  card task/interview counts), `GetAsync` (detail — stage/status/outcome), `MoveStageAsync`, `CloseAsync`,
  `ReopenAsync`, `DeleteAsync(confirm)`; append `offer_event` history on stage/close/reopen; **all write paths call
  `MaintenanceGate.WaitWhileActiveAsync`** in `backend/src/Application/Applications/ApplicationTrackingService.cs`
  (depends T014, T015).
- [X] T030 [US1] Edit `SetOfferApplication` in `backend/src/Application/Offers/SetOfferApplication.cs`:
  `MarkAppliedAsync` **creates** the `JobApplication` at the lowest-position stage if absent (+ seed first journal
  note from the note when the journal is empty); `ClearAsync` returns **`ApplicationHasHistory`** (steer-to-close)
  when interview data exists, else clears + removes the empty application (depends T029, T015).
- [X] T031 [US1] Implement `BackfillApplicationsAsync` (idempotent: ensure stages exist → for each applied offer with
  no application, create one at the first stage + migrate the legacy `ApplicationNote` → first note) in
  `backend/src/Infrastructure/Persistence/DatabaseInitializer.cs` and call it from `InitializeAsync`; implement
  `ApplicationBackfillRunner : IApplicationBackfill` in `backend/src/Infrastructure/Persistence/Repositories/ApplicationBackfillRunner.cs`
  and call it in `backend/src/Application/Backup/RestoreService.cs` after the enrichment backfill for an `Older` restore
  (depends T015, T018). **[no-data-loss — SC-001/SC-007]**
- [X] T032 [US1] Register `ApplicationTrackingService` + `PipelineStageService` (scoped) in
  `backend/src/Application/DependencyInjection.cs`; register `IApplicationBackfill`→`ApplicationBackfillRunner`
  (scoped) in `backend/src/Infrastructure/DependencyInjection.cs` (depends T028, T029, T031).
- [X] T033 [US1] Fill US1 endpoints in `backend/src/Web/Endpoints/ApplicationEndpoints.cs`: stage config
  (`GET/POST /applications/stages`, `PUT /applications/stages/{id}`, `PUT /applications/stages/order`,
  `DELETE /applications/stages/{id}?reassignTo=`), `GET /applications` (board), `GET /applications/{offerId}`,
  `POST /applications/{offerId}/stage|close|reopen`, `DELETE /applications/{offerId}?confirm=true`; and map
  `ClearAsync`'s `ApplicationHasHistory` → 409 in `backend/src/Web/Endpoints/OfferEndpoints.cs` (depends T020, T028, T029, T030).

### Tests — US1

- [X] T034 [P] [US1] Application tests: create-on-apply, clear-guard steer-to-close, close/reopen, delete-confirm;
  `PipelineStageService` reorder + remove-guard, in `backend/tests/Application.Tests/ApplicationTrackingServiceTests.cs`
  (depends T028, T029, T030).
- [X] T035 [P] [US1] Infra integration (real Postgres): `application`+`pipeline_stage` round-trip; board read model;
  move/close/reopen persist + append `offer_event`; stage-delete guard; **an application whose offer is
  `NoLongerAvailable` still appears on the board and is retrievable via detail (FR-012)**, in
  `backend/tests/Infrastructure.Tests/Applications/ApplicationFlowTests.cs` (depends T033).
- [X] T036 [P] [US1] Infra **no-data-loss backfill** tests: (a) an applied offer with a legacy note → `BackfillApplicationsAsync`
  yields an application at the first stage + the note as the first journal entry; (b) restoring a **pre-005** backup →
  applications reconstructed; (c) re-running the backfill creates nothing (idempotent), in
  `backend/tests/Infrastructure.Tests/Applications/ApplicationBackfillTests.cs` (depends T031).

### Implementation — frontend

- [X] T037 [US1] Build the **Applications** pipeline board (columns per stage with active cards; a closed section
  grouped by outcome; card shows title/company/appliedAt) in `frontend/src/pages/Applications/ApplicationsPage.tsx`
  (+ `.css`) (depends T024, T025).
- [X] T038 [US1] Create `ApplicationDrawer` (detail surface: stage selector, close-with-outcome / reopen, header) —
  reuse the `ApplyModal` portal + focus-trap + `.modal*` classes — opened from a board card and the offer card, in
  `frontend/src/components/ApplicationDrawer/ApplicationDrawer.tsx` (+ `.css`) (depends T024).
- [X] T039 [US1] Create `PipelineStagesSection` (add / rename / reorder / remove-with-reassign) as a self-contained
  settings-card and compose `<PipelineStagesSection/>` in `frontend/src/pages/Settings/SettingsPage.tsx`
  (`frontend/src/pages/Settings/PipelineStagesSection.tsx`) (depends T024).
- [X] T040 [US1] Edit `frontend/src/components/OfferCard/OfferCard.tsx`: when applied, show the current-stage `.chip`
  + a link that opens the application (drawer), reusing theme chips (depends T024, T038).
- [X] T041 [P] [US1] Frontend tests (Vitest + RTL) in `frontend/tests/applications/`: board renders stages + cards +
  closed section; drawer moves stage / closes / reopens; `PipelineStagesSection` add/rename/reorder/remove; offer-card
  stage indicator (depends T037, T038, T039, T040).

**Checkpoint**: US1 fully functional — the pipeline board + lifecycle work, stages are configurable, and **no
existing applied data is lost** (MVP).

---

## Phase 4: User Story 2 — Notes journal + full timeline (Priority: P1)

**Goal**: add dated notes over time (append-only) and see one chronological timeline merging stage changes + notes
(and, later, tasks/documents/communications/interviews).

**Independent Test**: add two notes at different times (neither overwrites the other); the timeline shows both notes +
the stage changes in order; a pre-existing migrated note appears as the first entry.

### Implementation

- [X] T042 [US2] Add `AddNoteAsync` + the **derived timeline** (union of application `offer_event` rows + child tables,
  ordered by time) to `backend/src/Application/Applications/ApplicationTrackingService.cs` and the timeline query to
  `ApplicationRepository` (depends T029, T015).
- [X] T043 [US2] Add `POST /applications/{offerId}/notes` and include `timeline` + `notes` in the `GET /applications/{offerId}`
  detail response in `backend/src/Web/Endpoints/ApplicationEndpoints.cs` (depends T033, T042).
- [X] T044 [US2] Add the notes journal (add + list) and the timeline view to
  `frontend/src/components/ApplicationDrawer/ApplicationDrawer.tsx` + `addNote` in `frontend/src/api/applications.ts`
  (depends T038, T042).

### Tests — US2

- [X] T045 [P] [US2] Infra test: the timeline merges stage changes + notes chronologically and the migrated legacy note
  appears **first**, in `backend/tests/Infrastructure.Tests/Applications/ApplicationTimelineTests.cs` (depends T042, T031).
- [X] T046 [P] [US2] Frontend test: adding a note appends (no overwrite) and the timeline renders in order, in
  `frontend/tests/applications/ApplicationDrawer.notes.test.tsx` (depends T044).

**Checkpoint**: US1 + US2 work — stages + a running journal and unified timeline.

---

## Phase 5: User Story 3 — Interview tasks and deadlines (Priority: P2)

**Goal**: record tasks with optional due dates; mark done; overdue and outstanding are surfaced on the application
and on the board card.

**Independent Test**: add a future-due and a past-due (not-done) task → the past-due one is flagged **overdue**; mark
the future one done → it stops counting; the board card shows an outstanding/overdue indicator without opening it.

### Implementation

- [X] T047 [P] [US3] Domain test: `ApplicationTask.IsOverdue` (due < now && not completed) in
  `backend/tests/Domain.Tests/Applications/ApplicationTaskTests.cs` (depends T007).
- [X] T048 [US3] Add task methods (`AddTaskAsync`, `CompleteTaskAsync`/reopen, `EditTaskAsync`, `RemoveTaskAsync`) to
  `ApplicationTrackingService` + repo, and populate `outstandingTaskCount`/`overdueTaskCount` on `ApplicationCard`, in
  `backend/src/Application/Applications/ApplicationTrackingService.cs` (depends T029, T015).
- [X] T049 [US3] Add `POST /applications/{offerId}/tasks`, `PUT …/tasks/{taskId}`, `DELETE …/tasks/{taskId}` +
  task DTOs (`overdue` derived) and **include the `tasks` collection in the `GET /{offerId}` detail response** in
  `backend/src/Web/Endpoints/ApplicationEndpoints.cs` (depends T033, T048).
- [X] T050 [US3] Add tasks UI (add / complete / overdue styling) to `ApplicationDrawer` and the outstanding/overdue
  badge to the board card in `frontend/src/pages/Applications/ApplicationsPage.tsx` +
  `frontend/src/components/ApplicationDrawer/ApplicationDrawer.tsx` + task calls in `frontend/src/api/applications.ts`
  (depends T037, T038, T048).
- [X] T051 [P] [US3] Tests: Application (complete → not outstanding; overdue counts) in
  `backend/tests/Application.Tests/ApplicationTrackingServiceTests.cs`; Infra overdue query in
  `backend/tests/Infrastructure.Tests/Applications/ApplicationFlowTests.cs`; frontend overdue badge in
  `frontend/tests/applications/ApplicationTasks.test.tsx` (depends T048, T050).

**Checkpoint**: US1–US3 work — stages, notes/timeline, and task deadlines.

---

## Phase 6: User Story 4 — Attach documents (Priority: P2)

**Goal**: attach any-type files (≤ ~50 MB) to an application, list them, download them intact; stored locally in
`cv-data`, covered by backup.

**Independent Test**: attach a file → it lists with name + added-at → download → byte-identical; a > ~50 MB file is
rejected with a clear message; the file lives under `cv-data/` as `appdoc-…` (gitignored, not committed).

### Implementation

- [X] T052 [US4] Add document methods (`AddDocumentAsync` with the **50 MB cap** (`MaxDocumentBytes = 50 * 1024 * 1024` = 52,428,800 bytes) → `FileTooLarge` `Result`,
  `GetDocumentPathAsync` for download, `RemoveDocumentAsync` deleting row + file via `IApplicationDocumentFileStore`)
  to `backend/src/Application/Applications/ApplicationTrackingService.cs` (depends T029, T017).
- [X] T053 [US4] Add `POST /applications/{offerId}/documents` (multipart, 50 MB / 52,428,800 B reject → 413/400),
  `GET …/documents/{docId}/download` (stream via `Results.File` + `Content-Disposition`), `DELETE …/documents/{docId}`,
  and **include the `documents` collection in the `GET /{offerId}` detail response**,
  in `backend/src/Web/Endpoints/ApplicationEndpoints.cs` (depends T033, T052).
- [X] T054 [US4] Add document upload / list / download / remove UI to `ApplicationDrawer` and `uploadDocument` +
  `downloadDocument` (blob → `<a download>`, mirror `api/backup.ts`) in `frontend/src/api/applications.ts` +
  `frontend/src/components/ApplicationDrawer/ApplicationDrawer.tsx` (depends T038, T024, T052).
- [X] T055 [P] [US4] Tests: Infra document round-trip **byte-identical** + 50 MB reject + a backup includes the flat
  `appdoc-*` files, in `backend/tests/Infrastructure.Tests/Applications/ApplicationDocumentTests.cs`; frontend
  upload/list/download in `frontend/tests/applications/ApplicationDocuments.test.tsx` (depends T053, T054).

**Checkpoint**: US1–US4 work — plus local, backup-covered document attachments.

---

## Phase 7: User Story 5 — Log communications and interviews (Priority: P3)

**Goal**: log communications (date/direction/channel/summary) and interviews (kind/date/interviewer/outcome); upcoming
interviews are distinguished; both interleave into the timeline.

**Independent Test**: log a recruiter communication and record a future interview (shown **upcoming**) → later record its
outcome → both appear in the timeline interleaved with notes and stage changes.

### Implementation

- [X] T056 [US5] Add communication (`AddCommunicationAsync`) + interview (`AddInterviewAsync`,
  `UpdateInterviewAsync` for outcome/edit, `RemoveInterviewAsync`) methods to `ApplicationTrackingService` + repo, and
  include them (interviews by `ScheduledAt`, `upcoming` derived) in the timeline, in
  `backend/src/Application/Applications/ApplicationTrackingService.cs` (depends T029, T042).
- [X] T057 [US5] Add `POST …/communications`, `POST …/interviews`, `PUT …/interviews/{id}`, `DELETE …/interviews/{id}`
  + DTOs, and **include the `communications` + `interviews` collections in the `GET /{offerId}` detail response**, in
  `backend/src/Web/Endpoints/ApplicationEndpoints.cs` (depends T033, T056).
- [X] T058 [US5] Add communications + interviews UI (log comm, record interview, add outcome, upcoming vs past) to
  `frontend/src/components/ApplicationDrawer/ApplicationDrawer.tsx` + calls in `frontend/src/api/applications.ts`
  (depends T038, T056).
- [X] T059 [P] [US5] Tests: Application/Infra (interview outcome update; `upcoming`; timeline interleave) in
  `backend/tests/Infrastructure.Tests/Applications/ApplicationTimelineTests.cs`; frontend in
  `frontend/tests/applications/ApplicationInterviews.test.tsx` (depends T056, T058).

**Checkpoint**: all five stories independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T060 [P] **Export (FR-018)**: add `ApplicationStage`/`ApplicationStatus`/`ApplicationOutcome` + a compact
  `InterviewEventExport` list to `backend/src/Application/Export/OfferExport.cs`; read them in
  `backend/src/Infrastructure/Persistence/Repositories/ExportReader.cs`; add 3 CSV columns + a joined interview column
  (JSON automatic) in `backend/src/Application/Export/ExportService.cs`; extend
  `backend/tests/Application.Tests/ExportServiceTests.cs` (depends T029, T015).
- [X] T061 [P] **Backup/restore round-trip** including all 7 tables **and** the flat `cv-data/appdoc-*` files
  (backup → wipe → restore → assert rows + files byte-identical) in
  `backend/tests/Infrastructure.Tests/Backup/BackupRestoreApplicationTests.cs` (depends T033, T053).
- [X] T062 [P] **`MaintenanceGate` defer** test: an application write (`MoveStageAsync`/`AddNoteAsync`) **defers** during
  an active restore (mirror the enrichment-defer case) in
  `backend/tests/Infrastructure.Tests/Backup/MaintenanceGatingTests.cs` (depends T029).
- [X] T063 [P] **No-external-call guard**: confirm the feature adds no external/AI call and `/api/applications/*` is
  UI-local (assert/comment in the existing no-AI-package guard test in `backend/tests/Infrastructure.Tests/`) (depends T033).
- [X] T064 [P] **FR-016 regression gate**: run the full **001–004** backend suites (`Domain.Tests`, `Application.Tests`,
  `Infrastructure.Tests`) **and** the frontend suite green — additive design means no existing test should need editing;
  if one does, that is a regression to investigate.
- [X] T065 Run `specs/005-application-tracking/quickstart.md` end-to-end — all five stories + the **no-data-loss upgrade**,
  the **backup/restore** round-trip, and the **cross-version (pre-005) restore** backfill — and **visually verify**
  (Principle VII) the Applications board, the drawer/timeline, the Settings pipeline-stage editor, the offer-card stage
  indicator, and a downloaded document. (FR-016 regression is its own gate, T064.)

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)** → no deps.
- **Foundational (P2)** → after Setup; **BLOCKS all stories**. Order inside: Domain (T002,T003 ∥; T004; T005 after
  T002,T003; T006,T007,T008 ∥) → Persistence (T009 ∥; T010,T011; T012; **T013 migration** after T010,T011,T012) →
  adapters (T014 → T015/T016/T017) → seeding (T018) → DI/endpoints/backup (T019, T020, T021→T022) → FE scaffolding
  (T023 → T024,T025).
- **US1 (P3)** → after Foundational. **MVP** (includes the no-data-loss backfill, T031/T036).
- **US2 (P4)**, **US3 (P5)**, **US4 (P6)**, **US5 (P7)** → after Foundational; each builds on US1's service/drawer
  (so practically after US1), but each is an independently testable slice.
- **Polish (P8)** → after the stories it touches.

### Story dependencies

- **US1** — needs only Foundational. Delivers the board + lifecycle + configurable stages + no-data-loss MVP.
- **US2** — reuses `ApplicationTrackingService` + the drawer; adds notes + the derived timeline.
- **US3** — adds task methods + endpoints + UI + board indicator (task entity already exists from Foundational).
- **US4** — adds document methods + upload/download endpoints + UI (file store already exists from Foundational).
- **US5** — adds communications + interviews + their timeline contribution.

### Same-file serialization (NOT parallel)

- `ApplicationTrackingService.cs`: T029 → T042 → T048 → T052 → T056 (US1→US2→US3→US4→US5) — sequential.
- `ApplicationEndpoints.cs`: T020 → T033 → T043 → T049 → T053 → T057 — sequential.
- `ApplicationRepository.cs`: T015 → T042 → T048 → T052 → T056 — sequential.
- `ApplicationDrawer.tsx`: T038 → T044 → T050 → T054 → T058 — sequential.
- `api/applications.ts`: T024 → T044 → T050 → T054 → T058 — sequential.
- `ApplicationsPage.tsx`: T037 → T050 — sequential.
- `OfferCard.tsx`: T040 (US1) — after T038.
- `ApplicationTimelineTests.cs`: T045 → T059 — sequential.
- `ApplicationTrackingServiceTests.cs`: T034 → T051 — sequential.

### Parallel opportunities

- **Foundational**: T002, T003 ∥; T006, T007, T008 ∥ (after T002/T003); T009 ∥; T014 after entities; T016, T017 ∥
  after T014; T023 ∥, then T024, T025 ∥.
- **US1**: T026, T027 (domain tests) ∥; T034, T035, T036 (backend tests) ∥ once targets exist; T037/T038/T039/T040
  (FE) proceed while backend tests run; T041 after the FE pieces.
- **Polish**: T060, T061, T062, T063, T064 ∥ (T065 — the final manual quickstart + visual verification — runs last).

---

## Parallel Example: Foundational Domain entities

```bash
# After T002 (ids) + T003 (enums), these target different files:
Task: "Create PipelineStage in backend/src/Domain/Applications/PipelineStage.cs"                       # T006
Task: "Create ApplicationNote + ApplicationTask in backend/src/Domain/Applications/"                   # T007
Task: "Create ApplicationDocument + ApplicationCommunication + ApplicationInterview in .../Applications/" # T008
```

## Parallel Example: User Story 1 backend tests

```bash
Task: "Application tests in backend/tests/Application.Tests/ApplicationTrackingServiceTests.cs"          # T034
Task: "Infra flow test in backend/tests/Infrastructure.Tests/Applications/ApplicationFlowTests.cs"       # T035
Task: "No-data-loss backfill test in backend/tests/Infrastructure.Tests/Applications/ApplicationBackfillTests.cs" # T036
```

---

## Implementation Strategy

### MVP first (US1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational (CRITICAL — one migration, seeding, backfill infra) → 3. Phase 3 US1 →
   **STOP & VALIDATE**: mark an offer applied, drive it across the board, close/reopen, and confirm a pre-existing
   applied offer survived as an application (no data lost) → demo.

### Incremental delivery

- Foundational ready → **US1** (board + lifecycle + configurable stages + no-data-loss, MVP) → **US2** (notes + timeline)
  → **US3** (tasks + overdue) → **US4** (documents) → **US5** (communications + interviews). Each adds value without
  breaking the prior; run the relevant quickstart checks at each checkpoint.

### Cross-feature caution

- **No data lost is the headline**: T013 (migration) + T018 (seed) + T021 (`BackupTables`) + T031 (backfill on startup
  **and** older-restore) must all land for the guarantee to hold on every path; T036 + T061 + T065 are the proofs.
- T021 (`BackupTables` edit) + T022 (completeness guard) must land with T013 so a backup taken after the migration
  includes the 7 tables (FR-014).

---

## Notes

- [P] = different files, no incomplete-task dependency. [Story] maps a task to US1–US5 for traceability.
- Tests are required (Principle V/VI): Domain/Application unit tests + **real-Postgres** integration tests
  (Testcontainers); the DB is never mocked.
- **Additive only**: no existing table/column/migration is edited; `offer_event` gains 3 enum values (no migration);
  `offer.Applied`/`AppliedAt`/`ApplicationNote` + all `offer_event` rows are retained.
- Reuse, don't reinvent: the satellite pattern (`OfferFit`/`tailored_cv`), `offer_event` for history, the derived
  timeline, `MaintenanceGate`, `ApplyModal`(+css)/`.modal*` classes, `backup.ts`/`tailoredCv.ts` blob-download,
  theme chips/`fitColorVar`, the seed + `Backfill…Async` machinery, `IEnrichmentBackfill`/`EnrichmentBackfillRunner`
  as the template for `IApplicationBackfill`.
- Keep `Domain` framework-free; async all the way; never add an external/AI call (Principle IV, SC-008).
- Commit per task or logical group; stop at any checkpoint to validate a story; run `/speckit-analyze` before
  `/speckit-implement`.
