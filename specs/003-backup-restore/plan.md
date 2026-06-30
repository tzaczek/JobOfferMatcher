# Implementation Plan: On-Demand Backup & Restore

**Branch**: `003-backup-restore` (spec directory; no git branch created — repo has no `before_*` hook)
| **Date**: 2026-06-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/003-backup-restore/spec.md` (with Clarifications Session
2026-06-29: create+restore, browser-download delivery, no encryption, migrate-older/refuse-newer,
serialize+pause-on-restore, ≤~60s/typical bounded performance).

**Constitution**: `.specify/memory/constitution.md` v1.1.0 — **not amended**. This feature directly
**fulfils** Principle IX ("Your Data Is Recoverable") and upholds Principle IV (PII stays local). No
load-bearing decision is superseded; decisions specific to this feature are recorded as ADR-1..ADR-4.

## Summary

Add a **complete, on-demand backup and restore** of the app's entire state — both data stores: the
PostgreSQL database **and** the uploaded CV files on disk (`cv-data/`, whose PDF bytes are *not* in the
DB) — triggered from the UI, delivered as a single downloadable archive, with a guarded restore-from-upload.
"Make sure no data is lost" decomposes into two guarantees: backup is **non-destructive** (live data
untouched, app stays usable) and **complete** (every table + every CV file), and restore is
**all-or-nothing + recoverable** (validate-before-touch, automatic safety pre-backup, atomic, reversible).

Technical approach (from [research.md](./research.md), grounded in the live code via a parallel
codebase investigation + adversarial mechanism review, 2026-06-29):

- **Mechanism — in-process pure-.NET logical snapshot via Npgsql `COPY`** (ADR-1). Read every table with
  `COPY <table> (<explicit cols>) TO STDOUT` (text format) inside **one `REPEATABLE READ READ ONLY`
  transaction** (a single MVCC point-in-time across all tables), and restore with `COPY … FROM STDIN`.
  This is the **only** option that works identically in *both* supported run modes: the shipped
  `docker-compose` `app` image (`aspnet:10.0`) has **no** `pg_dump`/`pg_restore` and no docker socket, so
  `pg_dump` shell-out / `docker exec` work in dev host-mode but **silently fail** in the container.
  COPY needs nothing but the existing `GetConnectionString("AppDb")` and copies each column as its **raw
  on-disk text**, which **sidesteps every EF mapping hazard** (the 8 strongly-typed-ID converters, the
  `Currency` value object with a private ctor inside `offers.salary_bands` jsonb, enum→string, owned-type
  flattening, jsonb arrays embedding wrapped IDs) — COPY never instantiates an EF entity. **No new NuGet
  dependency** (`System.IO.Compression` + Npgsql are already present).
- **Archive — one unencrypted `.zip`** (FR-019, ADR-4): `manifest.json` (format version, `createdAtUtc`,
  EF/app version `10.0.9`, the **applied-migration tip** from `GetAppliedMigrationsAsync().Last()`, the
  ordered table list **with the explicit per-table column list and `rowCount`** (per-category counts —
  FR-005), and a `cv-data` inventory of `{name,size,sha256}`); `db/<table>.copy` (one COPY-text payload per data table); `cv-data/<cvId:N>.<ext>`
  (raw PDF bytes for every CV). The per-table **column list** is the load-bearing element for
  cross-version restore. The archive is built to a **server-side temp file first**, then streamed — so a
  failure produces an error, never a truncated download presented as complete (FR-006).
- **Restore — data-only into the current HEAD schema, all-or-nothing across both stores** (ADR-2).
  Validate the archive fully **before** touching anything; write the automatic **server-side safety
  pre-backup**; stage `cv-data` into a sibling temp dir and verify hashes; then in **one DB transaction**:
  `TRUNCATE <12 data tables> RESTART IDENTITY CASCADE` (keeping schema + `__EFMigrationsHistory`), `COPY …
  FROM STDIN` per table using the **backup's** recorded columns (so newer columns absent from an older
  backup take their DDL defaults); **atomic `cv-data` directory swap**; `COMMIT` last. Any failure →
  `ROLLBACK` (Postgres DML is transactional) + swap the files back; the safety pre-backup is the final
  fallback. All PKs are client-assigned (`ValueGeneratedNever`) with **no identity/serial sequences and no
  concurrency tokens**, so explicit-value reload needs no `setval`/sequence resync (asserted by a test).
- **Cross-version (FR-017) by migration id, never by schema reversal** (ADR-2). Compare the manifest tip
  to `db.Database.GetMigrations()` (ids *this* build knows): **==HEAD** → load directly; **older but known**
  → load data-only into HEAD then run the idempotent enrichment backfill to synthesise missing `Pending`
  satellite rows; **unknown (newer)** → **refuse** (this binary cannot represent that schema; runtime never
  runs `Down`). Feasible because every migration to date is **additive with safe defaults** — an invariant
  a guard test enforces so a future non-defaulted `NOT NULL`/rename breaks the build, not a user's restore.
- **Quiescing (FR-020) via a new `MaintenanceGate` singleton** (ADR-3), *not* by overloading
  `ScanConcurrencyGuard` (which is scan-only and non-blocking). A **backup runs concurrently** with scans
  (MVCC gives consistency) and only serialises against other backup/restore ops; a **restore** acquires the
  gate, blocks new scans + enrichment writes, and **drains the in-flight scan** by acquiring
  `ScanConcurrencyGuard`. The gate is consulted at `ScanSchedulerService.TickAsync`,
  `ScanOrchestrator.RunAsync` (covers manual + scheduled scans), and `EnrichmentService.SubmitResultsAsync`
  / `TriggerRerunAsync` (the only enrichment write entry points — the repository's `ExecuteUpdateAsync`
  paths bypass `SaveChanges`, so gating at service entry, not at `SaveChanges`, is correct).
- **Endpoints — a loopback-only `/api/backup` group** (ADR-3/IV): `GET /api/backup` streams the archive;
  `POST /api/backup/inspect` validates an uploaded archive and returns its manifest summary **without
  touching live data** (US3); `POST /api/backup/restore` performs the guarded restore. Reuses the existing
  `LoopbackOnlyFilter` (the established PII control) and raises the upload body-size limit (the default
  ~30 MB Kestrel cap would reject a real archive).
- **Zero schema change** — no new tables/columns, **no migration** (only a new `Backup:StoragePath`
  config key + gate checks added to existing write paths). 001 collection/feed/scheduler and 002
  enrichment behaviour are preserved.

The full design is in [research.md](./research.md), [data-model.md](./data-model.md),
[contracts/backup-api.md](./contracts/backup-api.md), and [quickstart.md](./quickstart.md).

## Technical Context

**Language/Version**: C# 14 on **.NET 10** (backend); **TypeScript + React 19** via Vite (frontend).
Unchanged from features 001/002.

**Primary Dependencies**:
- Backend: ASP.NET Core 10 minimal APIs; EF Core **10.0.9** + `Npgsql.EntityFrameworkCore.PostgreSQL`
  **10.0.2** (Npgsql provides the `COPY` API used directly); `System.IO.Compression` (BCL — `ZipArchive`).
  **Added: none** — no new NuGet package (Principle X). No `pg_dump`/`pg_restore`/docker dependency.
- Frontend: React 19, Vite, TypeScript, the existing hand-rolled typed `fetch` client (`api/client.ts`,
  `ApiError`), central design tokens. **Added: none.**

**Storage**: PostgreSQL 17 via EF Core (12 application tables) **+** the on-disk CV directory
(`Cv:StoragePath` ?? `{AppContext.BaseDirectory}/cv-data`). Backup writes a `.zip`; the automatic
safety pre-backup is written under a new gitignored `Backup:StoragePath` ?? `{BaseDirectory}/backups`.
**No new EF migration** — backup/restore reads/writes existing tables via `COPY`.

**Testing**: xUnit unit tests (Application policy: `BackupCompatibility` older/newer/same; manifest
validation; the `MaintenanceGate` semantics). **Real-PostgreSQL** integration tests via Testcontainers
(Principle V) reusing the `PostgresFixture` `TRUNCATE … EXCEPT __EFMigrationsHistory` pattern:
backup→wipe→restore round-trip with full-fidelity asserts (incl. `salary_bands` `Currency`,
`role_group.member_offer_ids` jsonb, every enum/owned/jsonb column, and CV file bytes); OLDER-backup→HEAD
(new columns default, backfill synthesises `Pending`); NEWER-backup **refusal**; all-or-nothing failure
(assert DB rollback + `cv-data` swap-back leave live state byte-identical + a safety backup exists);
malformed/corrupt/non-backup upload rejected pre-write; non-destructive backup (data before == after);
**guard test** asserting no serial/identity column exists (the explicit-PK reload invariant). The
loopback guard on `/api/backup/*` is an HTTP-layer test. Frontend: Vitest + RTL for the backup button,
restore confirm modal, busy/progress, and `settings-msg` success/error states.

**Target Platform**: local-first, single-user; Windows 11 dev. The app runs on `localhost` (dev host
`:5180`; published `:5000`; container `:8080`). Backup/restore is an in-process capability behind a
loopback-only endpoint group + a Settings-page UI section.

**Project Type**: Web application (existing `backend/` + `frontend/`).

**Performance Goals**: not throughput-bound. SC-009 target: a typical single-user data set (≤ ~10k
offers + a handful of CVs) backs up or restores in **≤ ~60 s** with visible progress; **no hard size
cap**. Backup is build-to-temp-then-stream; restore is synchronous within one request (single user). An
async job/status model is explicitly rejected as over-engineering (ADR-4).

**Constraints**: fully local — **0** external calls, **0** external binaries (no `pg_dump`/docker);
`/api/backup/*` loopback-only; archives unencrypted (FR-019) and gitignored; restore is non-destructive
of the *prior* state (auto safety pre-backup, all-or-nothing); append-only migrations untouched (never
run `Down`); async all the way; nullable on, warnings-as-errors in Domain + Application.

**Scale/Scope**: 1 user; 12 data tables; ~hundreds–low-thousands of offers; ≤ a few CV PDFs. One backup
archive per click; ≥ the last safety pre-backup retained on disk. 3 user stories (US1 backup P1, US2
restore P2, US3 inspect/verify P3).

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design. All gates PASS — no violations;
feature-specific decisions recorded as ADR-1..ADR-4 (Principle XI). Principle IX is positively advanced.*

| # | Principle | Status | How this plan satisfies it |
|---|-----------|--------|----------------------------|
| I | Layered architecture, deps inward | ✅ PASS | Pure compatibility policy + DTOs in **Application/Backup** (`BackupService`, `RestoreService`, `BackupManifest`, `BackupCompatibility`, ports `IDatabaseSnapshotStore`/`IBackupArchiveStore`/`ISafetyBackupStore`/`IMigrationInspector` + `MaintenanceGate`); Npgsql `COPY`, zip, and file IO in **Infrastructure**; endpoints in **Web**. Commands return `Result<T>`; `inspect` is read-only. No MediatR (YAGNI). **Domain untouched** (backup is not a job-search concept). |
| II | Strongly-typed domain, no primitive obsession | ✅ PASS | Reuses existing wrapped IDs (`CvId`, …). New types where they carry meaning: `BackupManifest` record, `BackupCompatibility` enum (`Same`/`Older`/`Newer`), `RestoreReport`. EF migration ids stay plain strings (external EF identifiers, like file paths) — wrapping them would be ceremony (Principle X). `Result<T>` for expected failures (invalid/newer archive, busy, missing files). |
| III | The Tracker Reflects Reality (NON-NEG) | ✅ PASS | Restore is deliberate, explicitly confirmed, and recoverable; it replaces data only with a **validated** backup and never fabricates records. A truncated/partial backup is never presented as complete (build-to-temp-then-stream, FR-006). No demo/placeholder data introduced. |
| IV | Personal Data Private & Local (NON-NEG) | ✅ PASS | 100% in-process over the existing `AppDb` connection — **no external call, no external binary, no docker socket**. `/api/backup/*` reuses `LoopbackOnlyFilter` (the established load-bearing PII control) so the DB+CV archive never leaves `localhost`. Archives are unencrypted per FR-019 (recorded decision: live DB/CVs are already plaintext locally, and a lost password must never strand a backup) and stay local; the safety pre-backup lives under a **gitignored** `backups/` dir. The `.gitignore` posture for DB/exports/CVs extends to `*.zip` backups. |
| V | Real Database in Tests | ✅ PASS | Round-trip, cross-version, refusal, and all-or-nothing-failure paths tested on **real PostgreSQL** (Testcontainers + `PostgresFixture`). The DB is never mocked. |
| VI | Green Before Done (NON-NEG) | ✅ PASS | Each story closes only on a green local suite; the untouched 001/002 suites are the regression contract for "preserve existing behaviour". |
| VII | UI Changes Require Visual Verification | ✅ PASS | The Settings **Backup & Restore** section, the download, the restore **confirm modal**, the busy/progress state, and inline success/error `settings-msg` are run-and-looked-at before "done". |
| VIII | One Source of Design Truth | ✅ PASS | Reuses `.card`/`.settings-card`/`.settings-actions`/`.settings-msg--error\|--ok` and the `ApplyModal` portal+focus-trap pattern for the confirm dialog. The one new primitive — a `.btn--danger` for the destructive restore — is added to `base.css` using existing `--c-danger`/`--c-danger-bg` tokens (no scattered literals). |
| IX | Your Data Is Recoverable | ✅ **ADVANCED** | This feature **is** the documented backup/restore path the principle calls for. Append-only migrations untouched; restore **never** runs `Down`/downgrade — it loads data-only into HEAD; `__EFMigrationsHistory` is preserved; the automatic safety pre-backup guarantees the prior state is recoverable; the archive is a portable, self-describing, human-inspectable format. |
| X | Simple by Default (YAGNI) | ✅ PASS | **No new dependency**, **no new migration**, no `pg_dump`/docker/data-dir machinery, no async job/status table. Synchronous streaming endpoints; reuses the existing `TRUNCATE-except-history`, export-download, and CV-upload patterns. The gate is a small singleton consulted at 4 existing entry points. |
| XI | Documented Decisions, Immutable History | ✅ PASS | ADR-1..ADR-4 below record the mechanism, restore/cross-version strategy, quiescing seam, and delivery/safety choices. Conventional Commits, one logical change each. |

**Decisions (ADR-style, per Principle XI):**

- **ADR-1 — In-process Npgsql `COPY` logical snapshot (reject `pg_dump`, `docker exec`, volume copy).**
  - *Context*: two supported run modes. Dev = Postgres in the `jobs-postgres` container + the .NET host
    as a **host process** (`./start.ps1`, `:5180`) where the `docker` CLI exists. Shipped = the app
    **inside** the `jobs-app` container (`aspnet:10.0`), which has **no** postgres client and **no**
    docker socket mounted (compose mounts only `jobs_cvdata`). The data also lives in **two** stores
    (DB + `cv-data/` PDF bytes).
  - *Decision*: produce the DB half **in-process** with Npgsql `COPY <table> (<cols>) TO/FROM STDOUT`
    (text), bundled with the `cv-data` files into one `.zip`. Capture all 12 tables in **one
    `REPEATABLE READ READ ONLY`** transaction for a consistent point-in-time.
  - *Rationale*: the only mechanism that works in **both** modes (network protocol over `AppDb`, no
    external binary), and copying **raw column text** sidesteps every EF converter (`Currency` private
    ctor in `salary_bands`, 8 strongly-typed-ID converters, enum→string, owned-type flattening, wrapped
    IDs inside jsonb arrays). No new dependency.
  - *Rejected*: **host `pg_dump`** (not a documented prereq; PG-major version coupling); **`docker exec
    pg_dump`** (impossible in the shipped container — mode-specific footgun); **physical volume/data-dir
    copy** (needs Postgres stopped, unreachable from the app, OS/PG17-coupled); **EF-entity
    serialization** (re-hydrates every value converter — fragile, silent-corruption surface); **reusing
    `/api/export` json/csv** (offers-only, in-memory, omits CVs and most tables — incomplete).

- **ADR-2 — Data-only restore into the current HEAD schema; cross-version by migration id.**
  - *Decision*: the running binary has already migrated to **HEAD** at startup, so the restore **target
    is HEAD**. Validate → safety pre-backup → stage files → one transaction `TRUNCATE <12 tables>
    RESTART IDENTITY CASCADE` (keep schema + `__EFMigrationsHistory`) → `COPY FROM STDIN` per table using
    the **backup's** column list → atomic file swap → `COMMIT` last; rollback + swap-back on any failure.
    FR-017: manifest tip `== HEAD` → direct; **older but in `GetMigrations()`** → load into HEAD (omitted
    newer columns take DDL defaults; new tables start empty) then run the idempotent enrichment backfill;
    **unknown/newer** → refuse. **Never** call `MigrateAsync(target)` to downgrade (it would run
    `Down` = `DropColumn`/`DropTable` and destroy data).
  - *Rationale*: every migration to date is **additive with safe defaults** (the lone `DropColumn`
    dropped a recomputable cache), making "older snapshot into HEAD" sound. No identity/serial sequences
    and no concurrency tokens exist, so explicit-PK reload needs no resync.
  - *Consequence / guard*: a future non-defaulted `NOT NULL`, a column rename, or a new
    serial/identity column would break this. A **guard test asserts the additivity invariant** so such a
    migration fails the build, not a user's restore.

- **ADR-3 — New `MaintenanceGate` singleton for FR-020 (don't overload `ScanConcurrencyGuard`).**
  - *Context*: `ScanConcurrencyGuard` is a `SemaphoreSlim(1,1)` **non-blocking** single-flight scoped to
    **scans only** (`ScanOrchestrator.RunAsync`); it does not cover enrichment, and "reject if busy" is
    the wrong shape for "pause for the duration of a restore." The scheduler (`ScanSchedulerService`,
    registered by default) and enrichment writes (`EnrichmentService.SubmitResultsAsync`/`TriggerRerunAsync`,
    incl. `ExecuteUpdateAsync` paths that bypass `SaveChanges`) are independent writers.
  - *Decision*: add a process-wide **`MaintenanceGate`** (Application singleton, registered beside
    `ScanConcurrencyGuard`) with (1) a `SemaphoreSlim(1,1)` so **only one backup/restore runs at a time**
    (second attempt → 409), (2) a "maintenance active" check writers consult, (3) restore **drains the
    in-flight scan** by acquiring `ScanConcurrencyGuard` (blocking). A **backup** acquires only (1) and
    runs concurrently with scans (MVCC consistency); a **restore** acquires (1)+(2)+(3) and pauses
    writers. Consulted at: `ScanSchedulerService.TickAsync`, `ScanOrchestrator.RunAsync`,
    `EnrichmentService.SubmitResultsAsync`, `EnrichmentService.TriggerRerunAsync`.
  - *Rationale*: matches the spec exactly (backup concurrent + consistent; restore pauses
    scanning/enrichment) and gates at the true write entry points (a `SaveChanges` chokepoint would miss
    the three enrichment `ExecuteUpdateAsync` calls).

- **ADR-4 — Download-only delivery + server-side safety pre-backup; synchronous endpoints.**
  - *Decision*: `GET /api/backup` builds the archive to a **server-side temp file first**, then streams
    it via `Results.File(stream/path, "application/zip", "jobs-backup-<utc>.zip")` and deletes the temp
    (avoids the `ExportService` in-memory `byte[]` OOM risk **and** guarantees complete-or-error, FR-006).
    Restore is a single synchronous `POST` (single user, ≤~60s). Because delivery is download-only, the
    automatic **pre-restore safety backup is written server-side** to a gitignored `Backup:StoragePath`
    ?? `{BaseDirectory}/backups` dir (not streamed to the browser) and its path returned/logged; it is
    the rollback source and keeps restore non-destructive in spirit (Principle IX).
  - *Rejected*: an async job + `/status` polling table (the frontend has a `poll` helper, but a
    job/status table is over-engineering for a single-user ≤60s operation — YAGNI); streaming the
    safety backup as a second browser download (clumsier and not guaranteed to be saved).

**Complexity Tracking**: No constitution violations — table omitted (N/A).

## Project Structure

### Documentation (this feature)

```text
specs/003-backup-restore/
├── plan.md              # This file
├── research.md          # Phase 0 — R1..R6 decisions, rationale, alternatives
├── data-model.md        # Phase 1 — archive layout, manifest schema, table inventory + order,
│                         #           restore state machine, MaintenanceGate, gate consult points
├── quickstart.md        # Phase 1 — run & per-user-story validation guide
├── contracts/
│   └── backup-api.md    # Phase 1 — REST contract (GET /backup, POST /backup/inspect, POST /backup/restore)
│                         #           + the on-disk archive format spec
├── spec.md              # Feature spec (with Clarifications)
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root) — delta over features 001/002

```text
backend/
├── src/
│   ├── Domain/                            # UNCHANGED — backup is not a domain concept (no new types, no migration)
│   ├── Application/
│   │   ├── Backup/                        # NEW: BackupService, RestoreService, BackupManifest (+ entry/inventory records),
│   │   │                                  #      BackupCompatibility (Same/Older/Newer policy), RestoreReport,
│   │   │                                  #      ports: IDatabaseSnapshotStore, IBackupArchiveStore, ISafetyBackupStore,
│   │   │                                  #      IMigrationInspector; loose archive validation
│   │   ├── Scanning/                       # MaintenanceGate (NEW singleton, alongside ScanConcurrencyGuard);
│   │   │                                  #      ScanOrchestrator.RunAsync consults the gate (return ScanInProgress-style result)
│   │   ├── Enrichment/                     # EnrichmentService.SubmitResultsAsync + TriggerRerunAsync consult the gate
│   │   ├── Cv/                             # ICvFileStore += EnumerateAll() (list every stored CV for the archive)
│   │   └── DependencyInjection.cs          # register MaintenanceGate singleton + BackupService/RestoreService (Scoped)
│   ├── Infrastructure/
│   │   ├── Backup/                         # NEW: PostgresSnapshotStore (Npgsql COPY TO/FROM, REPEATABLE READ; TRUNCATE),
│   │   │                                  #      ZipBackupArchiveStore (System.IO.Compression), LocalSafetyBackupStore,
│   │   │                                  #      EfMigrationInspector (GetAppliedMigrationsAsync/GetMigrations)
│   │   ├── Cv/                             # LocalCvFileStore += EnumerateAll(); restore-time atomic dir swap helper
│   │   ├── Scheduling/                     # ScanSchedulerService.TickAsync consults MaintenanceGate (defer tick if active)
│   │   ├── Persistence/                    # reuse the TRUNCATE-except-__EFMigrationsHistory pattern; idempotent
│   │   │                                  #   enrichment backfill made callable by restore (older-backup path)
│   │   └── DependencyInjection.cs          # register Infrastructure backup ports; add Backup:StoragePath resolution
│   └── Web/
│       └── Endpoints/                      # NEW BackupEndpoints (GET /backup, POST /backup/inspect, POST /backup/restore;
│                                           #   .AddEndpointFilter<LoopbackOnlyFilter>(); .DisableAntiforgery() + raised
│                                           #   body-size limit on the upload endpoints); FeatureEndpoints wires the group
└── tests/
    ├── Application.Tests/                  # BackupCompatibility (older/newer/same), manifest validation, MaintenanceGate
    └── Infrastructure.Tests/              # real Postgres: round-trip fidelity (salary_bands Currency, member_offer_ids,
                                            #   enums/owned/jsonb, CV bytes); OLDER→HEAD (defaults + backfill); NEWER refusal;
                                            #   all-or-nothing failure (rollback + swap-back); corrupt-upload rejection;
                                            #   non-destructive backup; no-serial/identity guard; loopback guard (HTTP layer)

frontend/
└── src/
    ├── api/
    │   ├── backup.ts                       # NEW: downloadBackup() (fetch→blob→synthetic <a download>, so backup
    │   │                                  #      success/failure is surfaced — FR-006/016) + inspectBackup(file) +
    │   │                                  #      restoreBackup(file) via api.upload (mirrors cv.ts)
    │   └── types.ts                        # +BackupManifestDto / RestoreReportDto
    ├── pages/Settings/
    │   ├── SettingsPage.tsx                # render <BackupSection/> as the last settings card
    │   ├── BackupSection.tsx               # NEW: Backup download button + Restore (file picker → inspect summary →
    │   │                                  #      RestoreConfirmModal → upload) + busy/progress + settings-msg success/error
    │   └── RestoreConfirmModal.tsx         # NEW: destructive confirm (built on the ApplyModal portal/focus-trap pattern)
    └── theme/
        └── base.css                        # +.btn--danger using --c-danger / --c-danger-bg (no new tokens)
```

**Structure Decision**: Web-application layout, unchanged. The feature is **additive** and **schema-free**:
a new `Application/Backup` slice with ports implemented in `Infrastructure/Backup`, a small
`MaintenanceGate` consulted at four existing write entry points, one loopback-only Web endpoint group, and
one Settings UI section. Domain stays untouched; there is **no new EF migration**. 001/002 code paths are
modified only by adding a gate check (a guard, not a behaviour change) plus one no-behaviour-change
extraction — making the idempotent enrichment backfill callable so restore can reuse it for older backups
(startup keeps calling it unchanged).

## Phase Status

- [x] Phase 0 — Research (`research.md`): 6 unknowns resolved (R1–R6) via a parallel codebase
  investigation + an adversarial mechanism review; all `NEEDS CLARIFICATION` resolved.
- [x] Phase 1 — Design & Contracts (`data-model.md`, `contracts/backup-api.md`, `quickstart.md`);
  agent context (CLAUDE.md SPECKIT section) updated to point here.
- [ ] Phase 2 — Tasks (`/speckit-tasks`) — **not** produced by this command.

## Complexity Tracking

> No Constitution Check violations — this section is intentionally empty (N/A). Feature-specific
> decisions are recorded in ADR-1..ADR-4, not as violations.
