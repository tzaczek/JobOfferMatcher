<!-- SPECKIT START -->
**Active feature**: `005-application-tracking` — Application & Interview Process Tracking.
For technologies, project structure, shell commands, and other context, read the current plan:
`specs/005-application-tracking/plan.md` (with `research.md`, `data-model.md`, `contracts/`,
`quickstart.md` alongside it). Prior features: `001-job-offer-matcher` (collection/feed/scheduler),
`002-llm-enrichment-matching` (LLM enrichment via a local Claude-Code worker), `003-backup-restore`
(on-demand backup/restore), and `004-tailored-cv-generation` (per-offer tailored CV) — all delivered and
preserved unchanged; plans at `specs/00{1,2,3,4}-*/plan.md`.

**Locked stack** (constitution v1.1.0): local-first, single-user **web app** — React (Vite,
TypeScript) front end + **.NET 10** (ASP.NET Core) back end + **PostgreSQL** (EF Core,
append-only migrations). Layered: `Domain → Application → Infrastructure → Web`; Domain is
framework-free. Run: `./start.ps1` (docker-compose Postgres + `dotnet run`). Tests: xUnit +
real Postgres (Testcontainers); source adapters tested against recorded JSON fixtures (offline).

**Load-bearing decisions (001, still in force)**: collection via the public `api.justjoin.it` JSON
API behind an `IJobSource` port (Playwright manual-login fallback deferred); scheduler =
`BackgroundService` + Cronos poll-tick with single catch-up; identity vs content are **separate
hashes** (new-vs-seen by identity existence, never source dates); raw salary stored, normalized
salary derived. No PII/secrets/DB/CVs committed (Principle IV). See `specs/001-job-offer-matcher/plan.md`
ADR-2 for the source-access accepted-risk.

**Load-bearing decisions (002)**: summaries/key-skills, CV profile, and 0–100 fit + matched/missing +
rationale are produced **only** by a local **Claude Code worker under the user's Max plan** (the
backend imports no AI SDK and makes no external AI call — FR-012/SC-005); the worker drains a
loopback-only `/api/enrichment` queue and writes results back; outputs are **persisted** and
recomputed by **input-hash** (eager `pending` + recompute-on-write-back guard). The non-AI scorer +
keyword profiler + **FuzzySharp are removed** (FR-005 — no non-AI fallback; un-produced items show
"pending"); **PdfPig** is retained only as a CV readability gauge + text fallback. This supersedes the
former "CV matching fully local (keyword/FuzzySharp)" implementation and "fit derived on read, never
stored" — see `specs/002-llm-enrichment-matching/plan.md` ADR-1..ADR-4 (locality is preserved).

**Load-bearing decisions (003)**: complete on-demand **backup + restore** of BOTH stores — the
PostgreSQL DB and the on-disk CV files (`cv-data/`) — via an **in-process Npgsql `COPY`** logical
snapshot (text) zipped with the CV files (no `pg_dump`/docker dependency; works in host + container
modes; raw column text sidesteps EF converters). Delivered as a **browser download**; restore is
**upload → validate → server-side safety pre-backup → all-or-nothing** (one DB tx `TRUNCATE`+`COPY
FROM` excl. `__EFMigrationsHistory` + atomic `cv-data` swap; rollback+swap-back on failure). **No new
schema/migration, no new dependency.** Cross-version by migration id (load **older** into HEAD +
enrichment backfill; **refuse newer**; never run `Down`). Archives **unencrypted** + gitignored
(FR-019). Restore quiesces via a new **`MaintenanceGate`** singleton (scan scheduler +
`ScanOrchestrator` + `EnrichmentService` write methods); a backup runs concurrently (MVCC). Endpoints
are loopback-only `/api/backup/*`. See `specs/003-backup-restore/plan.md` ADR-1..ADR-4. Directly
fulfils Principle IX (recoverable); Principle IV upheld (fully local).

**Load-bearing decisions (004)**: per-offer **tailored CV** generated **only** by the local Claude-Code
worker (new `/tailor-cv` slash command draining a loopback `/api/tailored-cv` queue — **extends** the
002 Claude-as-worker decision; backend makes no external AI call, FR-005/SC-007). Worker returns
**tailored HTML** (re-emphasising the **uploaded 002 CV** — no fabrication, FR-006) following the
`cv_versions` two-column layout; the **backend renders HTML→PDF in-process via the already-present
Playwright/Chromium** (no new dependency). Opt-in, **latest-only** `tailored_cv` satellite (PK `OfferId`,
FK→`offers` cascade); a **`GenerationVersion`** supersede guard replaces 002's input-hash (generation is
user-driven, not auto-invalidated). Generated files are **flat** `tailored-{OfferId:N}.html/.pdf` in the
**`cv-data` root** so 003 backup covers them unchanged — plus add `"tailored_cv"` to
`BackupTables.InsertOrder` (after `offers`) and a **new completeness guard test** (model tables ==
backup list). The editable prompt holds instructions+skills; the source CV is an **attached, visible,
read-only** input (FR-003). Reuses `LoopbackOnlyFilter`, `MaintenanceGate`, `ApplyModal`,
`EnrichmentSettings.RetryLimit`. **One** new migration. See
`specs/004-tailored-cv-generation/plan.md` ADR-1..ADR-4. Principles III/IV/IX upheld.

**Load-bearing decisions (005)**: elevate the binary per-offer **"applied" flag** into a first-class
**application** — a `JobApplication` **satellite** aggregate (PK `OfferId`, FK→`offers` cascade, like
`OfferFit`/`tailored_cv`) with a **user-configurable** `pipeline_stage` (seeded-if-empty defaults) and a
**separate fixed** active/closed + outcome dimension (Accepted/Rejected/Withdrawn/NoResponse); free stage
movement. Five typed child tables (`application_note` append-only, `application_task`, `application_document`,
`application_communication`, `application_interview`); **stage-change/close/reopen reuse `offer_event`** (+3
enum values, no migration) and the **timeline is derived** (a union — no timeline table). **No data lost**:
**one** append-only migration (7 tables, schema only) + an **idempotent seed + `BackfillApplicationsAsync`**
(an application at the first stage per applied offer; legacy `ApplicationNote` → first journal note) run at
**startup AND on older-restore** (new `IApplicationBackfill` in `RestoreService`, mirroring 003's enrichment
backfill; 003 restore `TRUNCATE`s the full HEAD table list). Documents are **flat** `appdoc-{id:N}` files in
the **`cv-data` root** (003 backs them up unchanged); add the 7 tables to `BackupTables.InsertOrder` (guarded
by the completeness test); reuse `MaintenanceGate`. Clearing "applied" **prefers closing over erasing** (409
steer-to-Withdrawn); permanent delete is explicit + backup-recoverable. `/api/applications/*` is UI-local (no
worker, no external AI call). See `specs/005-application-tracking/plan.md` ADR-1..ADR-5. Principles III/IV/IX
upheld; **no existing data dropped or edited**.
<!-- SPECKIT END -->
