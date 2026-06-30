<!-- SPECKIT START -->
**Active feature**: `003-backup-restore` — On-Demand Backup & Restore.
For technologies, project structure, shell commands, and other context, read the current plan:
`specs/003-backup-restore/plan.md` (with `research.md`, `data-model.md`, `contracts/`,
`quickstart.md` alongside it). Prior features: `001-job-offer-matcher` (collection/feed/scheduler,
delivered, preserved unchanged) and `002-llm-enrichment-matching` (LLM enrichment via a local
Claude-Code worker) — plans at `specs/001-job-offer-matcher/plan.md` and
`specs/002-llm-enrichment-matching/plan.md`.

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
<!-- SPECKIT END -->
