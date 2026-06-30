---

description: "Task list for On-Demand Backup & Restore (003)"
---

# Tasks: On-Demand Backup & Restore

**Input**: Design documents from `specs/003-backup-restore/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/backup-api.md](./contracts/backup-api.md),
[quickstart.md](./quickstart.md)

**Tests**: INCLUDED — the constitution mandates real-PostgreSQL integration tests (Principle V) and the
plan enumerates round-trip / cross-version / all-or-nothing / guard tests. They are first-class tasks.

**Organization**: by user story (US1 backup = MVP, US2 restore, US3 inspect/verify) in priority order.
Each story is an independently testable increment. **No new EF migration / no new NuGet dependency**
(plan ADR-1, Principle IX/X) — backup/restore is additive behaviour over the existing 12 tables + `cv-data`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (Setup, Foundational, Polish have no story label)

## Path Conventions

Web app: `backend/src/{Domain,Application,Infrastructure,Web}/`, `backend/tests/`, `frontend/src/`.

---

## Phase 1: Setup

**Purpose**: prerequisites with no logic dependencies.

- [X] T001 Add the server-side safety-backup dir and downloadable archives to `.gitignore` — append `backups/` and `*backup*.zip` (Principle IV: archives hold PII) in `.gitignore`
- [X] T002 [P] Add a `.btn--danger` button variant (the destructive Restore button) to `frontend/src/theme/base.css` using the existing `--c-danger` / `--c-danger-bg` tokens (no literals — Principle VIII); mirror `.chip--missing`
- [X] T003 [P] Create the empty source folders `backend/src/Application/Backup/` and `backend/src/Infrastructure/Backup/` (real files created directly below)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: shared scaffolding every story depends on — the maintenance gate, manifest/ports, the
canonical table inventory, the loopback-only endpoint group, and the frontend module shell.

**⚠️ CRITICAL**: no user story work begins until this phase is complete.

- [X] T004 Create `MaintenanceGate` (process-wide singleton) in `backend/src/Application/Scanning/MaintenanceGate.cs` per data-model §6: `SemaphoreSlim(1,1)` `_slot`, `volatile bool _maintenanceActive`, `TryBeginBackup()/EndBackup()`, `TryBeginRestore()/EndRestore()`, `IsMaintenanceActive`
- [X] T005 Register `MaintenanceGate` as a singleton in `backend/src/Application/DependencyInjection.cs` (beside the existing `ScanConcurrencyGuard` registration)
- [X] T006 [P] Add `BackupManifest`, `BackupTable`, `CvFileEntry` immutable records in `backend/src/Application/Backup/BackupManifest.cs` per data-model §2 (camelCase JSON)
- [X] T007 [P] Define the canonical ordered 12-table inventory + restore order as a single static source `BackupTables` in `backend/src/Application/Backup/BackupTables.cs` (data-model §3) — consumed by snapshot, restore, and tests
- [X] T008 [P] Add the Application ports in `backend/src/Application/Backup/` — `IDatabaseSnapshotStore` (`SnapshotAsync`, `RestoreAsync`), `IBackupArchiveStore` (`Write`, `Read`), `IMigrationInspector` (`AppliedTipAsync`, `KnownMigrations`) per data-model §4
- [X] T009 [P] Add `EnumerateAll()` to `ICvFileStore` (`backend/src/Application/Cv/ICvFileStore.cs`) returning every stored CV (`{ fileName, absolutePath }`), and implement it in `backend/src/Infrastructure/Cv/LocalCvFileStore.cs` by enumerating the resolved `cv-data` directory
- [X] T010 [P] Implement `EfMigrationInspector : IMigrationInspector` in `backend/src/Infrastructure/Backup/EfMigrationInspector.cs` (`AppliedTipAsync` → `db.Database.GetAppliedMigrationsAsync().Last()`; `KnownMigrations` → `db.Database.GetMigrations()`)
- [X] T011 Add `Backup:StoragePath` resolution (default `{AppContext.BaseDirectory}/backups`, created on first use, mirroring `LocalCvFileStore`) and register `IMigrationInspector → EfMigrationInspector` in `backend/src/Infrastructure/DependencyInjection.cs`
- [X] T012 Create the loopback-only endpoint group skeleton: `internal static class BackupEndpoints` with `MapBackupEndpoints(this IEndpointRouteBuilder api)` → `api.MapGroup("/backup").AddEndpointFilter<LoopbackOnlyFilter>()` in `backend/src/Web/Endpoints/BackupEndpoints.cs`, and wire `MapBackupEndpoints()` into `backend/src/Web/Endpoints/FeatureEndpoints.cs`
- [X] T013 [P] Create the frontend module shell: `frontend/src/api/backup.ts` (empty exports stub) + add `BackupManifestDto`, `BackupInspectionDto`, `RestoreReportDto` to `frontend/src/api/types.ts`; render an empty `<BackupSection/>` card (`.card .settings-card`) as the last section in `frontend/src/pages/Settings/SettingsPage.tsx` and create `frontend/src/pages/Settings/BackupSection.tsx` (placeholder)

**Checkpoint**: gate, manifest, ports, table inventory, endpoint group, and FE shell exist — stories can begin.

---

## Phase 3: User Story 1 — Create a complete backup (Priority: P1) 🎯 MVP

**Goal**: one click in the UI downloads a single archive capturing **all 12 tables + every CV file**,
without altering live data (FR-001/002/003/004/006; SC-001/SC-002).

**Independent Test**: click **Backup** → a `jobs-backup-<utc>.zip` downloads; its `manifest.json` lists
all 12 tables (with columns) + the CV inventory; `cv-data/` holds every PDF; live data is unchanged and
the app stayed usable. (quickstart US1.)

### Tests for User Story 1 (write first; must fail before implementation)

- [X] T014 [P] [US1] Real-Postgres integration test `backend/tests/Infrastructure.Tests/Backup/BackupCreateTests.cs` — seed data (incl. `offers.salary_bands` Currency, `role_group.member_offer_ids` jsonb, an uploaded CV) → `GET /api/backup` → assert the zip has `manifest.json` + 12 `db/*.copy` + every `cv-data` file with matching SHA-256; assert an **empty-DB** backup is still valid
- [X] T015 [P] [US1] Real-Postgres test in the same file (`BackupCreateTests`) asserting **non-destructive** (SC-002): per-table row counts + row-hash fingerprints are identical before and after a backup
- [X] T016 [P] [US1] Frontend test `frontend/tests/settings/BackupSection.test.tsx` — the Backup button renders and invokes `downloadBackup()` (fetch→blob); the busy/success/error `settings-msg` states render

### Implementation for User Story 1

- [X] T017 [US1] Implement `PostgresSnapshotStore.SnapshotAsync` in `backend/src/Infrastructure/Backup/PostgresSnapshotStore.cs` — own Npgsql connection, `REPEATABLE READ READ ONLY` tx, and `COPY <table> (<cols>) TO STDOUT` (text) for each `BackupTables` entry; return per-table payload + column list + row count (columns from `information_schema.columns`)
- [X] T018 [US1] Implement `ZipBackupArchiveStore.Write` in `backend/src/Infrastructure/Backup/ZipBackupArchiveStore.cs` — write `manifest.json` + `db/<table>.copy` + `cv-data/<name>` into a `ZipArchive` on a **server-side temp file** (System.IO.Compression)
- [X] T019 [US1] Implement `BackupService.CreateAsync` in `backend/src/Application/Backup/BackupService.cs` — `gate.TryBeginBackup()` (`Result` `BusyMaintenance` if false); build `BackupManifest` (tip via `IMigrationInspector`; per-table `columns` **and `rowCount`** from the snapshot — FR-005; `cvFiles` hashes via `ICvFileStore.EnumerateAll` + SHA-256); call snapshot + archive-write to a temp file; return the artifact; `EndBackup()` in `finally`; on any error return failure (never a partial archive — FR-006)
- [X] T020 [US1] Register `IDatabaseSnapshotStore → PostgresSnapshotStore`, `IBackupArchiveStore → ZipBackupArchiveStore` (Infrastructure DI) and `BackupService` (Scoped, Application DI) in `backend/src/Infrastructure/DependencyInjection.cs` and `backend/src/Application/DependencyInjection.cs`
- [X] T021 [US1] Add `GET /api/backup` to `backend/src/Web/Endpoints/BackupEndpoints.cs` — call `BackupService.CreateAsync`; on success `Results.File(stream, "application/zip", "jobs-backup-<utc>.zip")` streaming from the temp file (DeleteOnClose) ; map `BusyMaintenance` → 409 and failure → 500 via `ResultExtensions`
- [X] T022 [US1] Implement the Backup control in `frontend/src/pages/Settings/BackupSection.tsx` + `downloadBackup()` in `frontend/src/api/backup.ts` — `fetch('/api/backup')` → read the `blob` → trigger a synthetic `<a download>` click (filename from `Content-Disposition`); show a busy state while fetching and a success/error `settings-msg` from the result (parse the `{ error }` envelope on non-OK). A plain `<a href download>` is insufficient — it cannot report completion or failure (FR-006/016)

**Checkpoint**: US1 is fully functional — a complete backup downloads and live data is provably untouched.

---

## Phase 4: User Story 2 — Restore from a backup (Priority: P2)

**Goal**: upload a previously downloaded backup and restore the app to it — validated before touching
anything, with an automatic safety pre-backup, explicit confirmation, all-or-nothing, and cross-version
handling (FR-008..FR-017; SC-003/004/005/006).

**Independent Test**: backup → mutate/delete data → Restore (upload, confirm) → data matches the backup
1:1 (incl. CV bytes); a `jobs-safety-<ts>.zip` exists; a corrupt upload is refused with live data intact;
an older backup loads into HEAD; a newer backup is refused. (quickstart US2.)

> Depends on Foundational. Adds `RestoreAsync` to the `PostgresSnapshotStore` and `Read`/validation to the
> `ZipBackupArchiveStore` created in US1 (additive — same files), plus new gate consult points.

### Tests for User Story 2 (write first; must fail before implementation)

- [X] T023 [P] [US2] Unit test `backend/tests/Application.Tests/BackupCompatibilityTests.cs` — `Decide` returns `Same` (tip==HEAD), `Older` (known earlier), `Newer` (unknown id)
- [X] T024 [P] [US2] Real-Postgres **round-trip** test `backend/tests/Infrastructure.Tests/RestoreRoundTripTests.cs` — backup → `PostgresFixture` wipe → restore → assert per-row equality across all 12 tables (esp. `salary_bands` Currency, `member_offer_ids` jsonb, enum/owned/jsonb columns) + CV file bytes (SC-004)
- [X] T025 [P] [US2] Real-Postgres **all-or-nothing** test `backend/tests/Infrastructure.Tests/RestoreAtomicityTests.cs` — inject a mid-restore failure (after TRUNCATE/COPY, before/at swap) → assert DB rollback + `cv-data` swap-back leave live state byte-identical and a safety backup exists (FR-012)
- [X] T026 [P] [US2] Real-Postgres **validation** test `backend/tests/Infrastructure.Tests/RestoreValidationTests.cs` — corrupt zip / `export.json` / truncated archive / SHA mismatch / `candidate_cv.file_name` with no file → refused **before any write**; live data intact (FR-011)
- [X] T027 [P] [US2] Real-Postgres **cross-version** tests `backend/tests/Infrastructure.Tests/RestoreCrossVersionTests.cs` — OLDER backup (omit a newer column/table) restores into HEAD, new columns take defaults, backfill synthesises `Pending` rows; NEWER backup (unknown tip) → refused, live data intact (FR-017)
- [X] T028 [P] [US2] **Additivity guard** test `backend/tests/Infrastructure.Tests/SchemaInvariantTests.cs` — assert no table has a serial/identity column (the explicit-PK reload invariant; data-model §3)
- [X] T029 [P] [US2] `MaintenanceGate` quiescing test `backend/tests/Application.Tests/MaintenanceGateTests.cs` (+ an Infrastructure test) — a second backup/restore while one runs → busy; `IsMaintenanceActive` makes `ScanOrchestrator.RunAsync` / scheduler tick / `EnrichmentService` write methods defer (FR-020)
- [X] T030 [P] [US2] Frontend test `frontend/src/pages/Settings/RestoreConfirmModal.test.tsx` — the confirm modal opens, Cancel aborts, Confirm fires the upload; busy/success/error rendering

### Implementation for User Story 2

- [X] T031 [P] [US2] Add `BackupCompatibility` enum + `Decide(tip, knownMigrations)` pure policy in `backend/src/Application/Backup/BackupCompatibility.cs` (data-model §5)
- [X] T032 [P] [US2] Add `RestoreReport` DTO in `backend/src/Application/Backup/RestoreReport.cs` and the `ISafetyBackupStore` port; implement `LocalSafetyBackupStore` (write a backup into `Backup:StoragePath`, return its path) in `backend/src/Infrastructure/Backup/LocalSafetyBackupStore.cs`
- [X] T033 [US2] Add `Read` + full archive validation to `backend/src/Infrastructure/Backup/ZipBackupArchiveStore.cs` — parse `manifest.json`, verify each `cv-data` file SHA-256, cross-check `candidate_cv.file_name` ↔ archived files, ensure required `db/*.copy` present (data-model §2 validation rules)
- [X] T034 [US2] Add `RestoreAsync` to `backend/src/Infrastructure/Backup/PostgresSnapshotStore.cs` — in one transaction: `TRUNCATE <12 tables> RESTART IDENTITY CASCADE` (exclude `__EFMigrationsHistory`) then `COPY <table> (<backup cols>) FROM STDIN` per table in dependency order (or `session_replication_role='replica'`); caller commits last
- [X] T035 [US2] Extract the idempotent enrichment backfill from `backend/src/Infrastructure/Persistence/DatabaseInitializer.cs` into a callable component so a restore can run it for OLDER backups (startup keeps calling it; no behaviour change)
- [X] T036 [US2] Implement `RestoreService.RestoreAsync` in `backend/src/Application/Backup/RestoreService.cs` — orchestrate the data-model §7 state machine: `gate.TryBeginRestore()` + drain in-flight scan (block-acquire `ScanConcurrencyGuard`) → read+validate → `BackupCompatibility.Decide` (refuse `Newer` → `IncompatibleNewer`) → **safety pre-backup** (`ISafetyBackupStore`) → stage `cv-data` temp + verify → DB tx wipe+load → atomic `cv-data` directory swap → COMMIT → if `Older` run backfill → return `RestoreReport`; on any failure ROLLBACK + swap-back; `EndRestore()` in `finally`
- [X] T037 [US2] Wire the `MaintenanceGate` consult points (FR-020): defer the tick in `backend/src/Infrastructure/Scheduling/ScanSchedulerService.cs` (`TickAsync`); check beside `ScanConcurrencyGuard.TryEnterAsync` in `backend/src/Application/Scanning/ScanOrchestrator.cs` (`RunAsync`, return a `ScanInProgress`-style result); reject/await in `backend/src/Application/Enrichment/EnrichmentService.cs` (`SubmitResultsAsync` + `TriggerRerunAsync`)
- [X] T038 [US2] Register `ISafetyBackupStore → LocalSafetyBackupStore` (Infrastructure DI) + `RestoreService` (Scoped, Application DI)
- [X] T039 [US2] Add `POST /api/backup/restore` to `backend/src/Web/Endpoints/BackupEndpoints.cs` — `multipart/form-data` `IFormFile`, `.DisableAntiforgery()`, **raised body-size limit** (default ~30 MB Kestrel cap is too small); call `RestoreService`; map `Result` → 200 (`RestoreReport`) / 400 `InvalidArchive` / 409 `BusyMaintenance` / 422 `IncompatibleNewer` / 500 (rolled-back) per the contract
- [X] T040 [US2] Add `restoreBackup(file)` to `frontend/src/api/backup.ts` (`api.upload`) and build `frontend/src/pages/Settings/RestoreConfirmModal.tsx` on the `ApplyModal` portal+focus-trap pattern; wire the Restore file-picker → confirm → upload + busy/progress + success (show `safetyBackupPath`) / error `settings-msg` in `BackupSection.tsx`

**Checkpoint**: full round trip works — back up → wipe → restore = zero data loss; restore is guarded,
atomic, version-aware, and pauses background writers.

---

## Phase 5: User Story 3 — Trust a backup before relying on it (Priority: P3)

**Goal**: inspect an uploaded backup (timestamp, per-table counts, CV count, source version, validity)
**without** restoring (FR-005; SC-008). Thin read-only veneer over the US2 archive read+validation.

**Independent Test**: pick a backup → see its summary + a `Same/Older/Newer` badge with no change to live
data; a corrupt/foreign file is flagged "not safe to restore" and Restore stays disabled. (quickstart US3.)

> Reuses the `ZipBackupArchiveStore.Read`/validation + `BackupCompatibility` from US2.

### Tests for User Story 3 (write first; must fail before implementation)

- [X] T041 [P] [US3] Real-Postgres test `backend/tests/Infrastructure.Tests/BackupInspectTests.cs` — a valid backup → `POST /api/backup/inspect` returns counts + version + compatibility with **no change to live data**; a corrupt/non-backup upload → `InvalidArchive`
- [X] T042 [P] [US3] Frontend test in `frontend/src/pages/Settings/BackupSection.test.tsx` — selecting a file shows the inspect summary; an invalid file disables Restore and shows an error

### Implementation for User Story 3

- [X] T043 [US3] Add `BackupService.InspectAsync` in `backend/src/Application/Backup/BackupService.cs` — read+validate the uploaded archive (reuse `IBackupArchiveStore.Read`), read per-table counts from the manifest's `rowCount` (no payload parsing) and compute `BackupCompatibility`, return a `BackupInspectionDto`; strictly read-only (no live-data access)
- [X] T044 [US3] Add `POST /api/backup/inspect` to `backend/src/Web/Endpoints/BackupEndpoints.cs` — `multipart/form-data`, `.DisableAntiforgery()`, raised body-size limit; return `BackupInspectionDto` (200) / `InvalidArchive` (400)
- [X] T045 [US3] Add `inspectBackup(file)` to `frontend/src/api/backup.ts` and wire `BackupSection.tsx` so selecting a restore file first calls inspect and renders the summary + compatibility badge, gating the Restore button on `valid && compatibility !== "Newer"`

**Checkpoint**: all three stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T046 [P] Loopback-guard HTTP-layer test for the `/api/backup/*` group in `backend/tests/Infrastructure.Tests/` (a non-loopback remote IP → 403 `LoopbackOnly`)
- [X] T047 [P] Document backup/restore in `README.md` (Principle XI) — the Settings UI, the archive format, that archives are sensitive + gitignored (Principle IV), and how this relates to the existing manual `pg_dump` note + `/api/export`
- [X] T048 SC-009 performance sanity check — confirm a typical data set backs up/restores in ≤ ~60 s with visible progress; note actuals in `quickstart.md`
- [X] T049 [P] SC-007 / FR-015 egress guard test in `backend/tests/Application.Tests/` (or `Infrastructure.Tests`) — assert the backup/restore code path references no outbound HTTP / external client and adds no new external dependency (fully local; complements the loopback test T046)
- [X] T050 Run the full [quickstart.md](./quickstart.md) validation in the running app (Principle VII) and confirm the full suite is green (Principle VI) before "done" — incl. the untouched `/api/export` + `ExportServiceTests` as the FR-018 regression contract

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all user stories**.
- **US1 (Phase 3)**: depends on Foundational. The MVP.
- **US2 (Phase 4)**: depends on Foundational; additively extends the `PostgresSnapshotStore` +
  `ZipBackupArchiveStore` files first created in US1 (so US1's backup-direction code should exist), and
  adds the gate consult points.
- **US3 (Phase 5)**: depends on Foundational + the `ZipBackupArchiveStore.Read`/validation +
  `BackupCompatibility` introduced in US2.
- **Polish (Phase 6)**: depends on the desired stories being complete.

### Within each story

- Tests are written first and must FAIL before implementation (Principle VI).
- Ports/types before services; services before endpoints; endpoint before its UI.
- `COPY TO` (US1) before `COPY FROM` (US2, same file); archive `Write` (US1) before `Read`/validate (US2).

### Parallel opportunities

- Setup: T002, T003 in parallel.
- Foundational: T006, T007, T008, T009, T010, T013 in parallel (distinct files) after T004/T005; T011/T012 follow.
- US1 tests T014–T016 in parallel; then implementation (T017→T018→T019→T020→T021→T022 are largely sequential by dependency).
- US2 tests T023–T030 in parallel; impl T031/T032 in parallel, then T033/T034/T035 (distinct files) in parallel, then T036 (depends on them) → T037 → T038 → T039 → T040.
- US3 tests T041–T042 in parallel; impl T043→T044→T045.
- Polish: T046, T047, T049 in parallel; T050 (quickstart run + green suite) last.

---

## Parallel Example: User Story 2 tests

```bash
# Launch US2 tests together (all distinct files, write-to-fail-first):
Task: "Unit BackupCompatibility in backend/tests/Application.Tests/BackupCompatibilityTests.cs"
Task: "Round-trip restore in backend/tests/Infrastructure.Tests/RestoreRoundTripTests.cs"
Task: "All-or-nothing in backend/tests/Infrastructure.Tests/RestoreAtomicityTests.cs"
Task: "Validation in backend/tests/Infrastructure.Tests/RestoreValidationTests.cs"
Task: "Cross-version in backend/tests/Infrastructure.Tests/RestoreCrossVersionTests.cs"
Task: "Additivity guard in backend/tests/Infrastructure.Tests/SchemaInvariantTests.cs"
Task: "MaintenanceGate in backend/tests/Application.Tests/MaintenanceGateTests.cs"
```

---

## Implementation Strategy

### MVP first (US1 only)

1. Phase 1 Setup → Phase 2 Foundational (CRITICAL — blocks all stories).
2. Phase 3 US1 — download a complete, non-destructive backup.
3. **STOP & VALIDATE**: quickstart US1 (the archive captures both stores; live data untouched). The
   user can already protect their data — ship/demo.

### Incremental delivery

1. Setup + Foundational → foundation ready.
2. US1 → complete backup (MVP).
3. US2 → guarded, atomic, version-aware restore (the recovery half; round-trip zero-loss).
4. US3 → inspect/verify a backup before relying on it.
5. Polish → docs, loopback test, performance check, full quickstart run.

---

## Notes

- [P] = different files, no incomplete dependency. [Story] maps each task to US1/US2/US3 for traceability.
- **No new EF migration and no new NuGet dependency** (plan ADR-1; Principles IX/X). Edits to 001/002 code
  paths (scheduler, `ScanOrchestrator`, `EnrichmentService`) are **gate checks only** — a guard, not a
  behaviour change; the untouched 001/002 suites remain the regression contract.
- Resolve the `cv-data` and `backups` directories via the `Cv:StoragePath` / `Backup:StoragePath` logic —
  never hard-code a repo-root path (differs between host-dev `{bin}/cv-data` and the container volume).
- Commit after each task or logical group (Conventional Commits, Principle XI).
