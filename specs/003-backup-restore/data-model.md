# Phase 1 Data Model: On-Demand Backup & Restore

**Date**: 2026-06-29 | **Feature**: `003-backup-restore` | **Plan**: [plan.md](./plan.md)

This feature adds **no database tables or columns** and **no EF migration** (Principle IX/X). Its "data
model" is (1) the on-disk **archive** format, (2) the **Application-layer** records/ports/policies that
orchestrate it, and (3) the **restore state machine** + the **`MaintenanceGate`**. The authoritative DB
contents it transports are the existing 12 tables (inventory below).

---

## 1. Archive (on-disk artifact)

A single unencrypted `.zip` (FR-019). See [contracts/backup-api.md](./contracts/backup-api.md) for the
byte-level spec; summary:

```
jobs-backup-<utcstamp>.zip
├── manifest.json          # see §2
├── db/<table>.copy        # one Postgres COPY-text payload per data table (§3 order)
└── cv-data/<cvId:N>.<ext> # raw PDF bytes for every stored CV
```

- **`db/<table>.copy`** — the exact bytes of `COPY <table> (<cols>) TO STDOUT` (text format, default
  delimiter, `\N` null). Restored verbatim via `COPY <table> (<cols>) FROM STDIN`. Raw column text ⇒ no
  EF converter is involved (research R1).
- **`cv-data/`** — every file enumerated from `ICvFileStore.EnumerateAll()`; names are the existing
  `{CvId:N}{ext}` so they line up with `candidate_cv.file_name`.

---

## 2. `BackupManifest` (Application record; serialized as `manifest.json`)

| Field | Type | Notes |
|-------|------|-------|
| `backupFormatVersion` | `int` | Archive schema version owned by this feature (start at `1`). Restore refuses an unknown/greater value. |
| `createdAtUtc` | `DateTimeOffset` | When the snapshot transaction opened (via `TimeProvider`). |
| `appProductVersion` | `string` | App/EF product version, e.g. `10.0.9`. Informational. |
| `migrationTip` | `string` | `db.Database.GetAppliedMigrationsAsync()` **last** id. **Load-bearing for FR-017.** |
| `tables` | `BackupTable[]` | Ordered (§3). Each `{ name: string, columns: string[], rowCount: long }`. The column list is replayed verbatim into `COPY … FROM STDIN (columns)`; `rowCount` records the per-category counts in the manifest (FR-005), so inspect reads counts without parsing payloads. |
| `cvFiles` | `CvFileEntry[]` | Each `{ name: string, size: long, sha256: string }`. |
| `cvFileCount` | `int` | Convenience count (also = `cvFiles.Length`). |

Derived records: `BackupTable(string Name, string[] Columns, long RowCount)`, `CvFileEntry(string Name,
long Size, string Sha256)`. All immutable records (Principle II). `migrationTip` stays a plain string — it
is an external EF identifier, not a domain concept (Principle X).

**Validation rules** (applied by `RestoreService` before any write — FR-011):
- `manifest.json` present, parseable, `backupFormatVersion` understood.
- Compatibility (`BackupCompatibility`, §5) ≠ `Newer` (else refuse).
- For every `cvFiles[i]`: the archived `cv-data/<name>` exists and its SHA-256 == `sha256`.
- The set of `candidate_cv.file_name` values in `db/candidate_cv.copy` ⊆ archived `cv-data` names
  (a referenced-but-absent CV file fails the restore *before* touching live data); orphan archived files
  not referenced by any row are recorded as a discrepancy (non-fatal — they are still restored).
- Every `tables[i].name` is one of the 12 known tables; required `db/*.copy` entries present.

---

## 3. Transported DB inventory (existing tables — restore order)

No schema change; this is the set `COPY` reads/writes. Order = **insert** order (reverse for truncate,
though `TRUNCATE … CASCADE` handles dependents).

| # | Tier | Table | Restore notes |
|---|------|-------|---------------|
| 1 | 1 | `job_source` | jsonb `search_criteria`; includes the 3 fixed-GUID seeded defaults. |
| 2 | 1 | `scan_run` | UNIQUE `(window_utc, trigger)`; owned `Counts`; jsonb `source_ids`. |
| 3 | 1 | `role_group` | jsonb `member_offer_ids` (wrapped IDs); `MatchConfidence`↔double; nullable enum. |
| 4 | 1 | `schedule_config` | int singleton PK `=1`. |
| 5 | 1 | `app_settings` | int singleton PK `=1`; 4 jsonb columns (`salary_norm`/`scoring_weights`/`profile_prefs`/`enrichment`). |
| 6 | 1 | `candidate_cv` | `file_name`→disk; jsonb `profile`; `profile_state` enum; `enrichment_input_hash`. |
| 7 | 2 | `offers` | UNIQUE `(source_id, native_key)`; owned `ExternalRef`/fingerprint; jsonb `salary_bands` (+`Currency`), `required_skills`, `nice_skills`; soft `role_group_id`. |
| 8 | 3 | `offer_observation` | hard FK→`offers` (CASCADE); owned fingerprint; soft `scan_run_id`. |
| 9 | 3 | `offer_version` | hard FK→`offers`; jsonb `snapshot`; owned fingerprint; `change_tier` enum. |
| 10 | 3 | `offer_event` | hard FK→`offers`; raw jsonb `payload`; `type` enum. |
| 11 | 3 | `offer_enrichment` | 1:1 hard FK (PK=`offer_id`); jsonb `key_skills`; `state` enum. |
| 12 | 3 | `offer_fit` | 1:1 hard FK (PK=`offer_id`); jsonb `matched`/`missing`; `state` enum. |

Invariants the restore relies on (research R2):
- **All PKs client-assigned** (`ValueGeneratedNever`); **no serial/identity sequences**, **no concurrency
  tokens** ⇒ explicit-value `COPY` needs no `setval`/sequence resync. A **guard test asserts** no
  serial/identity column exists, so a future migration cannot silently break this.
- `__EFMigrationsHistory` is **excluded** from the data set: not truncated, not reloaded (the running
  binary's HEAD history is authoritative). Its tip is only *recorded* in the manifest.

---

## 4. Application services & ports

```
Application/Backup/
  BackupService          // orchestrates: gate → snapshot → archive (→ stream); also the safety-backup routine
  RestoreService         // orchestrates: gate+drain → validate → safety pre-backup → stage → tx wipe+load → swap → commit
  BackupManifest         // §2 record (+ BackupTable, CvFileEntry)
  BackupCompatibility    // §5 enum + pure comparison policy
  RestoreReport          // result DTO: per-table restored counts, cv file count, compatibility, safetyBackupPath
  IDatabaseSnapshotStore // port: SnapshotAsync(writer) [COPY TO]; RestoreAsync(reader, tables) [TRUNCATE+COPY FROM in one tx]
  IBackupArchiveStore    // port: write/read the .zip (manifest + db/*.copy + cv-data/*)
  ISafetyBackupStore     // port: write a backup to the local backups/ dir, return its path
  IMigrationInspector    // port: AppliedTipAsync(); KnownMigrations()  (wraps EF GetAppliedMigrationsAsync/GetMigrations)
Application/Scanning/
  MaintenanceGate        // NEW singleton (§6)
```

- `BackupService`/`RestoreService` registered **Scoped** (alongside `ExportService`/`CvService`);
  `MaintenanceGate` registered **Singleton** (beside `ScanConcurrencyGuard`).
- Ports implemented in `Infrastructure/Backup` (`PostgresSnapshotStore` via Npgsql `COPY`,
  `ZipBackupArchiveStore`, `LocalSafetyBackupStore`, `EfMigrationInspector`). `ICvFileStore` gains
  `EnumerateAll()` (impl in `LocalCvFileStore`).
- Commands return `Result<T>` for expected failures: `BusyMaintenance`, `InvalidArchive`,
  `IncompatibleNewer`, `MissingCvFile`, `HashMismatch`. Mapped to HTTP via the existing `ResultExtensions`
  `{ error: { code, message } }` envelope.

---

## 5. `BackupCompatibility` (pure policy)

```
enum BackupCompatibility { Same, Older, Newer }

Decide(string backupTip, IReadOnlyList<string> knownMigrations):
  if (!knownMigrations.Contains(backupTip)) return Newer;   // unknown to this build → refuse
  if (backupTip == knownMigrations[^1])      return Same;    // == HEAD → restore directly
  return Older;                                              // known but earlier → load into HEAD + backfill
```

`knownMigrations` is the ordered `db.Database.GetMigrations()`; HEAD is its last element. Unit-tested in
isolation (no DB). `Older` triggers the idempotent enrichment backfill after load (research R5).

---

## 6. `MaintenanceGate` (FR-020)

Process-wide singleton coordinating backup/restore vs. background and request-path writers.

State / API (sketch):
- `SemaphoreSlim(1,1) _slot` — at most one backup **or** restore at a time.
- `volatile bool _maintenanceActive` — true only for the duration of a **restore**.
- `bool TryBeginBackup()` / `void EndBackup()` — acquire/release `_slot` only (backup runs concurrently
  with scans). Non-blocking try; a second op returns busy → HTTP 409.
- `bool TryBeginRestore()` / `void EndRestore()` — acquire `_slot`, set `_maintenanceActive=true`;
  release clears it. The restore additionally **blocks-acquires** `ScanConcurrencyGuard` to drain an
  in-flight scan, releasing it when done.
- `bool IsMaintenanceActive { get; }` — consulted by writers.

**Consult points** (writers reject/defer when `IsMaintenanceActive`):
- `ScanSchedulerService.TickAsync` — defer the tick (like the existing "scan already running" path).
- `ScanOrchestrator.RunAsync` — check beside `ScanConcurrencyGuard.TryEnterAsync`; covers manual
  `/scans/run` **and** the scheduler. Returns a `ScanInProgress`-style result.
- `EnrichmentService.SubmitResultsAsync` and `TriggerRerunAsync` — the only enrichment write entry points
  (the repo's `ExecuteUpdateAsync` calls bypass `SaveChanges`, so gating must be here, not at SaveChanges).

Lighter request-path writers (CV upload/delete, settings, offer status/applied, sources, schedule) are
short and naturally serialized by the restore's brief window; gating them is **optional** (FR-020 reads
as "pause scanning + enrichment writes"). If added, they return the standard busy error.

---

## 7. Restore state machine (all-or-nothing — FR-011/FR-012/FR-013)

```
            ┌─────────┐
            │  Idle   │
            └────┬────┘
                 │ POST /backup/restore (upload)
            ┌────▼─────────┐  gate busy → 409 (BusyMaintenance)
            │ Acquiring    │──────────────────────────────────────────┐
            │ gate + drain │                                           │
            └────┬─────────┘                                           │
                 │ acquired (_maintenanceActive=true; scan drained)    │
            ┌────▼────────┐  invalid/corrupt/newer → reject            │
            │ Validating  │───────────────────────────► [Idle] live    │
            └────┬────────┘   data UNTOUCHED (FR-011)    intact         │
                 │ valid                                                │
            ┌────▼─────────────┐                                       │
            │ Safety pre-backup│ (write to backups/ dir; FR-009)        │
            └────┬─────────────┘                                       │
            ┌────▼───────────────┐                                     │
            │ Stage cv-data temp │ (verify hashes; live cv-data intact) │
            └────┬───────────────┘                                     │
            ┌────▼──────────────────────────┐  any failure             │
            │ DB tx: TRUNCATE + COPY FROM    │──────────────► ROLLBACK ─┤
            └────┬──────────────────────────┘                          │
            ┌────▼──────────────┐  swap fails → swap back + ROLLBACK ───┤
            │ Atomic file swap   │                                      │
            └────┬──────────────┘                                       │
            ┌────▼────────┐  commit fails → ROLLBACK + swap back ───────┘
            │ COMMIT DB   │
            └────┬────────┘
                 │ Older? → idempotent enrichment backfill
            ┌────▼─────────────────┐
            │ Done: RestoreReport  │  release gate
            └──────────────────────┘
```

Failure semantics: the DB transaction makes TRUNCATE+COPY atomic (Postgres DML is transactional); the
file swap is rename-based (atomic on the same volume) with a kept `cv-data.old-<ts>` for swap-back; the
server-side safety pre-backup is the final fallback. **Live state is never left partially restored.**

---

## 8. What does NOT change

- No Domain types, no DbSet, no migration, no `__EFMigrationsHistory` mutation on restore.
- The existing `/api/export` (offers json/csv) is untouched and complementary.
- 001 collection/feed/scheduler and 002 enrichment **behaviour** are unchanged — the only edits to those
  paths are read-only **gate checks** (a guard), not logic changes.
