# Phase 1 — Data Model: Application & Interview Process Tracking

**Feature**: `005-application-tracking` | **Date**: 2026-07-01 | **Spec**: [spec.md](./spec.md) ·
[research.md](./research.md)

All new persistence is **additive** (Principle IX): **one** append-only migration `ApplicationTracking`
creates **7 new tables**; **no existing table/column/migration is edited**. `offer_event` gains **three
new enum values** (string column — no schema change). New tables use **non-identity uuid PKs + safe
DDL defaults**, preserving the 003 `SchemaInvariantTests` no-serial invariant and OLDER→HEAD restore.

---

## 1. Strongly-typed IDs (Domain/Common/Ids, Principle II)

The `JobApplication` root reuses **`OfferId`** as its key (satellite pattern, like `OfferFit` /
`tailored_cv`). Six new wrapped ids (`readonly record struct` over `Guid`, `New()`/`From()`), each with
a `ValueConverter` in `StronglyTypedIdConverters.cs` and one line in `AppDbContext.ConfigureConventions`:

| New id | On table |
|--------|----------|
| `PipelineStageId` | `pipeline_stage` |
| `ApplicationNoteId` | `application_note` |
| `ApplicationTaskId` | `application_task` |
| `ApplicationDocumentId` | `application_document` |
| `ApplicationCommunicationId` | `application_communication` |
| `ApplicationInterviewId` | `application_interview` |

## 2. Enums (Domain/Applications)

- **`ApplicationStatus { Active, Closed }`** — stored `HasConversion<string>` varchar(20).
- **`ApplicationOutcome { Accepted, Rejected, Withdrawn, NoResponse }`** — nullable varchar(20); set
  only while `Closed`.
- **`CommunicationDirection { Inbound, Outbound }`** — varchar(20).
- **`OfferEventType`** (existing enum, Domain/Offers) gains **`ApplicationStageChanged`**,
  **`ApplicationClosed`**, **`ApplicationReopened`** — no migration (the column is varchar(40)).

`Kind` (interview) and `Channel` (communication) are **free-text** (user flexibility, no fixed catalog).

## 3. Domain aggregate & entities (framework-free)

### `JobApplication` (aggregate root; key `OfferId`)

| Property | Type | Notes |
|----------|------|-------|
| `OfferId` | `OfferId` | PK = FK → `offers(id)` cascade |
| `CurrentStageId` | `PipelineStageId` | FK → `pipeline_stage` RESTRICT; always references an existing stage |
| `Status` | `ApplicationStatus` | `Active` \| `Closed` |
| `Outcome` | `ApplicationOutcome?` | non-null iff `Closed` |
| `AppliedAt` | `DateTimeOffset?` | copied from `Offer.AppliedAt` on creation |
| `ClosedAt` | `DateTimeOffset?` | set on `Close`, cleared on `Reopen` |
| `CreatedAt` / `UpdatedAt` | `DateTimeOffset` | |

**Behavior (all return `Result`, validate in-aggregate — Principle III):**

- `Create(offerId, firstStageId, appliedAt, now)` → new `Active` application at the first stage.
- `MoveToStage(stageId, now)` → free movement; allowed only while `Active` (reopen a closed one first).
  The **service** confirms `stageId` exists before calling; the aggregate records the transition.
- `Close(outcome, now)` → requires `Active`; sets `Closed` + `Outcome` + `ClosedAt`. Rejects a second close.
- `Reopen(now)` → requires `Closed`; back to `Active` (stage retained), clears `Outcome`/`ClosedAt`.

Stage change / close / reopen are appended to the **existing `offer_event`** log by the service
(`ApplicationStageChanged` / `ApplicationClosed` / `ApplicationReopened`, jsonb payload) — the
append-only history (FR-003). The aggregate holds no EF child collections (lean-root idiom, R1).

### `PipelineStage` (entity)

`Id`, `Name` (≤ 80), `Position` (int), `CreatedAt`. Methods: `Create`, `Rename(name)`, `MoveTo(position)`.
The ordered set is the pipeline (FR-019). Managed via `IPipelineStageRepository`.

### Interview-data entities (each its own table, R4)

| Entity | Key fields | Mutability |
|--------|-----------|------------|
| `ApplicationNote` | `Body` (text), `CreatedAt` | **append-only** (immutable; FR-006) |
| `ApplicationTask` | `Title` (≤300), `Description?`, `DueAt?`, `CompletedAt?`, `CreatedAt` | mutable: `Complete(now)` / `Reopen()` / `Edit(...)`; **overdue** = `DueAt < now && CompletedAt == null` (derived) |
| `ApplicationDocument` | `StoredFileName`, `OriginalFileName`, `ContentType?`, `SizeBytes`, `AddedAt` | add / remove; bytes on disk (§6) |
| `ApplicationCommunication` | `OccurredAt`, `Direction`, `Channel`, `Summary` (text), `CreatedAt` | logged interaction |
| `ApplicationInterview` | `Kind`, `ScheduledAt?`, `Interviewer?`, `Outcome?` (text), `Notes?`, `CreatedAt` | mutable: `RecordOutcome(...)` / `Edit(...)`; **upcoming** = `ScheduledAt > now` (derived) |

All carry `OfferId` (FK → `application(offer_id)` cascade). Validation (lengths, non-empty title/summary)
lives in the entity factory (Principle III).

## 4. Tables (migration `ApplicationTracking`)

snake_case columns; uuid PKs `ValueGeneratedNever`; timestamptz; text via `HasColumnType`. FK/cascade as noted.

```
pipeline_stage            (id PK, name varchar(80), position int, created_at)                       — index(position)
application               (offer_id PK/FK→offers CASCADE, current_stage_id FK→pipeline_stage RESTRICT,
                           status varchar(20), outcome varchar(20) NULL, applied_at NULL,
                           closed_at NULL, created_at, updated_at)                                   — index(current_stage_id), index(status)
application_note          (id PK, offer_id FK→application CASCADE, body text, created_at)            — index(offer_id)
application_task          (id PK, offer_id FK→application CASCADE, title varchar(300),
                           description text NULL, due_at NULL, completed_at NULL, created_at)         — index(offer_id), index(offer_id, completed_at)
application_document      (id PK, offer_id FK→application CASCADE, stored_file_name varchar(200),
                           original_file_name varchar(400), content_type varchar(200) NULL,
                           size_bytes bigint, added_at)                                              — index(offer_id)
application_communication (id PK, offer_id FK→application CASCADE, occurred_at, direction varchar(20),
                           channel varchar(80), summary text, created_at)                            — index(offer_id)
application_interview     (id PK, offer_id FK→application CASCADE, kind varchar(80), scheduled_at NULL,
                           interviewer varchar(200) NULL, outcome text NULL, notes text NULL, created_at) — index(offer_id), index(scheduled_at)
```

`application(offer_id)` FK → `offers(id)` cascade; the five child FKs → `application(offer_id)` cascade.
Deleting an offer cascades to its application and the whole subtree; 003 restore `TRUNCATE … CASCADE`
covers them. `application_document` rows reference **flat files in `cv-data`** (§6), not bytes.

## 5. State machine (FR-002 / FR-003 / SC-004)

```
                 apply (MarkApplied → create)        close(outcome)
   (no application) ───────────────────────▶ Active ───────────────▶ Closed
                                              │  ▲                      │
                          MoveToStage(any) ◀──┘  └────── reopen ────────┘
   Stage ∈ user-defined pipeline_stage (free movement while Active).
   Outcome ∈ {Accepted, Rejected, Withdrawn, NoResponse} (only while Closed).
   Invalid: close-when-closed, reopen-when-active, stage ∉ pipeline, close without outcome — all Result errors.
```

## 6. Document file storage (R6, mirrors 004 ADR-3)

`IApplicationDocumentFileStore` (Infrastructure, **singleton** like `LocalCvFileStore` /
`LocalTailoredCvFileStore`) resolves the **same** directory `Cv:StoragePath ?? {AppContext.BaseDirectory}/
cv-data` and stores each attachment **flat** as `appdoc-{ApplicationDocumentId:N}{ext}`. Because 003's
`LocalCvFileStore.EnumerateAll()` is top-level and its `StageSwap` replaces the whole `cv-data` dir
atomically, these flat files are **backed up and restored with zero changes to 003**. Distinct prefixes
(`{cvId}` / `tailored-` / `appdoc-`) avoid collisions. Upload cap **50 MB/file** (`MaxDocumentBytes =
50 * 1024 * 1024` = 52,428,800 bytes), **any type** (clarification #4); over-limit ⇒ a `FileTooLarge`
`Result` error. Download streams the file over the UI endpoint. Files gitignored (IV).

## 7. Backup inclusion (R7 — `BackupTables.InsertOrder`)

Add the 7 tables in FK/dependency order; the existing **`BackupTablesCompletenessTests`** guard (model
tables == `InsertOrder`) fails until all are present:

- **Tier 1** (independent root, before `offers`): `pipeline_stage`.
- **Tier 3** (after `offers` **and** `pipeline_stage`): `application`.
- **Tier 4** (new; after `application`): `application_note`, `application_task`, `application_document`,
  `application_communication`, `application_interview`.

The COPY snapshot/restore is catalog-driven, so this list entry + the guard are the **only** backup
changes (no snapshot/zip/swap edits — 003/004 pattern).

## 8. No-data-loss seed + backfill (R5 — the load-bearing mechanism)

- **Seed** (`DatabaseSeeder.SeedPipelineStagesAsync`, **seed-if-empty**): if `pipeline_stage` has zero
  rows, insert defaults **Applied(0) → Screening(1) → Interviewing(2) → Offer(3)**. Table-level guard
  (not per-id) so a user who deletes a default stage does **not** have it resurrected on restart.
- **Backfill** (`DatabaseInitializer.BackfillApplicationsAsync`, idempotent): ensure stages exist, then
  for every `Offer` with `Applied == true` and no `application` row → create one at the **lowest-position
  stage**, `Active`, `AppliedAt = offer.AppliedAt`; if `offer.ApplicationNote` is set and the journal is
  empty → seed the first `application_note` (`CreatedAt = AppliedAt ?? now`). Re-running only fills gaps.
- **Wired on both paths**: `DatabaseInitializer.InitializeAsync` (after `SeedAsync` + enrichment backfill)
  for **in-place upgrade**; and a new **`IApplicationBackfill` / `ApplicationBackfillRunner`** (mirroring
  `IEnrichmentBackfill` / `EnrichmentBackfillRunner`) called from **`RestoreService`** after the
  enrichment backfill on an **`Older`** restore — because 003's restore `TRUNCATE`s the full HEAD
  `InsertOrder` (confirmed in `PostgresSnapshotStore.RestoreAsync`), a pre-005 backup lands with empty
  application tables that this backfill reconstructs from the restored applied offers. **SC-001 & SC-007.**

## 9. Read models (Application layer)

- **`ApplicationBoard`** (FR-004/FR-009/SC-002): `stages` (ordered) each with active `ApplicationCard`s +
  a `closed` list grouped by outcome. `ApplicationCard { offerId, title, company, stageId, status,
  outcome?, appliedAt?, outstandingTaskCount, overdueTaskCount, nextInterviewAt? }`.
- **`ApplicationDetail`** (FR-007): stage/status/outcome + the merged **timeline** + notes/tasks/documents/
  communications/interviews collections.
- **`ApplicationTimelineEntry`** (FR-007, **derived — no timeline table**): a union of the application's
  `offer_event` rows (`ApplicationStageChanged`/`Closed`/`Reopened`) + notes + communications + interviews
  (by `ScheduledAt`) + task added/completed + document added, projected to
  `{ occurredAt, kind, title, detail }` and ordered by `occurredAt`.

## 10. Export additions (R9 / FR-018 — `OfferExport`)

Add to the positional `OfferExport` record (additive; keep "captured facts only"): `ApplicationStage`
(string?), `ApplicationStatus` (string?), `ApplicationOutcome` (string?), and a compact
`IReadOnlyList<InterviewEventExport>` (new record: `Kind`, `ScheduledAt?`, `Outcome?`). `ExportReader`
reads stage/status/outcome off the `application` row (join by offer id) and projects the interviews;
`ExportService` adds the three scalar CSV columns + a joined interview column (via the existing
`string.Join(" | ", …)` / `FormatBand` idiom); JSON serializes automatically.

## 11. Touch-list (all additive)

**New**: `Domain/Applications/*` (aggregate, entities, enums); `Application/Applications/*` (ports:
`IApplicationRepository`, `IPipelineStageRepository`, `IApplicationDocumentFileStore`, `IApplicationBackfill`;
services: `ApplicationTrackingService`, `PipelineStageService`; contracts/DTOs + read models);
`Infrastructure/Persistence/Configurations/*` (7 configs); the `ApplicationTracking` migration (+designer
+snapshot); `Infrastructure/Persistence/Repositories/*` (repos + `ApplicationBackfillRunner`);
`Infrastructure/Applications/LocalApplicationDocumentFileStore`; `Web/Endpoints/ApplicationEndpoints`;
6 ids + 6 converters. **Edited (additive only)**: `OfferEventType` (+3 values); `SetOfferApplication`
(create-on-apply + clear-guard); `OfferEndpoints` (clear → 409 steer-to-close mapping); `BackupTables.InsertOrder`
(+7); `DatabaseSeeder`/`DatabaseInitializer` (seed + backfill); `RestoreService` (+application backfill);
`AppDbContext` (+7 DbSets, +6 converters); `Application`/`Infrastructure` DI; `FeatureEndpoints`
(+`MapApplicationEndpoints`); `OfferExport`/`ExportReader`/`ExportService` (+application fields). Frontend
delta in [plan.md](./plan.md).
