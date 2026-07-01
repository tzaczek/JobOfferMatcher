# Implementation Plan: Application & Interview Process Tracking

**Branch**: `005-application-tracking` (spec directory; no git branch — repo has no `before_*` hook)
| **Date**: 2026-07-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/005-application-tracking/spec.md` (with Clarifications
Session 2026-07-01: **(1)** user-defined pipeline stages; **(2)** active/closed + fixed outcome is a
separate dimension from stages, free stage movement; **(3)** clearing "applied" prefers closing over
erasing; **(4)** attachments any type ≤ ~50 MB).

**Constitution**: `.specify/memory/constitution.md` v1.1.0 — **not amended**. This feature realises the
constitution's stated **core scope** (application tracking) and its Principle-III lifecycle. The
NON-NEGOTIABLE Principles III, IV and Principle IX are the load-bearing ones here; feature decisions are
recorded as ADR-1..ADR-5 (Principle XI).

## Summary

Turn the existing binary per-offer **"applied" flag** into a first-class **application** the user can
drive through a **user-configurable interview pipeline**, close with a **fixed outcome**, and enrich with
**notes, interview tasks, documents, communications, and interviews** — all viewable as one **pipeline
board** and one **per-application timeline** — **without losing a single byte of existing data**.

Technical approach (from [research.md](./research.md), grounded in the live 001–004 code read during
Phase 0):

- **A `JobApplication` satellite aggregate** (PK `OfferId` = FK → `offers(id)` cascade), exactly the
  `OfferFit` / `tailored_cv` pattern (ADR-1). The lean `Offer` root and its `Applied`/`AppliedAt`/
  `ApplicationNote` columns are **unchanged** (the entry gate). The application carries `CurrentStageId`
  (FK → a new user-editable `pipeline_stage`), `Status` (Active/Closed) + `Outcome`
  (Accepted/Rejected/Withdrawn/NoResponse) — a **separate fixed dimension** from stages (clarifications
  #1/#2). Movement between stages is free; closing requires an outcome; a closed application reopens.
- **Five typed child tables** for the interview data — `application_note` (append-only journal),
  `application_task` (mutable, overdue derived), `application_document` (file metadata), `application_
  communication`, `application_interview` (mutable, upcoming derived) — while **stage changes / close /
  reopen reuse the existing append-only `offer_event`** log (three new enum values, no migration), and the
  **timeline is a derived read model** (a union, **no timeline table, no dual-write**) (ADR-2). Each table
  maps 1:1 to an explicit FR.
- **No data lost, on both paths that matter** (ADR-3, the headline): **one** append-only migration adds
  the 7 tables (schema only); an **idempotent seed** (default stages, seed-if-empty) + **idempotent
  backfill** (`BackfillApplicationsAsync`: an application at the first stage for every applied offer, the
  legacy note as its first journal entry) runs at **startup** (in-place upgrade) **and** — via a new
  `IApplicationBackfill` mirroring 003's `IEnrichmentBackfill` — inside **`RestoreService`** on an
  **older-backup** restore (because 003's restore `TRUNCATE`s the full HEAD table list, confirmed in
  `PostgresSnapshotStore`). Nothing existing is dropped, overwritten, or edited (Principle IX).
- **Documents as flat files in the `cv-data` root** (`appdoc-{id:N}{ext}`) so **003 backs them up for
  free** — 003's top-level `EnumerateAll()` + atomic `StageSwap` cover them with **zero changes**, exactly
  004's flat-file reasoning; ≤ ~50 MB, any type (ADR-4).
- **Backup coverage** = add the 7 tables to `BackupTables.InsertOrder` (FK order) — the **only** DB-backup
  change (the COPY snapshot is catalog-driven); the existing **`BackupTablesCompletenessTests`** guard
  fails until they're all listed, permanently protecting FR-014 (ADR-4). Application write paths reuse the
  **`MaintenanceGate`** so a restore's `TRUNCATE` is consistent.
- **Clearing "applied" prefers closing over erasing** (ADR-5): the un-apply path is guarded to a 409 that
  steers the user to *close (Withdrawn)* when history exists; permanent deletion is a separate confirmed
  action, backup-recoverable (clarification #3).
- **UI**: a new **Applications** page (pipeline board — columns per stage, active cards + a closed
  section by outcome, overdue/outstanding-task indicators); an **application detail** surface (timeline +
  notes/tasks/documents/communications/interviews) reusing the `ApplyModal` portal/focus-trap idiom; a
  **Settings → Pipeline stages** section (add/rename/reorder/remove); the offer card gains a stage chip +
  link when applied. Export gains application fields (FR-018). All reuse existing theme tokens/chips/buttons.

Full design: [research.md](./research.md), [data-model.md](./data-model.md),
[contracts/applications-api.md](./contracts/applications-api.md), [quickstart.md](./quickstart.md).

## Technical Context

**Language/Version**: C# 14 on **.NET 10** (backend); **TypeScript + React 19** via Vite (frontend).
Unchanged from 001–004.

**Primary Dependencies**:
- Backend: ASP.NET Core 10 minimal APIs; EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL`. **Added:
  none** — no new NuGet package; no AI SDK (this feature makes **no** external call of any kind).
- Frontend: React 19, Vite, TypeScript, the hand-rolled typed `fetch` client (`api/client.ts`), the
  `ApplyModal` portal/focus-trap pattern, the `backup.ts`/`tailoredCv.ts` blob-download pattern, central
  design tokens + `theme/index.ts` helpers. **Added: none.**

**Storage**: PostgreSQL via EF Core, **append-only** migrations at startup (`MigrateAsync`). **One** new
migration `ApplicationTracking` — 7 tables, non-identity uuid PKs, safe DDL defaults. Document files are
**flat files in the existing `cv-data` directory** (gitignored) — no new directory, no new config key.

**Testing**: xUnit unit tests (Domain `JobApplication` state machine + task/interview/note logic +
pipeline ordering; Application `ApplicationTrackingService` create-on-apply / close / reopen / clear-guard
/ delete-confirm, backfill idempotency). **Real-PostgreSQL** integration via Testcontainers (Principle V):
7-table + jsonb round-trip; the timeline read-model ordering; backup/restore round-trip incl. the new
tables + flat document files; the extended **`BackupTablesCompletenessTests`**; the **no-data-loss backfill**
on upgrade **and** on older-restore; the stage-delete guard; document 50 MB reject; export includes
application fields; `SchemaInvariantTests` (no-serial) over the new tables. The untouched 001–004 suites
are the **FR-016 regression contract**. Frontend: Vitest + RTL (`frontend/tests/applications/`) for the
board, detail/timeline, tasks/overdue, documents, the stage-config section, the offer-card stage
indicator, and the applications client.

**Target Platform**: local-first, single-user; Windows 11 dev. Foreground ASP.NET Core on
`localhost:5180` serving the SPA. No worker/external process for this feature.

**Performance Goals**: not latency-bound — single user, tens of applications. The board and timeline are
simple indexed reads/unions; overdue/upcoming are derived on read. No background work is added.

**Constraints**: local-first; **no external service and no external call** (0 records transmitted — SC-008);
personal interview data (notes, recruiter names, documents) stays in Postgres + gitignored `cv-data`
(Principle IV); **append-only** migration, **no existing data dropped/edited** (Principle IX); async all
the way; nullable on, warnings-as-errors in Domain + Application.

**Scale/Scope**: 1 user; applications exist only for offers the user applied to (tens, not the full feed);
5 user stories; 7 new tables; one migration; one new Web endpoint group; a page + detail surface + a
settings section.

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design. All gates PASS — no violations.
This feature realises the constitution's core scope (application tracking) and Principle-III lifecycle.
Feature decisions are ADR-1..ADR-5 (Principle XI).*

| # | Principle | Status | How this plan satisfies it |
|---|-----------|--------|----------------------------|
| I | Layered architecture, deps inward | ✅ PASS | New `Domain/Applications` (framework-free aggregate + entities + enums + pure overdue/upcoming logic); ports (`IApplicationRepository`, `IPipelineStageRepository`, `IApplicationDocumentFileStore`, `IApplicationBackfill`) + `ApplicationTrackingService`/`PipelineStageService` in **Application**; EF configs, repos, file store, endpoints in **Infrastructure/Web**. Commands return `Result`; board/detail are read-only. No MediatR (YAGNI). |
| II | Strongly-typed domain, no primitive obsession | ✅ PASS | `OfferId` as satellite key + 6 new wrapped ids; `ApplicationStatus`/`ApplicationOutcome`/`CommunicationDirection` enums (the constitution names `ApplicationStatus`); no raw `Guid`/`int` in Domain/Application. `Result<T>` for expected failures. |
| III | The Tracker Reflects Reality (NON-NEG) | ✅ PASS | A **defined lifecycle** (user-configurable stages + fixed outcomes) with invalid transitions rejected **in the aggregate** (`Result`): a stage must reference an existing `pipeline_stage`; close requires an outcome; no double-close/reopen-active. **Append-only history** via `offer_event` + append-only notes. No fabricated/demo records persisted. |
| IV | Personal Data Private & Local (NON-NEG) | ✅ PASS | **No external call, no AI, 0 records transmitted** (SC-008). Notes, recruiter/interviewer names, communication summaries live in local Postgres; documents live in gitignored `cv-data`. The new `/api/applications/*` is UI-local (same model as `/api/offers/*`); no data leaves the machine. |
| V | Real Database in Tests | ✅ PASS | All 7 tables, jsonb, the timeline union, the backup/restore round-trip, and **both** backfill paths are tested on **real PostgreSQL** (Testcontainers). No mocked DB. |
| VI | Green Before Done (NON-NEG) | ✅ PASS (process) | Each user story closes only on a green local suite; the untouched 001–004 suites are the FR-016 regression contract. |
| VII | UI Changes Require Visual Verification | ✅ PASS | The Applications board, the detail/timeline surface, the Settings pipeline-stage editor, and the offer-card stage indicator are run-and-looked-at (per `quickstart.md`) before "done". |
| VIII | One Source of Design Truth | ✅ PASS | Reuse `.btn`/`.btn--primary`/`.btn--ghost`/`.btn--danger`, `.chip*`, the `ApplyModal` portal + focus-trap + `.modal*` classes, and `theme/index.ts` helpers (`statusChipClass`, `fitColorVar`). Any stage/outcome chip colors are added as **token pairs** in `tokens.css` (light+dark) + a `.chip--*` in `base.css` — never scattered literals. |
| IX | Your Data Is Recoverable | ✅ PASS | **ONE** append-only migration; **no** prior migration/column edited; `offer.ApplicationNote` + all `offer_event` rows retained. **No-data-loss backfill** runs on **upgrade and on older-restore**. The 7 tables + flat document files are in **003 backup/restore**, guarded by the completeness test. Export gains application state + interview history (portable). Permanent delete is explicit + backup-recoverable. |
| X | Simple by Default (YAGNI) | ✅ PASS | **No new dependency**; reuse `offer_event` (no `application_event` table), **derive** the timeline (no timeline table, no dual-write), **flat `cv-data`** files (no 003 file changes), reuse the **seed+backfill** machinery, reuse `MaintenanceGate`, reuse `ApplyModal`/theme. The 7 tables are the feature's inherent shape (5 distinct entities named in the spec) — see Complexity Tracking. |
| XI | Documented Decisions, Immutable History | ✅ PASS | ADR-1..ADR-5 below. Conventional Commits, one logical change each; no `--no-verify`, no history rewrite. |

**Decisions (ADR-style, per Principle XI):**

- **ADR-1 — A `JobApplication` satellite (PK `OfferId`) with a user-configurable `pipeline_stage` and a
  separate active/closed+outcome dimension.** *Context*: an application is 1:1 with an offer and begins at
  "applied"; stages are user-defined; outcome is fixed (clarifications #1/#2). *Decision*: a satellite
  aggregate keyed by `OfferId` (like `OfferFit`/`tailored_cv`); `CurrentStageId` → a global editable
  `pipeline_stage`; `Status`+`Outcome` enums separate from stages; free stage movement; close/reopen.
  *Rationale*: matches the proven lean-root + satellite idiom, gives 1:1-per-offer and cascade-from-offers
  for free, and keeps outcomes reportable regardless of stage names. *Rejected*: extending `Offer` (bloats
  the root, loads interview data with every feed query); a fixed stage enum (contradicts the clarification).

- **ADR-2 — Five typed child tables; history via the existing `offer_event`; the timeline is derived.**
  *Context*: the spec names five distinct interview-data kinds with different fields and mutability; FR-007
  wants one chronological timeline. *Decision*: `application_note/task/document/communication/interview`
  each as its own table; stage-change/close/reopen appended to `offer_event` (three new enum values, no
  migration); the timeline is a **union read model** (no dedicated table, no dual-write). *Rationale*: each
  table maps to an explicit FR; reusing `offer_event` keeps append-only history in one place (III/VIII) and
  avoids a sixth table; deriving the timeline avoids drift (X). *Rejected*: one generic
  `application_event(kind, jsonb)` table (forces mutable tasks/interviews into an append-only log, weakens
  typing); a materialized timeline table (dual-write for no benefit).

- **ADR-3 — No data lost via idempotent seed + backfill at startup AND on older-restore (mirror 002/003).**
  *Context*: the explicit "no data lost" requirement must hold on **both** an in-place upgrade and a
  restore of a pre-005 backup; 003's restore `TRUNCATE`s the full HEAD table list (confirmed in
  `PostgresSnapshotStore.RestoreAsync`). *Decision*: schema-only migration; `SeedPipelineStagesAsync`
  (seed-if-empty) + idempotent `BackfillApplicationsAsync` (application at first stage per applied offer,
  legacy note → first journal entry) wired into `DatabaseInitializer` (upgrade) and, via a new
  `IApplicationBackfill`/`ApplicationBackfillRunner`, into `RestoreService` after the enrichment backfill
  for `Older` restores. `offer.ApplicationNote` + `offer_event` retained. *Rationale*: reuses the app's
  proven backfill machinery so the guarantee holds on the restore path too, where a migration-SQL backfill
  could not run. *Rejected*: backfill in the migration `Up()` (never runs on restored data); lazy
  create-on-view (invariant unenforced, board incomplete); dropping the legacy note column (destructive).

- **ADR-4 — Documents flat in `cv-data`; +7 tables in `BackupTables.InsertOrder` + the completeness guard;
  reuse `MaintenanceGate`.** *Context*: 003's `EnumerateAll()` is top-level only and its restore
  `TRUNCATE`s `InsertOrder`; the COPY snapshot is catalog-driven. *Decision*: store attachments as flat
  `appdoc-{id:N}{ext}` in the `cv-data` root (covered by 003's zip/swap unchanged, à la 004 ADR-3); add the
  7 tables to `InsertOrder` (FK order) — the only backup change; the existing completeness test enforces it;
  application write paths call `MaintenanceGate.WaitWhileActiveAsync`. *Rationale*: minimal, reuses 003/004
  exactly; ≤ ~50 MB any type (clarification #4). *Rejected*: a `cv-data/app-docs/` subfolder (silently
  dropped from backups); `bytea` in Postgres (bloats the snapshot).

- **ADR-5 — Clearing "applied" prefers closing over erasing; permanent delete is explicit.** *Context*:
  clarification #3. *Decision*: `SetOfferApplication.ClearAsync` returns 409 `ApplicationHasHistory` when
  interview data exists (UI steers to *close as Withdrawn*); `MarkAppliedAsync` creates the application at
  the first stage if absent; a separate `DELETE /applications/{offerId}?confirm=true` permanently removes
  the subtree, recoverable from a prior backup. *Rationale*: encodes the clarification as a domain/service
  rule so history is never lost by an accidental un-apply. *Rejected*: silent cascade-delete on un-apply
  (data loss); blocking un-apply entirely (too rigid).

## Project Structure

### Documentation (this feature)

```text
specs/005-application-tracking/
├── plan.md              # This file
├── research.md          # Phase 0 — R1..R10 decisions, rationale, alternatives
├── data-model.md        # Phase 1 — aggregate + 7 tables, state machine, migration, backfill, backup, export
├── quickstart.md        # Phase 1 — run & per-user-story validation guide
├── contracts/
│   └── applications-api.md  # Phase 1 — REST contract (stages, board, detail/timeline, notes/tasks/docs/comms/interviews)
├── spec.md              # Feature spec (with Clarifications 2026-07-01)
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root) — delta over features 001–004

```text
backend/
├── src/
│   ├── Domain/
│   │   ├── Applications/                 # NEW: JobApplication (root, key OfferId), PipelineStage,
│   │   │                                  #      ApplicationNote/Task/Document/Communication/Interview,
│   │   │                                  #      ApplicationStatus/Outcome/CommunicationDirection enums
│   │   ├── Offers/OfferEvent.cs          # EDIT: +ApplicationStageChanged/Closed/Reopened enum values (no migration)
│   │   └── Common/Ids/Ids.cs             # EDIT: +PipelineStageId + 5 child ids
│   ├── Application/
│   │   ├── Applications/                 # NEW: IApplicationRepository, IPipelineStageRepository,
│   │   │                                  #      IApplicationDocumentFileStore, IApplicationBackfill (ports);
│   │   │                                  #      ApplicationTrackingService, PipelineStageService;
│   │   │                                  #      contracts/DTOs + ApplicationBoard/Detail/Timeline read models
│   │   ├── Offers/SetOfferApplication.cs # EDIT: create application on apply; clear-applied → steer-to-close guard (ADR-5)
│   │   ├── Backup/BackupTables.cs        # EDIT: +7 tables in InsertOrder (pipeline_stage T1; application T3; children T4)
│   │   ├── Backup/RestoreService.cs      # EDIT: +IApplicationBackfill.RunAsync after enrichment backfill (Older)
│   │   ├── Export/OfferExport.cs         # EDIT: +application stage/status/outcome + InterviewEventExport list
│   │   └── DependencyInjection.cs        # EDIT: +ApplicationTrackingService, PipelineStageService
│   ├── Infrastructure/
│   │   ├── Persistence/Configurations/   # NEW: 7 IEntityTypeConfiguration (tables, FKs, indexes, string enums)
│   │   ├── Persistence/Migrations/       # NEW: 2026XXXX_ApplicationTracking.cs (+designer +snapshot) — 7 tables, safe defaults
│   │   ├── Persistence/AppDbContext.cs   # EDIT: +7 DbSet<>; +6 id converters in ConfigureConventions
│   │   ├── Persistence/Converters/StronglyTypedIdConverters.cs # EDIT: +6 converters
│   │   ├── Persistence/Repositories/     # NEW: ApplicationRepository, PipelineStageRepository, ApplicationBackfillRunner
│   │   ├── Persistence/Seed/DatabaseSeeder.cs        # EDIT: +SeedPipelineStagesAsync (seed-if-empty)
│   │   ├── Persistence/DatabaseInitializer.cs        # EDIT: +BackfillApplicationsAsync (upgrade path)
│   │   ├── Persistence/Repositories/ExportReader.cs  # EDIT: project application fields + interview history
│   │   ├── Applications/                 # NEW: LocalApplicationDocumentFileStore (flat appdoc-{id:N}{ext} in cv-data)
│   │   └── DependencyInjection.cs        # EDIT: +repos, +IApplicationDocumentFileStore (singleton), +IApplicationBackfill
│   ├── Application/Export/ExportService.cs # EDIT: +3 CSV columns + joined interview column (JSON automatic)
│   └── Web/
│       └── Endpoints/                    # NEW: ApplicationEndpoints (stages, board, detail, stage/close/reopen,
│                                          #      notes, tasks, documents up/down, communications, interviews);
│                                          #      OfferEndpoints.cs EDIT (clear → 409 mapping);
│                                          #      FeatureEndpoints.cs EDIT (+MapApplicationEndpoints)
└── tests/
    ├── Domain.Tests/                     # JobApplication state machine; task overdue; note append-only; stage ordering
    ├── Application.Tests/                # ApplicationTrackingService (apply→create, clear-guard, close/reopen, delete-confirm);
    │                                      #   backfill idempotency
    └── Infrastructure.Tests/             # real Postgres: 7-table + jsonb round-trip; timeline ordering; backup/restore incl.
                                           #   new tables + flat docs; BackupTablesCompletenessTests (now +7); no-data-loss
                                           #   backfill on upgrade AND older-restore; stage-delete guard; 50MB reject; export fields

frontend/
└── src/
    ├── api/
    │   ├── applications.ts               # NEW: stages CRUD, getBoard, getApplication, moveStage, close, reopen,
    │   │                                  #      addNote/Task/Communication/Interview, task/interview edit+delete,
    │   │                                  #      uploadDocument, downloadDocument (blob→<a download>, mirrors backup.ts), deleteApplication
    │   └── types.ts                       # EDIT: +ApplicationStatus/Outcome unions, +Board/Card/Detail/Timeline/Note/Task/Document/
    │                                      #      Communication/Interview/PipelineStage DTOs (mirror TailoredCvDto style)
    ├── pages/
    │   └── Applications/                  # NEW: ApplicationsPage.tsx (+.css) — pipeline board (columns + closed section)
    ├── components/
    │   ├── ApplicationDrawer/            # NEW: the detail surface (timeline + notes/tasks/documents/communications/interviews),
    │   │                                  #      reusing the ApplyModal portal/focus-trap idiom; small add-forms
    │   └── OfferCard/OfferCard.tsx        # EDIT: when applied, show current-stage chip + link to the application
    ├── pages/Settings/
    │   ├── PipelineStagesSection.tsx     # NEW: add/rename/reorder/remove stages (self-contained settings-card section)
    │   └── SettingsPage.tsx              # EDIT: compose <PipelineStagesSection/>
    ├── App.tsx                            # EDIT: +{ to:'/applications', label:'Applications' } in NAV; +import + <Route>
    └── theme/                            # reuse chips/buttons/helpers; add stage/outcome chip token pairs (light+dark) if needed
```

**Structure Decision**: Web-application layout, unchanged. The feature is **additive**: a new
`Domain/Applications` slice, a new `Application/Applications` slice with ports implemented in
`Infrastructure`, one new migration (7 tables), one UI-local Web endpoint group, and a UI surface (one
page + a detail drawer + a settings section + an offer-card indicator). Every edit to existing code is
**additive** (three `offer_event` enum values, `BackupTables`/DI/DbContext/seeder/initializer/restore/
export lines, the `SetOfferApplication` create-on-apply + clear-guard, the `OfferCard`/`App.tsx` UI).
001–004 behaviour is preserved; Domain stays framework-free; **no existing data is dropped or edited**.

## Phase Status

- [x] Phase 0 — Research (`research.md`): R1–R10 resolved against the live 001–004 code (satellite pattern,
  `offer_event`/backup/restore internals, seed+backfill machinery, file store); all `NEEDS CLARIFICATION`
  resolved (the spec's four clarifications fed the design directly).
- [x] Phase 1 — Design & Contracts (`data-model.md`, `contracts/applications-api.md`, `quickstart.md`);
  agent context (CLAUDE.md active-feature pointer) updated to this feature.
- [ ] Phase 2 — Tasks (`/speckit-tasks`) — **not** produced by this command.

## Complexity Tracking

*No Constitution violations. One item is worth recording for transparency (Principle X):*

| Item | Why needed | Simpler alternative rejected because |
|------|-----------|--------------------------------------|
| **7 new tables** (`pipeline_stage`, `application`, + 5 child) | The spec names **five distinct** interview-data kinds (notes, tasks, documents, communications, interviews) with genuinely different fields and mutability, plus a configurable pipeline and the aggregate. Each table maps 1:1 to an explicit FR. | A single generic `application_event(kind, jsonb)` table forces mutable tasks/interviews into an append-only log they don't fit and abandons strong typing (Principle II). The count is already **minimized**: history reuses the existing `offer_event` (no 8th table) and the timeline is **derived** (no 9th table). |
| `SetOfferApplication` gains an `IApplicationRepository` dependency | Keeps the invariant "applied ⟹ application exists" tight and creates the application at the moment of apply (ADR-5). | Lazy create-on-view leaves the board incomplete and the invariant unenforced until each offer is opened. |
