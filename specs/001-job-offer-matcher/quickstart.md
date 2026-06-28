# Quickstart: Job Offer Aggregation & CV-Based Matching

A validation/run guide for the local-first web app (React + .NET 10 + PostgreSQL). For design
detail see [plan.md](./plan.md), [data-model.md](./data-model.md), and
[contracts/](./contracts/). This is **not** implementation code — bodies/migrations/tests live
in the implementation phase.

## Prerequisites

- **.NET 10 SDK**, **Node 20+** (build the SPA), **Docker Desktop** (Postgres + Testcontainers)
  — or a native PostgreSQL 17 install as the Docker-free fallback.
- One-time, when the deferred manual-login browser path is built: `playwright install chromium`.
- The user's CV PDFs already live in `cv/` (gitignored). Nothing else leaves the machine.

## First-time setup

```powershell
# 1. Local Postgres (pinned, resettable)
docker compose up -d db                      # postgres:17-alpine, named volume

# 2. Connection string + secrets stay OUTSIDE the repo
dotnet user-secrets --project backend/src/Web init
dotnet user-secrets --project backend/src/Web set "ConnectionStrings:AppDb" `
  "Host=localhost;Port=5432;Database=jobs;Username=jobs;Password=<from .env>"

# 3. Front end deps
npm --prefix frontend install

# 4. EF tools (append-only migrations applied at startup via MigrateAsync)
dotnet tool restore
```

`.gitignore` must exclude: the live DB/volume, `.env`, `appsettings.*.local.json`,
user-secrets, `cv/`, exports, and Playwright browser-profile dirs (Principle IV).

## Run

```powershell
# Dev (single command): SpaProxy auto-starts Vite (HMR); migrations auto-apply.
./start.ps1                                  # = docker compose up -d db; dotnet run --project backend/src/Web

# Run-for-real (one process, no Node at runtime):
npm --prefix frontend run build              # emits SPA into backend/src/Web/wwwroot
dotnet publish backend/src/Web -c Release
# then launch the published host → http://localhost:5000
```

For unattended **≥3×/day while the app is "closed"**, install the host as a Windows Service /
Task Scheduler login-item (the `BackgroundService` only runs while the host runs — research §3).

## Validation scenarios (prove each user story end-to-end)

Each maps to spec acceptance scenarios / success criteria. Run against the **real app**
(Principle VII) unless noted.

### US1 — Collect & display (P1)
1. `POST /api/sources` with the justjoin.it `.NET` search (or use the seeded default).
2. `POST /api/scans/run`; poll `GET /api/scans/{id}/status` until `completed`.
3. `GET /api/offers` → the UI lists offers with title, company, **raw salary band(s)**,
   location, work mode, seniority, skills, and a working **canonical link** (open one → FR-029).
   ✅ AC US1-1. An offer with no published salary shows "Salary not disclosed", not dropped
   (FR-010) ✅ AC US1-3. Expect ~177 remote/hybrid `.NET` offers (research §1).

### US2 — New vs seen + schedule (P2)
4. Note the offers. `POST /api/scans/run` again → previously-seen offers are **not** re-flagged
   new; only genuinely new `guid`s show as new ✅ AC US2-1/2, SC-002.
5. Change an offer's salary in a fixture and re-run a fixture-backed scan → it is flagged
   **updated**, not new ✅ AC US2-4.
6. A scan finding nothing new → UI states "no new offers" (no error/empty break) ✅ AC US2-5.
7. `GET /api/schedule` shows `0 6,13,20 …`; confirm three runs occur across a day (or simulate
   via `TimeProvider` in tests). Missed window (machine asleep) → **one** catch-up on resume,
   not a replay ✅ AC US2-3, FR-039 (verified deterministically in Application.Tests).

### US3 — CV ranking (P3)
8. `POST /api/cv` (upload `cv/Tomasz_Zaczek_CV.pdf`) → `isReadable: true`; `GET /api/profile`
   shows derived skills/seniority. Set salary `floor`/`target` + prefs via `PUT /api/profile`.
9. `GET /api/offers?sort=rank` → each offer shows a **0–100 fit** with explicit **matched** and
   **missing** lists (FR-025); list ordered best-fit-and-paid first ✅ AC US3-1/2.
10. Re-sort by salary / fit / recency ✅ AC US3-3.
11. Delete the CV (or use an unreadable PDF) → list still ranks by salary + recency; UI says
    "No readable CV…" ✅ AC US3-4, FR-026.

### US4 — More sources (P4, additive)
12. Configure a second source behind the same `IJobSource` port → its offers appear in the same
    unified, ranked feed; the same role across sources collapses into one `RoleGroup` entry
    (with a user "same/not same" override) ✅ AC US4-1/2, FR-016.

### Cross-cutting
- **Export** (FR-037): `GET /api/export?format=json` (and `csv`) downloads a readable file with
  offers + statuses + history; open it outside the app ✅ SC-007.
- **Incomplete run** (FR-036): simulate a `403` fixture → run recorded `Partial/ChallengeDetected`,
  partial results persisted, **no disappearance reconciliation**, surfaced to the user.
- **Status persists** (FR-031): mark an offer `dismissed` → it never re-appears as new across
  later scans ✅ SC-002.

## Test suite (Principles V/VI)

```powershell
dotnet test backend/JobOfferMatcher.sln     # xUnit; integration tests spin real Postgres (Testcontainers)
npm --prefix frontend test                  # Vitest + RTL
```

Source mapping/pagination/escalation are tested against **checked-in recorded JSON fixtures**
(offline, deterministic — see [contracts/ijobsource-port.md](./contracts/ijobsource-port.md));
the normal suite never hits the live justjoin.it API.

## Backup / recover (Principle IX)

- DB: `pg_dump` the `jobs` database (or copy the Docker volume).
- App-level: `GET /api/export` to JSON/CSV — the portable, human-readable record.
- Migrations are **append-only**: never edit an applied migration; correct forward with a new one.
