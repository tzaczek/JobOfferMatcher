# Phase 0 Research: On-Demand Backup & Restore

**Date**: 2026-06-29 | **Feature**: `003-backup-restore` | **Plan**: [plan.md](./plan.md)

Method: a parallel read-only investigation of the live codebase (runtime/tooling, full data inventory,
Web/DI, background writers, frontend, migrations) feeding an adversarial mechanism-selection review.
Every decision below is grounded in cited code. All `NEEDS CLARIFICATION` are resolved.

---

## R1 — Backup mechanism: how to produce a complete, consistent snapshot

**Decision**: An **in-process pure-.NET logical snapshot via Npgsql `COPY`**. For each of the 12
application tables, stream `COPY <table> (<explicit columns>) TO STDOUT` (text format) into the archive;
restore with `COPY <table> (<columns>) FROM STDIN`. Bundle with the `cv-data` files into one `.zip`.

**Rationale**:
- **Mode-agnostic** — the decisive constraint. There are two supported run modes (R5): dev host-process
  (`./start.ps1`, Postgres in the `jobs-postgres` container) and the shipped `docker-compose` `app`
  container (`mcr.microsoft.com/dotnet/aspnet:10.0`). The app image has **no** `pg_dump`/`pg_restore`
  and **no** docker socket mounted (compose mounts only `jobs_cvdata`). `COPY` runs over the existing
  Npgsql connection (`GetConnectionString("AppDb")`) in **both** modes; nothing else does.
- **Converter-free fidelity** — `COPY` reads/writes each column as its **raw on-disk text**, never
  hydrating an EF entity. This sidesteps every mapping hazard found in the data inventory (R2): the 8
  strongly-typed-ID value converters, the `Currency` value object with a *private validated ctor* inside
  the `offers.salary_bands` jsonb (needs `CurrencyJsonConverter` through EF), enum→string conversions,
  owned-type column flattening, and wrapped IDs embedded inside jsonb arrays
  (`role_group.member_offer_ids`, `scan_run.source_ids`). Raw-text copy is exact.
- **No new dependency / YAGNI** — `Npgsql` and `System.IO.Compression` are already referenced.
- **Consistent point-in-time** — issue every table's `COPY … TO STDOUT` inside one transaction opened
  `BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY`, giving a single MVCC snapshot across all tables.

**Alternatives rejected**:
- **Shell out to host `pg_dump`/`pg_restore`** — `pg_dump` is *not* a documented host prerequisite
  (README lists only .NET SDK, Node, Docker Desktop); a host client of a non-17 major version can refuse
  a PG17 dump. Mode-fragile and external to the deployment guarantee.
- **`docker exec` / `docker compose exec pg_dump` into `jobs-postgres`** — works only in dev host-mode;
  **impossible** inside the shipped `jobs-app` container (no client, no docker socket). A backup that
  only works in one of two supported modes is unacceptable.
- **Physical volume / data-dir file copy** — needs Postgres stopped (or risks a torn copy), is
  unreachable from the app process, and is tightly coupled to the PG17 on-disk layout + OS.
- **EF-entity serialization (load DbSets → re-serialize)** — re-hydrates every value converter; the
  `Currency` private ctor and the strongly-typed IDs make this fragile with a real silent-corruption
  surface.
- **Reuse the existing `/api/export` (json/csv)** — covers offers+statuses+history only, materializes
  the whole payload in memory as `byte[]`, and omits CV PDFs and most tables. Incomplete by construction.

---

## R2 — What a complete backup must capture (data inventory)

**Decision**: Capture **two stores**: (a) all **12 application tables** and (b) the entire **`cv-data`
directory**. `__EFMigrationsHistory` is **not** dumped as data — its tip is recorded in the manifest
(R4) and the live history row is preserved on restore.

**The 12 tables** (`AppDbContext` DbSets) and the restore dependency order:
- **Tier 1** (no outgoing hard FK): `job_source`, `scan_run`, `role_group`, `schedule_config`,
  `app_settings`, `candidate_cv`.
- **Tier 2**: `offers` (soft, *un-enforced* references to `job_source` and `role_group`).
- **Tier 3** (hard `ON DELETE CASCADE` FK → `offers.id`): `offer_observation`, `offer_version`,
  `offer_event`, `offer_enrichment` (1:1), `offer_fit` (1:1).

**Mapping facts that shape the mechanism** (all argue for raw `COPY`, R1):
- **PKs are client-assigned** (`Guid.NewGuid()`, every config calls `ValueGeneratedNever`); two int
  singletons (`app_settings.id=1`, `schedule_config.id=1`) are app-assigned. **No identity/serial
  sequences** (the global `UseIdentityByDefaultColumns` annotation is overridden everywhere; DDL creates
  no serial column) and **no concurrency tokens** (no `xmin`/rowversion). ⇒ explicit-value reload is
  safe with **no `setval`/`RESTART IDENTITY` resync**.
- **jsonb is the authoritative form** for: `job_source.search_criteria`; `offers.salary_bands`
  (+`Currency`), `required_skills`, `nice_skills`; `offer_version.snapshot`; `offer_event.payload`;
  `scan_run.source_ids`; `candidate_cv.profile`; `app_settings.{salary_norm,scoring_weights,profile_prefs,
  enrichment}`; `role_group.member_offer_ids`; `offer_enrichment.key_skills`; `offer_fit.{matched,missing}`.
- **Owned types are flattened** into parent columns (no child tables) — `Offer.ExternalRef`,
  fingerprints on offer/observation/version, `ScanRun.Counts`.
- **Two UNIQUE indexes** a reload must not violate: `offers (source_id, native_key)`; `scan_run
  (window_utc, trigger)`. A **full TRUNCATE + reload** avoids collisions (never merge into a non-empty DB).
- **Soft (un-enforced) references** — `offers.source_id+native_key → job_source`,
  `offer_observation.scan_run_id → scan_run`, `offers.role_group_id`, and the jsonb membership arrays
  have **no** FK constraint. The DB will not stop an inconsistent *partial* restore; because we always
  restore the **full** snapshot and preserve all PKs verbatim, references stay valid. Validation (R6)
  cross-checks `candidate_cv.file_name` ↔ archived files.

**CV files** (the second store): `LocalCvFileStore` stores PDFs under `Cv:StoragePath` ??
`{AppContext.BaseDirectory}/cv-data`, named `{CvId:N}{ext}`. **PDF bytes are not in the DB** — only the
stored filename + the AI `CvProfile` jsonb. A DB-only backup leaves `candidate_cv` rows pointing at
missing files (and the startup backfill silently skips them). ⇒ the `cv-data` directory **must** be in
the archive, resolved via the *same* `Cv:StoragePath` logic.

---

## R3 — Backup consistency & non-destructiveness

**Decision**: Quiesce-then-snapshot is **not** required for backup. A backup runs **concurrently** with
scans/enrichment and gets DB consistency from one `REPEATABLE READ READ ONLY` transaction (R1). It is
inherently **read-only** to live data (FR-003): `COPY … TO STDOUT` and reading `cv-data` mutate nothing.

**Cross-store skew**: because backup does not pause writers, a CV uploaded/deleted during the brief
backup window could be slightly out of step with the MVCC DB snapshot. This is bounded and acceptable:
the manifest records a per-file inventory and the **"file referenced in DB but missing on disk / orphan
file"** edge case (spec) is handled by capturing what exists and recording the discrepancy — not by
failing. Single-user cadence makes a concurrent CV write during a sub-minute backup unlikely.

**Completeness, not partial** (FR-006): the archive is built to a **server-side temp file**; only a fully
written, closed archive is streamed to the browser. A failure mid-build returns an error envelope and
deletes the temp — a truncated archive is never presented as a complete backup.

---

## R4 — Archive format & manifest

**Decision**: one **unencrypted `.zip`** (FR-019) built with `System.IO.Compression.ZipArchive`:

```
jobs-backup-<utcstamp>.zip
├── manifest.json
├── db/
│   ├── job_source.copy            # COPY-text payload, one file per table (the 12 tables of R2)
│   ├── offers.copy
│   └── … (10 more)
└── cv-data/
    └── <cvId:N>.<ext>             # raw PDF bytes for every CV in the store
```

`manifest.json` fields:
- `backupFormatVersion` (int) — this feature's archive schema version.
- `createdAtUtc` (ISO-8601).
- `appProductVersion` / `efVersion` (`10.0.9`).
- `migrationTip` — `db.Database.GetAppliedMigrationsAsync()` **last** id (the `__EFMigrationsHistory`
  tip at backup time), e.g. `20260629155706_AppliedFlag`. **Load-bearing for FR-017 (R5).**
- `tables[]` — ordered `{ name, columns[], rowCount }`. The **explicit column list per table** is captured
  at backup time and used verbatim in `COPY … FROM STDIN (cols)` on restore, so an older backup missing
  newer columns lets those columns take their DDL defaults; `rowCount` (known from the `COPY` at backup
  time) records the per-category counts in the manifest (FR-005), so inspect/verify needs no payload parsing.
- `cvFiles[]` — `{ name, size, sha256 }` and a `count` (the integrity + completeness inventory, FR-005).

**Rationale**: self-describing and human-inspectable (Principle IX); the per-table column list is what
makes cross-version restore safe without schema reversal; `.zip` needs no dependency and streams well.

---

## R5 — Cross-version restore (FR-017)

**Decision**: drive compatibility off **migration ids**, never off schema reversal. The running binary
has already `MigrateAsync`-ed to **HEAD** at startup, so the restore target is HEAD. Compare
`manifest.migrationTip` to `db.Database.GetMigrations()` (the ordered ids *this build* knows):
1. `tip == HEAD` → **restore directly**.
2. `tip` present in `GetMigrations()` but earlier than HEAD → **OLDER**: load the data-only snapshot into
   the HEAD schema using the backup's per-table columns (omitted newer columns take DDL defaults —
   e.g. `offers.applied=false`, `candidate_cv.profile_state='Pending'`, `app_settings.enrichment` default
   jsonb; newer tables like `offer_enrichment`/`offer_fit` start empty), then run the **idempotent
   enrichment backfill** to synthesise the missing `Pending` satellite rows.
3. `tip` **not** in `GetMigrations()` → **NEWER**: **refuse** with a clear message — this binary cannot
   represent that schema and runtime has no `Down` path.

**Why this is sound**: every migration to date is **additive with safe defaults** (the only `DropColumn`
removed a recomputable keyword cache). Reverting via `MigrateAsync(target)` would run `Down`
(`DropColumn`/`DropTable`) and destroy data — never done.

**Guard**: a test asserts the additivity invariant (no serial/identity column; no non-defaulted
`NOT NULL` added without a default) so a future schema change that breaks "older snapshot into HEAD"
**fails the build**, not a user's restore.

**Seed/backfill reconciliation**: after restore, the next startup re-runs `DatabaseSeeder` (3 default
sources by fixed GUID) and `BackfillEnrichmentAsync` (Pending satellite rows for offers lacking them).
A full backup includes those rows ⇒ both stay no-ops; for older backups, backfill intentionally gap-fills.

---

## R6 — Restore safety: all-or-nothing, validation, safety pre-backup, quiescing

**Decision** — restore sequence (all under the `MaintenanceGate`, R-quiescing below):
1. **Acquire** the `MaintenanceGate` (serialize; second op → 409) and set maintenance-active; **drain**
   the in-flight scan by acquiring `ScanConcurrencyGuard` (blocking).
2. **Validate before touching anything**: manifest present + parseable; `backupFormatVersion` understood;
   version compatibility (R5: refuse NEWER); every `cv-data` file matches its manifest `sha256`;
   `candidate_cv.file_name` set ↔ archived file set cross-check; required `db/*.copy` present. Reject a
   corrupt/incomplete/non-backup upload here — **live data still intact** (FR-011).
3. **Safety pre-backup**: run the full backup routine into the server-side `backups/` dir (R-delivery);
   return/log its path. This is the rollback source (FR-009).
4. **Stage files**: extract `cv-data` into a sibling temp dir (`cv-data.incoming-<ts>`); verify hashes.
   Live `cv-data` untouched so far.
5. **One DB transaction**: `TRUNCATE <12 data tables> RESTART IDENTITY CASCADE` (excluding
   `__EFMigrationsHistory`); `COPY … FROM STDIN` per table in dependency order (R2) — or
   `session_replication_role='replica'` to defer FK checks — using the backup's column list.
6. **Atomic file swap**: rename live `cv-data → cv-data.old-<ts>`, then `cv-data.incoming → cv-data`.
7. **Commit last**: `COMMIT` the DB transaction; delete `cv-data.old`.
8. **On any failure**: `ROLLBACK` (Postgres DML is fully transactional — TRUNCATE+COPY revert); if the
   file swap already happened, swap back from `cv-data.old`; the safety pre-backup is the final fallback.
9. Release the gate; if the backup was OLDER, run the idempotent enrichment backfill (R5).

**Quiescing (FR-020) — `MaintenanceGate`**: a new process-wide Application singleton (registered beside
`ScanConcurrencyGuard`) with (1) a `SemaphoreSlim(1,1)` (one backup/restore at a time), (2) a
maintenance-active flag writers consult, (3) restore drains the in-flight scan via `ScanConcurrencyGuard`.
`ScanConcurrencyGuard` is **not** reused for this because it is scan-only and non-blocking. Gate consult
points (the true write entry points):
- `ScanSchedulerService.TickAsync` — defer the tick if maintenance active (mirrors the existing
  "scan already running ⇒ retry next tick").
- `ScanOrchestrator.RunAsync` — check beside the existing `ScanConcurrencyGuard.TryEnterAsync` (covers
  **both** the manual `/scans/run` and the scheduler).
- `EnrichmentService.SubmitResultsAsync` **and** `TriggerRerunAsync` — the only enrichment write entry
  points. A `SaveChanges` chokepoint is **insufficient**: the repo's `InvalidateAllFitsAsync`/
  `RearmFailedAsync`/`ForceAllPendingAsync` use `ExecuteUpdateAsync`, bypassing `SaveChanges`.

A **backup** acquires only the `SemaphoreSlim` (serialise vs other backup/restore) and does **not** set
maintenance-active — it runs concurrently with scans/enrichment (MVCC consistency, R3).

---

## R-runtime — Run modes, ports, delivery & connection (supporting facts)

- **Run modes**: dev host process `./start.ps1` (Postgres in `jobs-postgres`, host app on `:5180`,
  Vite `:5173`); published host (`:5000`); full `docker-compose` (`jobs-app` on `:8080`). Connection via
  `IConfiguration.GetConnectionString("AppDb")` (user-secrets in dev; `ConnectionStrings__AppDb` env in
  compose) — backup tooling must read it through configuration, not assume a source.
- **Delivery (ADR-4)**: browser download. `GET /api/backup` builds to a temp file then streams via
  `Results.File(stream/path, "application/zip", fileName)`. The automatic **safety pre-backup** is written
  server-side under `Backup:StoragePath` ?? `{AppContext.BaseDirectory}/backups` (new gitignored dir);
  not streamed to the browser. Restore is initiated by **uploading** a previously downloaded archive.
- **Web/DI seams**: add `BackupEndpoints` (`internal static class`, `MapBackupEndpoints` →
  `api.MapGroup("/backup")`), register in `FeatureEndpoints`. Reuse `.AddEndpointFilter<LoopbackOnlyFilter>()`
  (the established PII control) on the group. Upload endpoints need `.DisableAntiforgery()` and a **raised
  body-size limit** (default Kestrel ~30 MB would reject a real archive — nothing is configured today).
  `ICvFileStore` has no enumerate method ⇒ add `EnumerateAll()` for the archive.
- **Frontend seams**: hand-rolled typed `fetch` client (`api/client.ts`, `api.get/post/upload`,
  `ApiError{code,message,status}`, `/api`→`:5180` proxy). Backup = `fetch('/api/backup')` → `blob` →
  synthetic `<a download>` click (filename from the backend's `Content-Disposition`), so the JS sees
  completion/error and can drive a busy state + success/failure `settings-msg` (FR-006/016) — a plain
  `<a href download>` cannot report completion or failure. Restore upload = `FormData` + `api.upload`
  (mirrors `cv.ts`/`CvPage`). Home =
  a new `BackupSection` card in `SettingsPage` (a stack of `.card .settings-card`). **No toast** — use
  inline `settings-msg--error|--ok` (`role="alert"`/`"status"`). **No generic confirm dialog** — build
  `RestoreConfirmModal` on the `ApplyModal` portal+focus-trap pattern. **No `.btn--danger`** — add one in
  `base.css` using `--c-danger`/`--c-danger-bg`. Progress = the existing `busy`-flag + `.spinner` +
  disabled-button idiom (synchronous restore needs no `poll`).

---

## Open risks carried into design / tasks

- **Additivity invariant is an assumption, not enforced** until the guard test ships (R5). It must land
  with US2 (restore), since cross-version correctness depends on it.
- **Upload body-size limit** must be explicitly raised on `/api/backup/restore` and `/api/backup/inspect`, or a
  real archive upload fails (R-runtime).
- **CV directory differs by mode** (`{bin}/cv-data` in dev vs `/app/cv-data` volume in compose) — always
  resolve via the `Cv:StoragePath` logic; never hard-code a repo-root path (R2).
- **`ExecuteUpdateAsync` enrichment writes bypass `SaveChanges`** — gate at the two `EnrichmentService`
  methods, not at `SaveChanges` (R6).
