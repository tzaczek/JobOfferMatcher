# Job Offer Matcher

A **local-first, single-user web app** that periodically collects job offers from a configured
search, tracks them with **append-only history**, scores each offer against your CV, and presents
a **new-vs-seen**, **salary-and-fit-ranked** feed. It runs entirely on your machine: collection
hits a job source's public JSON API, all offers, salary data, history and CVs are stored in a
local PostgreSQL database, and CV matching is done locally (no external LLM by default). The only
thing that leaves the machine is the non-PII search filter used to query the source.

Stack: **React** (Vite, TypeScript) front end + **.NET 10** (ASP.NET Core) back end +
**PostgreSQL** (EF Core, append-only migrations). Layered architecture
`Domain → Application → Infrastructure → Web`; the Domain layer is framework-free.

---

## Prerequisites

- **.NET 10 SDK**
- **Node.js 20+** and **npm** (to build / dev-run the SPA)
- **Docker Desktop** (runs the local PostgreSQL container, and the integration tests via
  Testcontainers). A native PostgreSQL 17 install is the Docker-free fallback.
- One-time, only when the deferred manual-login browser path is eventually built:
  `playwright install chromium`.

---

## Run it

### 1. Configuration

Copy the example environment file and set a strong database password:

```powershell
copy .env.example .env
# edit .env and set POSTGRES_PASSWORD
```

`.env` (gitignored) holds the Postgres container credentials:

```
POSTGRES_USER=jobs
POSTGRES_PASSWORD=change-me-to-a-strong-password
POSTGRES_DB=jobs
POSTGRES_PORT=5432
```

The .NET connection string is **not** kept in the repo — it lives in user-secrets, outside the
working tree. The app reads it as `ConnectionStrings:AppDb` (see
`Infrastructure/DependencyInjection.cs`), so set it to match the values in `.env`:

```powershell
dotnet user-secrets --project backend/src/Web init
dotnet user-secrets --project backend/src/Web set "ConnectionStrings:AppDb" `
  "Host=localhost;Port=5432;Database=jobs;Username=jobs;Password=<from .env>"
```

Install the front-end dependencies and restore EF tools once:

```powershell
npm --prefix frontend install
dotnet tool restore
```

### 2. Start the app (dev)

```powershell
./start.ps1
```

`start.ps1` runs `docker compose up -d db` (the pinned `postgres:17-alpine` container) and then
`dotnet run --project backend/src/Web`. The host applies EF Core **append-only migrations at
startup** via `MigrateAsync()` and, in Development, auto-starts the Vite dev server (HMR) through
`Microsoft.AspNetCore.SpaProxy` — one command, one URL.

### 3. Run for real (published SPA, no Node at runtime)

Build the SPA into the host's `wwwroot` and publish the single ASP.NET Core process, which then
serves the static SPA (`UseStaticFiles` + `MapFallbackToFile("index.html")`) and the `/api` on
the same `localhost` port:

```powershell
npm --prefix frontend run build       # emits the SPA into backend/src/Web/wwwroot
dotnet publish backend/src/Web -c Release
# then launch the published host → http://localhost:5000
```

---

## Tests

```powershell
dotnet test backend/JobOfferMatcher.sln   # xUnit
npm --prefix frontend test                # Vitest + React Testing Library
```

Integration tests run against **real PostgreSQL** spun up per run via **Testcontainers**, so
**Docker must be running**. The outbound job-source HTTP boundary is tested against checked-in
**recorded JSON fixtures** — the normal suite is offline and deterministic and never hits the
live source API.

---

## Backup & restore

Your data is recoverable two ways (Principle IX — append-only migrations, deliberate destructive
ops):

**1. Database dump / restore.** The Postgres container is the `db` service (`jobs-postgres`),
database/user `jobs`:

```powershell
# Back up
docker compose exec -T db pg_dump -U jobs -d jobs -Fc > jobs-backup.dump

# Restore (into a running, empty db)
docker compose exec -T db pg_restore -U jobs -d jobs --clean < jobs-backup.dump
```

The data itself lives in the named Docker volume `jobs_pgdata`, which survives container
restarts and can be backed up independently.

**2. In-app export.** A portable, human-readable record of offers + statuses + history:

```
GET /api/export?format=json
GET /api/export?format=csv
```

Open the downloaded file outside the app. CVs are stored locally (under `cv/`) and are
**gitignored** — back them up yourself; they are never committed and never leave the machine.

---

## Unattended scheduled scans (≥3×/day while the app is closed)

Scans are driven by an in-process **Cronos `BackgroundService`** with a poll-tick catch-up. This
worker **only runs while the host process is running** — closing the app stops scheduled scans.
To collect offers on a cadence (e.g. `0 6,13,20 * * *`) while the app is otherwise "closed", you
must have the OS keep the host running. This applies to any in-process scheduler, by design.

**Recommended (Windows): Task Scheduler, at log-on.** Launch the host whenever you log in:

```powershell
$action  = New-ScheduledTaskAction -Execute 'pwsh.exe' `
  -Argument '-NoProfile -File "C:\Users\tomas\Repo\Job\start.ps1"' `
  -WorkingDirectory 'C:\Users\tomas\Repo\Job'
$trigger = New-ScheduledTaskTrigger -AtLogOn
Register-ScheduledTask -TaskName 'JobOfferMatcher' -Action $action -Trigger $trigger
```

(Point `-Execute` at the published host `.exe` instead of `start.ps1` for the run-for-real build.)

**Alternative (Windows): a dedicated Windows Service**, e.g. `New-Service` or
`sc.exe create JobOfferMatcher binPath= "<path-to-published-host.exe>"` (the host can be published
to run as a service). Note: a session-0 Windows Service has no interactive desktop, so it cannot
display the headed manual-login browser window if/when that deferred path is built — the
at-log-on approach keeps the host in your desktop session.

**macOS / Linux:** use a `launchd` LaunchAgent (macOS) or a `systemd` user service (Linux) to
start the host at login.

---

## Project structure

```text
backend/
├── JobOfferMatcher.sln
├── src/
│   ├── Domain/          # framework-free: offers, sources, salary, matching, scans, role groups
│   ├── Application/      # use cases + ports (scanning, CV, scheduling, offers, export, settings)
│   ├── Infrastructure/  # EF Core persistence, source adapters (justjoin.it), CV (PdfPig), scheduling (Cronos)
│   └── Web/              # ASP.NET Core host: Program.cs, /api endpoints, static SPA (wwwroot)
└── tests/
    ├── Domain.Tests/
    ├── Application.Tests/
    └── Infrastructure.Tests/   # real Postgres via Testcontainers; source mapping vs recorded fixtures

frontend/
├── package.json
├── vite.config.ts
└── src/
    ├── api/             # typed client mirroring the REST contracts
    ├── components/      # offer card, filters, status banner
    ├── pages/           # offers feed, scans, sources, CV, settings
    ├── theme/           # central design tokens (one source of design truth)
    └── lib/             # status polling

docker-compose.yml       # postgres:17-alpine, named volume, password from gitignored .env
start.ps1                # docker compose up -d db; dotnet run --project backend/src/Web
docs/adr/                # Architecture Decision Records
```

Dependencies point inward (`Domain → Application → Infrastructure → Web`); EF Core, Npgsql,
PdfPig, Cronos, HttpClient and the deferred Playwright reference live **only** in Infrastructure.

---

## Privacy & data

This is a **local-first** tool. There is **no required external service**: the database, offers,
salary data, scan history and CVs all stay on your machine. During collection, only the **non-PII
search filter** (category, seniority, employment type, etc. — no name or email) leaves the
machine to query the source, sent with a generic User-Agent. **No portal credentials are ever
stored.** The `.gitignore` excludes the database/volume, `.env`, exports, CVs (`cv/`),
user-secrets, and any browser-profile directories.
