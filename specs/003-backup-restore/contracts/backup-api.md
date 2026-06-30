# Contract: Backup & Restore API + Archive Format

**Date**: 2026-06-29 | **Feature**: `003-backup-restore` | **Plan**: [plan.md](./plan.md)

All endpoints live under the existing `/api` group and a new `MapGroup("/backup")` registered in
`FeatureEndpoints`. The **entire group is loopback-only** — `.AddEndpointFilter<LoopbackOnlyFilter>()` —
because the payload is the full DB + CV PII (Principle IV). Errors use the established
`{ "error": { "code": string, "message": string } }` envelope via `ResultExtensions`.

> Naming note: routes are grouped under `/api/backup` — create `GET /api/backup`, inspect
> `POST /api/backup/inspect`, restore `POST /api/backup/restore`. These are the canonical paths used
> consistently across the plan, data-model, and tasks.

---

## 1. `GET /api/backup` — create & download a full backup (US1, FR-001/004/006)

Builds the archive to a **server-side temp file** (complete-or-error, FR-006), then streams it.

- **Auth**: loopback-only.
- **Query**: none.
- **Behaviour**: acquire `MaintenanceGate.TryBeginBackup()` (409 if busy); open one
  `REPEATABLE READ READ ONLY` transaction; `COPY … TO STDOUT` for all 12 tables; add `cv-data` files;
  write `manifest.json`; close the zip; stream it; delete the temp; release the gate.
- **200 OK**: `Content-Type: application/zip`,
  `Content-Disposition: attachment; filename="jobs-backup-<utcstamp>.zip"`, body = the archive bytes
  (streamed via `Results.File`).
- **409** `{ "error": { "code": "BusyMaintenance", "message": "A backup or restore is already running." } }`
- **500** (build failed before streaming) — error envelope; no partial file is sent.

Frontend: to surface success/failure + a busy state (FR-006/016), the UI fetches the archive
(`fetch('/api/backup')`), reads the `blob`, and triggers a synthetic `<a download>` click using the
filename from `Content-Disposition`; a non-OK response is parsed as the `{ error }` envelope and shown via
`settings-msg`. (A plain `<a href download>` cannot report completion or failure.)

---

## 2. `POST /api/backup/inspect` — verify a backup without restoring (US3, FR-005)

- **Auth**: loopback-only. **`.DisableAntiforgery()`**; **raised body-size limit** (archives exceed the
  default ~30 MB Kestrel cap).
- **Request**: `multipart/form-data` with field `file` = the `.zip` (`IFormFile`).
- **Behaviour**: parse + validate the archive (manifest present/parseable; per-file SHA-256;
  `candidate_cv.file_name` ↔ archived files cross-check; compatibility via `BackupCompatibility`); table
  counts are read from the manifest's per-table `rowCount` (no payload parsing). **Reads the upload only —
  never touches live data.**
- **200 OK** (`BackupInspectionDto`):
  ```json
  {
    "valid": true,
    "createdAtUtc": "2026-06-29T14:32:10Z",
    "appProductVersion": "10.0.9",
    "migrationTip": "20260629155706_AppliedFlag",
    "compatibility": "Same",           // "Same" | "Older" | "Newer"
    "tableCounts": { "offers": 184, "candidate_cv": 1, "...": 0 },
    "cvFileCount": 1,
    "totalCvBytes": 524288,
    "warnings": []                      // e.g. ["orphan cv file: ab12… not referenced by any row"]
  }
  ```
- **400** `{ "error": { "code": "InvalidArchive", "message": "…" } }` — not a backup / corrupt /
  unreadable manifest / hash mismatch. `valid:false` is conveyed via the 400 error code (the body is the
  error envelope), so the UI surfaces "not safe to restore."

---

## 3. `POST /api/backup/restore` — restore from an uploaded backup (US2, FR-008..FR-017)

- **Auth**: loopback-only. **`.DisableAntiforgery()`**; **raised body-size limit**.
- **Request**: `multipart/form-data` with field `file` = the `.zip`. The UI obtains explicit user
  confirmation (RestoreConfirmModal) **before** calling this; the destructive action is the POST itself.
- **Behaviour**: the all-or-nothing sequence (data-model §7): acquire `MaintenanceGate.TryBeginRestore()`
  + drain in-flight scan → validate (refuse `Newer`) → **server-side safety pre-backup** → stage cv-data
  temp → one DB tx `TRUNCATE … RESTART IDENTITY CASCADE` (excl. `__EFMigrationsHistory`) + `COPY FROM
  STDIN` per table (backup's column list) → atomic cv-data swap → `COMMIT` → (if `Older`) idempotent
  enrichment backfill → release gate.
- **200 OK** (`RestoreReportDto`):
  ```json
  {
    "restoredAtUtc": "2026-06-29T15:01:44Z",
    "compatibility": "Older",
    "tableCounts": { "offers": 184, "candidate_cv": 1, "...": 0 },
    "cvFileCount": 1,
    "safetyBackupPath": "…/backups/jobs-safety-2026-06-29-150140.zip",
    "backfillApplied": true             // true when compatibility was "Older"
  }
  ```
- **409** `BusyMaintenance` — a backup/restore is already running, **or** an in-flight scan could not be
  drained within the timeout.
- **400** `InvalidArchive` — corrupt/incomplete/non-backup; **live data untouched** (FR-011).
- **422** `IncompatibleNewer`
  `{ "error": { "code": "IncompatibleNewer", "message": "This backup was created by a newer app version (…) and cannot be restored. Update the app first." } }` — **live data untouched** (FR-017).
- **500** — restore failed mid-flight; the response states that live data was rolled back and names the
  safety backup. Postgres rollback + file swap-back guarantee no partial state (FR-012).

---

## 4. Archive format (the `.zip` byte-level spec)

```
jobs-backup-<utcstamp>.zip            # utcstamp = yyyy-MM-dd-HHmmss (UTC); unencrypted (FR-019)
├── manifest.json                     # UTF-8 JSON, camelCase (matches app JSON conventions)
├── db/
│   ├── job_source.copy               # bytes of: COPY job_source (<cols>) TO STDOUT  (text format)
│   ├── scan_run.copy
│   ├── role_group.copy
│   ├── schedule_config.copy
│   ├── app_settings.copy
│   ├── candidate_cv.copy
│   ├── offers.copy
│   ├── offer_observation.copy
│   ├── offer_version.copy
│   ├── offer_event.copy
│   ├── offer_enrichment.copy
│   └── offer_fit.copy
└── cv-data/
    └── <cvId:N>.<ext>                 # raw PDF bytes; name == candidate_cv.file_name
```

`manifest.json`:
```json
{
  "backupFormatVersion": 1,
  "createdAtUtc": "2026-06-29T14:32:10Z",
  "appProductVersion": "10.0.9",
  "migrationTip": "20260629155706_AppliedFlag",
  "tables": [
    { "name": "job_source", "columns": ["id", "kind", "name", "enabled", "search_criteria", "..."], "rowCount": 3 },
    { "name": "offers", "columns": ["id", "source_id", "native_key", "identity_kind", "salary_bands", "..."], "rowCount": 184 }
  ],
  "cvFiles": [
    { "name": "ab12cd34ef56...pdf", "size": 524288, "sha256": "9f86d0818..." }
  ],
  "cvFileCount": 1
}
```

**COPY payload rules**:
- Text format, default delimiter (tab), `\N` for NULL, header **off** (the manifest carries the column
  order). Each `db/<table>.copy` is produced by `COPY <table> (<columns>) TO STDOUT` and consumed by
  `COPY <table> (<columns>) FROM STDIN` using the **manifest's** column list for that table.
- jsonb columns travel as their raw text — no EF converter is invoked (`offers.salary_bands` `Currency`,
  `role_group.member_offer_ids`, etc. are exact).
- `__EFMigrationsHistory` is **not** included as a `db/*.copy` file; its tip is `migrationTip`.

**Restore column-matching (FR-017 forward-compat)**: restore issues `COPY` with the **manifest's**
column list. Columns present in the current HEAD schema but absent from an older backup are simply not
listed, so Postgres applies their DDL defaults. A column present in the backup but absent from HEAD only
occurs for a `Newer` backup, which is refused before this step.

---

## 5. Cross-cutting

- **Loopback**: 403 `{ "error": { "code": "LoopbackOnly", "message": "…" } }` for any non-loopback caller
  (the existing `LoopbackOnlyFilter` behaviour).
- **Idempotency / safety**: `GET /api/backup` and `POST /api/backup/inspect` are read-only w.r.t. live
  data. `POST /api/backup/restore` is the only mutating call and is guarded as above.
- **Concurrency**: a second backup/restore while one runs → 409 `BusyMaintenance` (FR-020). A backup may
  run while a scan/enrichment runs (it does not block them); a restore pauses them.
