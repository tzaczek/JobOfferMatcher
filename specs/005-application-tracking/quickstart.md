# Quickstart — Application & Interview Process Tracking

**Feature**: `005-application-tracking` | **Date**: 2026-07-01 | [spec.md](./spec.md) ·
[plan.md](./plan.md) · [contracts/applications-api.md](./contracts/applications-api.md)

A **run-and-validate** guide (Principle VII: run the app and look at it). It proves each user story and
the load-bearing **no-data-loss** guarantee. Implementation detail lives in `tasks.md`, not here.

## Prerequisites & run

- .NET 10 SDK; Node (frontend); Docker (local Postgres) — same as 001–004.
- Start the app: **`./start.ps1`** (docker-compose Postgres + `dotnet run`; SPA served on
  `http://localhost:5180`). Migrations + seeding run automatically at startup
  (`DatabaseInitializer.InitializeAsync`).
- Tests: backend `dotnet test` (real Postgres via Testcontainers); frontend `npm test` (Vitest).

## Story validation

### US1 — Track where each application stands (P1)
1. On **Offers**, mark an offer **applied** (existing Apply modal). Open the new **Applications** page.
2. Confirm the offer appears as a card under the first pipeline stage (**Applied**).
3. Move it to **Screening**, then **Interviewing** — the card moves columns; reload → the stage persists.
4. **Close** it with an outcome (e.g. *Rejected*); confirm it leaves the active columns and shows in the
   **closed** section tagged with its outcome. **Reopen** it → it returns to the active pipeline.
5. Attempt an invalid action (close an already-closed application via the API) → rejected (409). ✔ SC-002/SC-004

### US1b — No data lost on upgrade (the explicit requirement) ✔ SC-001
1. Before building this feature, note an existing offer already marked applied (with a date + note).
2. After the migration + startup backfill, open **Applications**: that offer is present at the **Applied**
   stage with its **original applied date**, and its original note appears as the **first journal entry**.
3. Confirm the offer feed, enrichment, fit, tailored CV, export still work unchanged. ✔ SC-009

### US2 — Notes journal + timeline (P1)
1. Open an application; add two notes at different times. Confirm both persist (neither overwrites the other).
2. Open the **Timeline**: stage changes and both notes appear in chronological order with their dates. ✔ SC-003

### US3 — Interview tasks & deadlines (P2)
1. Add a task with a **future** due date and one with a **past** due date (leave it not-done).
2. Confirm the past-due task is flagged **overdue**; mark the future task **done** → it stops counting as outstanding.
3. On **Applications**, confirm the card shows an **outstanding/overdue** indicator without opening it. ✔ SC-005

### US4 — Documents (P2)
1. Attach a file to an application; confirm it lists with its name + added date.
2. **Download** it; confirm the file is byte-for-byte identical to what you attached.
3. Confirm the file lives under `cv-data/` (gitignored) as `appdoc-…` and is **not** committed. ✔ SC-006/SC-008
4. Try a > ~50 MB file → rejected with a clear message.

### US5 — Communications & interviews (P3)
1. Log a recruiter **communication** (date/direction/channel/summary) and record an **interview** with a
   future date; confirm the interview shows as **upcoming**.
2. Later record the interview's **outcome**; confirm it updates and appears in the timeline interleaved
   with notes and stage changes. ✔ SC-003

### Stage configuration (FR-019)
1. In **Settings → Pipeline stages**, rename a stage, add one, reorder, and try to remove a stage that
   holds applications → blocked/asked to reassign; reassign then remove. Applications are never orphaned.

### Backup / restore round-trip ✔ SC-007
1. With applications, notes, tasks, documents, communications, interviews present, create a **backup**
   (Settings → Backup).
2. Change some data, then **restore** the backup. Confirm every application, its stages/notes/tasks/
   documents/communications/interviews, and the pipeline configuration return intact.
3. (Cross-version) Restore a **pre-005** backup into this build → applied offers are reconstructed as
   applications at the Applied stage (older-restore backfill). ✔ SC-001 on the restore path

## Automated coverage (see `tasks.md`)
- **Domain**: `JobApplication` state machine (stage/close/reopen invariants), task overdue, note append-only,
  pipeline ordering.
- **Application**: create-on-apply, clear-applied → steer-to-close guard, permanent-delete confirmation,
  backfill idempotency.
- **Infrastructure (real Postgres)**: 7-table round-trip; timeline read model ordering; backup/restore
  incl. the new tables + document files; **`BackupTablesCompletenessTests`** now covering all 7;
  **no-data-loss backfill** on upgrade **and** on older-restore; stage-delete guard; export includes
  application fields; the untouched 001–004 suites (FR-016 regression).
- **Frontend (Vitest+RTL)**: `frontend/tests/applications/` — board, detail/timeline, tasks/overdue,
  documents, stage-config section, offer-card stage indicator, the applications API client.
