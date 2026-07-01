# Phase 0 — Research: Application & Interview Process Tracking

**Feature**: `005-application-tracking` | **Date**: 2026-07-01 | **Spec**: [spec.md](./spec.md)

This feature turns the existing binary per-offer **"applied" flag** (`Offer.Applied` + `AppliedAt` +
one `ApplicationNote`, with an append-only `offer_event` timeline) into a first-class, tracked
**application** carrying a **user-configurable interview pipeline**, an **active/closed + outcome**
dimension, and the interview data the user asked for (notes, tasks, documents, communications,
interviews) — **without losing any existing data**. Every decision below is grounded in the live
001/002/003/004 code, read directly during Phase 0.

The four spec clarifications (2026-07-01) fed the design directly: **(1)** stages are user-defined;
**(2)** outcome is a separate fixed dimension, stage movement is free; **(3)** clearing "applied"
prefers closing over erasing; **(4)** attachments accept any type up to ~50 MB.

---

## R1 — A separate `JobApplication` satellite aggregate (PK = `OfferId`), not fields on `Offer`

- **Decision**: Model the application as a **satellite aggregate** keyed by `OfferId` (PK = FK →
  `offers(id)` ON DELETE CASCADE), exactly like `OfferFit` and `tailored_cv` (004). The lean `Offer`
  root is **not** extended with interview children. `Offer.Applied`/`AppliedAt`/`ApplicationNote`
  remain **unchanged** as the entry gate (FR-001, FR-016).
- **Rationale**: The codebase's established idiom is a lean denormalized `Offer` root with *separate*
  child tables (`offer_event`, `offer_observation`, `offer_version`, `offer_enrichment`, `offer_fit`)
  managed via `IOfferRepository.AddEventAsync`, never loaded as EF navigation collections on the root
  (`SetOfferApplication` appends events through the repository). One application per offer maps cleanly
  to an `OfferId` PK. Cascade from `offers` means the whole application subtree is covered by the same
  lifecycle and by 003 backup/restore.
- **Alternatives rejected**: *Extend `Offer`* — bloats the root, loads interview data with every feed
  query, and violates the lean-root idiom. *A surrogate `ApplicationId` root* — unnecessary; the
  satellite pattern (OfferId PK) is already proven twice and gives 1:1-per-offer for free.

## R2 — User-configurable pipeline: a global `pipeline_stage` table, seeded-if-empty, position-ordered

- **Decision**: A single global (single-user) `pipeline_stage` table — `PipelineStageId` PK, `Name`,
  `Position` (int), `CreatedAt`. The ordered set is the source of truth for "where am I". Default
  stages (**Applied → Screening → Interviewing → Offer**, per Principle III; *not* "Closed" — that is an
  outcome, R3) are **seeded idempotently when the table is empty** in `DatabaseSeeder.SeedAsync`
  (extending the existing startup seeder). A `JobApplication.CurrentStageId` FK → `pipeline_stage`
  uses **ON DELETE RESTRICT**; removing a stage that still holds applications is **rejected in the
  domain/service** unless a reassignment target is supplied (spec edge case).
- **Rationale**: Mirrors the existing idempotent seed + backfill pattern (`DatabaseSeeder`,
  `BackfillEnrichmentAsync`), so first-run works with zero setup and re-running is safe. Position-ordering
  (not a hardcoded enum) is what makes stages reorderable. Looking up "the first stage" by lowest
  `Position` (rather than a fixed GUID) keeps the backfill (R5) robust even after the user customizes
  their pipeline.
- **Alternatives rejected**: A fixed `ApplicationStage` **enum** — contradicts the clarification
  (user-defined). Per-offer stage lists — no requirement; single global pipeline is simpler (Principle X).
  Hard-deleting a referenced stage with cascade-nulling — would orphan "where am I" (violates FR-019).

## R3 — Active/closed + outcome is a **separate fixed dimension** from stages (enums)

- **Decision**: `JobApplication` has `Status` (`ApplicationStatus { Active, Closed }`) and, when closed,
  `Outcome` (`ApplicationOutcome { Accepted, Rejected, Withdrawn, NoResponse }`) + `ClosedAt`. Stage
  movement among pipeline stages is **unrestricted** (any → any). Closing requires an outcome; a closed
  application can be **reopened** (Status→Active, stage retained). Invalid states are rejected in the
  aggregate via `Result` (Principle III): a recorded `CurrentStageId` must reference an existing stage;
  closing without an outcome is refused.
- **Rationale**: Directly implements clarification #2. Keeping outcomes a small fixed vocabulary —
  independent of the user's stage names — is what keeps them consistent for the applications view (closed
  section grouped by outcome), the export (FR-018), and any future reporting. Uses `HasConversion<string>`
  storage exactly like `UserOfferStatus`/`AvailabilityStatus`. The constitution's Principle II explicitly
  anticipates an `ApplicationStatus` type.
- **Alternatives rejected**: *Outcomes as user stages* — loses a stable outcome vocabulary. *Strict linear
  transitions* — the clarification chose free movement; a correction (moving back) must stay possible
  (spec edge case).

## R4 — Five typed child tables; history via the **existing `offer_event`**; timeline **derived**, not stored

- **Decision**: Model the interview data as the shapes that match their behavior:
  - **Notes** → `application_note` (append-only journal; immutable rows).
  - **Tasks** → `application_task` (mutable: `DueAt?`, `CompletedAt?`; overdue is derived).
  - **Documents** → `application_document` (file metadata + a local file, R6).
  - **Communications** → `application_communication` (`OccurredAt`, `Direction`, `Channel`, `Summary`).
  - **Interviews** → `application_interview` (mutable: `Kind`, `ScheduledAt?`, `Interviewer?`, `Outcome?`).
  - **Stage changes / close / reopen** → **reuse `offer_event`** with three new `OfferEventType` values
    (`ApplicationStageChanged`, `ApplicationClosed`, `ApplicationReopened`) + a jsonb payload. `offer_event.type`
    is `varchar(40)` stored `HasConversion<string>`, so **new enum values need no migration**.
  - **Timeline (FR-007)** → a **derived read model** that unions the application-scoped `offer_event`
    rows + the five child tables (each already timestamped), ordered chronologically. **No dedicated
    timeline table, no dual-write.**
- **Rationale**: Each table maps 1:1 to an explicit FR and to a distinct UX; the five kinds have genuinely
  different fields and mutability (a task toggles done; an interview gains an outcome later; a note is
  immutable). Reusing `offer_event` for pure history keeps append-only history in the one place the
  codebase already uses for it (Principle III/VIII) and **avoids a sixth `application_event` table**.
  Deriving the timeline avoids redundant storage and the dual-write bug class (Principle X).
- **Alternatives rejected**: *One generic `application_event(kind, jsonb)` table for all five kinds* —
  collapses tasks (mutable done-state, queried for overdue) and interviews (mutable, scheduled) into an
  append-only log they don't fit, and weakens typing (Principle II). *Merge communications + interviews
  into one `contact` table* — saves one table but muddies two genuinely different UX surfaces
  (a logged past interaction vs. a scheduled round with an outcome); kept separate for clean typing.
  *A materialized timeline table* — dual-write + drift risk for zero benefit over a union read.

## R5 — No data lost: **idempotent seed + backfill** at startup **and** on older-restore (the load-bearing decision)

- **Decision**: Guarantee "no existing data lost" (FR-005/SC-001) with the exact 002/003 pattern:
  - **ONE** new append-only migration `ApplicationTracking` creates the 7 tables. It creates **schema
    only** — no data backfill in migration SQL (the codebase backfills in C# at startup, not in migrations).
  - A new idempotent **`BackfillApplicationsAsync`** (mirroring `BackfillEnrichmentAsync`): for every
    `Offer` with `Applied == true` and **no** `application` row, create one at the **lowest-position
    stage**, `Status = Active`, `AppliedAt = offer.AppliedAt`; and if `offer.ApplicationNote` is set and
    the journal is empty, seed the **first `application_note`** from it (`CreatedAt = AppliedAt ?? now`).
    Re-running only fills gaps.
  - Call it from **`DatabaseInitializer.InitializeAsync`** (after `SeedAsync` + `BackfillEnrichmentAsync`)
    — covers the **upgrade** of the user's existing DB.
  - Add a new **`IApplicationBackfill` / `ApplicationBackfillRunner`** port (mirroring
    `IEnrichmentBackfill` / `EnrichmentBackfillRunner`) and call it in **`RestoreService`** right after the
    enrichment backfill for an **`Older`** restore — covers restoring a **pre-005 backup** into HEAD.
  - `offer.ApplicationNote` and all `offer_event` rows are **retained** (never dropped/edited) — additive
    only (Principle IX). The journal is the go-forward notes surface; the legacy note becomes its first entry.
- **Rationale**: This is why "no data lost" holds on **both** paths that matter — upgrading in place and
  restoring an old backup — and it *reuses* the app's proven backfill machinery rather than inventing a
  migration-SQL data step (which couldn't run on restored data). Idempotency makes it safe to re-run and
  makes the satellite an invariant (one application per applied offer), exactly as enrichment is today.
- **Alternatives rejected**: *Backfill inside the migration's `Up()` SQL* — runs once at migrate time,
  **not** against data loaded by a later restore, so an older-backup restore would silently leave applied
  offers with no application (data effectively lost from the feature's view). *Lazy create-on-first-view*
  — leaves the invariant unenforced and the pipeline board incomplete until each offer is opened. *Dropping
  `offer.ApplicationNote` in favor of the journal* — a destructive, non-additive migration (violates IX).

## R6 — Documents: flat files in the `cv-data` root (003 backs them up for free), 50 MB cap

- **Decision**: Store each attachment as a **flat** file `appdoc-{ApplicationDocumentId:N}{ext}` in the
  **same `cv-data` root** (`Cv:StoragePath ?? {AppContext.BaseDirectory}/cv-data`) via a small
  `IApplicationDocumentFileStore`, mirroring 004's `LocalTailoredCvFileStore`. `application_document`
  stores metadata (stored name, original name, content type, size, added-at). Upload enforces a **~50 MB**
  per-file cap (any type; clarification #4) and rejects over-limit with a clear error. Download streams the
  file over the UI endpoint.
- **Rationale**: 003's `LocalCvFileStore.EnumerateAll()` is **top-level, non-recursive**, and its
  `StageSwap` replaces the whole `cv-data` directory atomically. **Flat** files in that root are therefore
  captured by the existing backup zip + atomic restore swap with **zero changes to 003's file handling** —
  exactly the reasoning 004 used (ADR-3). Distinct filename prefixes (`{cvId}` / `tailored-` / `appdoc-`)
  avoid collisions with CVs and tailored CVs. Files are gitignored (Principle IV) and recoverable
  (Principle IX). Kestrel's body limit is already raised to 4 GB globally in `Program.cs`.
- **Alternatives rejected**: A `cv-data/app-docs/` **subfolder** — silently dropped from every backup
  (003 enumerates top-level only) unless 003's archive/swap gains recursive/relative-path plumbing (more
  change, more risk). Storing bytes **in Postgres** (`bytea`) — bloats the logical COPY snapshot and the
  DB; the app already keeps large binaries (CVs) on disk. External links — the spec wants real local files.

## R7 — Backup inclusion: +7 tables in `BackupTables.InsertOrder`, guarded by the completeness test

- **Decision**: Add the 7 new tables to `BackupTables.InsertOrder` in FK/dependency order:
  `pipeline_stage` in **Tier 1** (independent root, before `offers`); `application` in **Tier 3** (after
  `offers` **and** `pipeline_stage`); `application_note`, `application_task`, `application_document`,
  `application_communication`, `application_interview` in a new **Tier 4** (after `application`). The
  existing **`BackupTablesCompletenessTests`** (004 T019 — model tables == `InsertOrder`) will **fail until
  all 7 are added**, which is the guard doing its job.
- **Rationale**: The Npgsql `COPY` snapshot/restore is catalog-driven (column list derived from the model),
  so backup inclusion needs **only** the ordered list entry per table plus the guard — no snapshot/zip/swap
  edits (003 ADR pattern, reaffirmed by 004). Dependency order lets restore replay `COPY … FROM` without
  disabling FK triggers. New tables use **non-identity uuid PKs + safe defaults**, preserving the
  `SchemaInvariantTests` no-serial invariant and OLDER→HEAD restore.
- **Alternatives rejected**: A separate backup stream for application data — re-implements 003.

## R8 — Clearing "applied": prefer closing over erasing; permanent delete is explicit

- **Decision**: When an offer's application has accumulated interview data, `SetOfferApplication.ClearAsync`
  (the existing un-apply path) is **guarded**: it returns a `Result` error (`ApplicationHasHistory`) that
  the UI turns into "close as Withdrawn instead", preserving history (clarification #3, FR-013). Clearing an
  application with **no** interview data proceeds (removes the empty application + flag). **Permanent
  deletion** is a **separate, explicit, confirmed** endpoint that removes the application subtree; it stays
  recoverable from a prior backup (Principle IX). Marking an offer applied (`MarkAppliedAsync`) **creates**
  the `JobApplication` at the first stage if absent (keeping "applied ⟹ application exists").
- **Rationale**: Encodes clarification #3 as a domain/service rule so history is never lost by an
  accidental un-apply, while still allowing genuine deletion. `MarkAppliedAsync` gaining an
  `IApplicationRepository` dependency is a small additive edit that keeps the invariant tight.
- **Alternatives rejected**: Silent cascade-delete on un-apply (data loss — violates III/IX). Blocking
  un-apply entirely (too rigid; deletion must remain possible when intended).

## R9 — Export (FR-018): add application state + a compact interview history to `OfferExport`

- **Decision**: Extend the human-readable export (`OfferExport` / `ExportReader` / `ExportService`) with
  each offer's **application state** (current stage name, status, outcome, applied date) and a **compact
  interview history** (stage-change events, note count / latest notes, task summary, interview list), so the
  tracked pursuit is portable (Principle IX). Additive fields only.
- **Rationale**: FR-018 requires the tracked pursuit to be portable, not trapped in one app version. The
  export already surfaces `applied`/`appliedAt`/`applicationNote`; extending it is the natural home.
  *(Exact `OfferExport` shape confirmed against the code before finalizing data-model.md.)*
- **Alternatives rejected**: A separate applications export — fragments the "everything about an offer" view.

## R10 — Reuse `MaintenanceGate` on application write paths (restore consistency)

- **Decision**: The application write methods consult the existing **`MaintenanceGate`**
  (`WaitWhileActiveAsync`) — the same defer pattern used by `EnrichmentService`/`TailoredCvService` write
  paths — so a restore's `TRUNCATE` + `COPY` of the application tables is consistent. **No change** to the
  gate itself.
- **Rationale**: 003 quiesces writers during restore via the gate; the new write paths must participate or
  a restore could race an in-flight application edit. Reuse, not new infrastructure (Principle X).

---

## Cross-cutting confirmations (from the live code)

- **Strongly-typed IDs** are `readonly record struct` over `Guid` with a converter registered in
  `AppDbContext.ConfigureConventions` (`StronglyTypedIdConverters.cs`). New IDs: `PipelineStageId`,
  `ApplicationNoteId`, `ApplicationTaskId`, `ApplicationDocumentId`, `ApplicationCommunicationId`,
  `ApplicationInterviewId` (the `JobApplication` root reuses `OfferId` as its key). 6 new converters +
  6 `ConfigureConventions` lines.
- **Migrations** run at startup via `MigrateAsync` (`DatabaseInitializer`), never `EnsureCreated`
  (Principle IX). One new migration `ApplicationTracking` (+ designer + snapshot).
- **Endpoints**: `/api/offers/*` are plain UI endpoints (not loopback-restricted; loopback is only for the
  002/003/004 worker/backup channels). The new `/api/applications/*` group follows the **offers** access
  model — it is UI-facing, single-user, localhost. Document upload/download stream over it.
- **Testing**: real-Postgres integration via Testcontainers (`PostgresFixture`/`PostgresCollection`);
  Domain/Application unit tests; frontend Vitest + RTL. The untouched 001/002/003/004 suites are the FR-016
  regression contract.

**All Phase-0 unknowns resolved — no `NEEDS CLARIFICATION` remain.** Proceed to Phase 1 design.
