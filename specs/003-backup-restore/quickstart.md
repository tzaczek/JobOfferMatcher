# Quickstart & Validation: On-Demand Backup & Restore

**Date**: 2026-06-29 | **Feature**: `003-backup-restore` | **Plan**: [plan.md](./plan.md)

A run-and-verify guide proving the feature end-to-end. Implementation detail lives in the plan/data-model/
contracts; this is the validation script mapped to the spec's user stories and success criteria.

## Prerequisites

- Same as the project baseline: .NET 10 SDK, Node, Docker Desktop running; `.env` present;
  `ConnectionStrings:AppDb` in user-secrets (see README "First-time setup").
- Run the app the standard way: `./start.ps1` (Postgres in the `jobs-postgres` container + the host
  process on `http://localhost:5180`). The UI is at `:5180`.
- Have some data present: run a scan (Offers → "Run scan") so there are offers, and upload a CV on the
  **CV & Profile** page, so a backup has both stores to capture. Optionally run `/enrich` so enrichment/
  fit rows exist.

## Where it lives

- **UI**: Settings page → a new **"Backup & Restore"** card (last section), with a **Backup** download
  button and a **Restore** file-picker + confirm flow.
- **API** (loopback-only): `GET /api/backup`, `POST /api/backup/inspect`, `POST /api/backup/restore`
  (see [contracts/backup-api.md](./contracts/backup-api.md)).

---

## US1 — Create a complete backup (P1) · FR-001/002/003/004/006 · SC-001/SC-002

1. On Settings → Backup & Restore, click **Backup**.
2. **Expect**: a `jobs-backup-<utcstamp>.zip` downloads; the button shows a busy/disabled state while it
   builds, then a success `settings-msg` with the timestamp and a content summary.
3. Open the zip. **Expect** `manifest.json`, a `db/<table>.copy` for **all 12 tables**, and a `cv-data/`
   folder containing **every** uploaded CV PDF (SC-001: zero categories/files omitted). The manifest
   lists per-table column lists **and row counts**, the migration tip, and the CV inventory with sizes + SHA-256.
4. **Non-destructive (SC-002)**: confirm the app stayed usable during the backup (the feed still loads,
   a scan can still run — backup does not block them) and the live data is unchanged afterward.
   - CLI cross-check (dev): the integration test asserts a row-count/`md5(table)` comparison before vs
     after a backup is byte-identical.
5. **Empty-data edge**: on a fresh DB (no offers, no CV), clicking Backup still produces a valid archive
   with empty `db/*.copy` payloads and an empty `cv-data/` (manifest `cvFileCount: 0`).
6. **Failure surfacing (FR-006)**: the archive is built to a temp file first; if the build fails, an
   error `settings-msg` is shown and **no** partial file downloads.

## US3 — Verify a backup without restoring (P3) · FR-005 · SC-008

> Validated before US2 because it is the read-only safety check restore depends on.

1. In the Restore flow, pick a previously downloaded `.zip`. The UI first calls
   `POST /api/backup/inspect` and shows a **summary**: when it was taken, per-table counts, CV file
   count, the source version, and a **compatibility** badge (`Same` / `Older` / `Newer`).
2. **Expect**: the summary appears with **no change to live data** (re-open the feed — unchanged).
3. **Corrupt/foreign file**: pick the offers `export.json`, a random file, or a truncated zip.
   **Expect**: an inline error ("not a valid backup / not safe to restore") and the **Restore** action
   stays disabled. Live data untouched.

## US2 — Restore from a backup (P2) · FR-008..FR-017 · SC-003/004/005/006

1. Take a backup (US1). Then change state: dismiss/apply a few offers, edit settings, or delete the CV.
2. In Backup & Restore, pick the backup `.zip`; review the inspect summary (US3); click **Restore**.
3. **Expect a confirm modal** (`RestoreConfirmModal`) warning that current data will be **replaced**.
   Cancel → nothing happens. Confirm → the restore runs (busy/progress state).
4. **Expect on success** (`RestoreReportDto`): a success message naming the **server-side safety backup
   path** (under `backups/`) and per-table restored counts. Re-open the app:
   - Every offer/status/setting/CV present at backup time is back, identical (SC-004 round-trip: zero
     loss); restored counts == backed-up counts (FR-013); the restored CV PDF opens.
   - No non-backup data lingers (full replace).
5. **Safety net (SC-006)**: confirm a `jobs-safety-<ts>.zip` now exists in the `backups/` dir — the
   pre-restore state is recoverable by restoring *that*.
6. **All-or-nothing (FR-011/012)**:
   - Restore a corrupt archive → **422/400** before any write; live data intact.
   - (Integration test) inject a mid-restore failure → DB transaction rolls back and `cv-data` swaps
     back; live state is byte-identical to before; the safety backup exists.
7. **Cross-version (FR-017)**:
   - **Older** backup (taken before a later migration) → restores into the current schema; new columns
     take defaults and the enrichment backfill synthesises `Pending` rows (`backfillApplied: true`).
   - **Newer** backup (from a future app version) → **refused** with `IncompatibleNewer`; live data
     untouched.
8. **Locality (SC-005)**: backup/restore make **0** external calls — verify no outbound network traffic;
   the `/api/backup/*` group rejects any non-loopback caller (403 `LoopbackOnly`).

## FR-020 — Serialization & quiescing

1. Trigger a backup, then immediately trigger another backup/restore → the second returns **409**
   (`BusyMaintenance`).
2. (Integration test) start a restore while the scheduler tick / an enrichment write-back is pending →
   the restore drains/pauses them (`ScanSchedulerService.TickAsync`, `ScanOrchestrator.RunAsync`,
   `EnrichmentService.SubmitResultsAsync`/`TriggerRerunAsync` honour `MaintenanceGate`); they resume
   after the restore completes.

## SC-009 — Performance

- With a typical data set (≤ ~10k offers + a few CVs), a backup and a restore each complete in **≤ ~60s**
  with a visible busy/progress state; larger sets are not rejected by a hard cap.
- **Observed (integration suite, 2026-06-30):** a seeded data set (2 offers + their version/event/
  observation/enrichment/fit satellites + 1 CV file) completes a full **backup → wipe → restore** round
  trip in **~1 s** end-to-end through the real HTTP endpoints against real PostgreSQL
  (`RestoreRoundTripTests`); the entire 14-test backup suite runs in **~11 s**. The mechanism is
  build-to-temp-then-stream for backup and a single `TRUNCATE`+`COPY` transaction for restore, so wall-clock
  scales with data size (no hard cap) and the single-user target has wide headroom under 60 s. The
  frontend shows a busy state ("Preparing backup…" / the confirm modal's "Restoring…") throughout.

---

## Automated tests (Principle V — real Postgres via Testcontainers)

- **Round-trip fidelity**: seed data exercising every tricky column (`offers.salary_bands` `Currency`,
  `role_group.member_offer_ids` jsonb, all enum/owned/jsonb columns) → backup → wipe (`PostgresFixture`
  TRUNCATE-except-history) → restore → assert per-row equality + CV file bytes.
- **OLDER→HEAD**: a backup missing newer columns/tables restores into HEAD; new columns take defaults;
  backfill synthesises `Pending` satellite rows.
- **NEWER refusal**: manifest tip not in `GetMigrations()` → refused; live data intact.
- **All-or-nothing failure**: injected failure → rollback + file swap-back; live state untouched; safety
  backup present.
- **Validation**: corrupt/incomplete/non-backup upload rejected before any write.
- **Non-destructive backup**: data before == after.
- **Additivity guard**: assert no serial/identity column exists (the explicit-PK reload invariant).
- **Loopback guard**: `/api/backup/*` rejects a non-loopback remote IP (HTTP-layer test).
- **Frontend (Vitest + RTL)**: backup button (fetch→blob), inspect-then-confirm flow, busy/progress, and
  `settings-msg` success/error rendering.

## Done when

- All three user stories validate in the running app (Principle VII), the real-Postgres suite is green
  (Principle VI), and a full **back up → wipe → restore** round trip shows **zero data loss** across both
  stores (SC-004).
