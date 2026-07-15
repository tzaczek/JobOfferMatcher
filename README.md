# Job Offer Matcher

A **local-first, single-user web app** that periodically collects job offers from a configured
search, tracks them with **append-only history**, enriches each offer with an AI summary + key
skills and a 0–100 fit score against your CV, and presents a **new-vs-seen**,
**salary-and-fit-ranked** feed. It runs entirely on your machine: collection hits a job source's
public JSON API, and all offers, salary data, history and CVs are stored in a local PostgreSQL
database. The AI outputs (offer summaries/key-skills, a recruiter-style CV profile, and the fit
score with matched/missing + rationale) are produced **only** by your own **Claude Code session
under your Max plan** acting as a local worker — the backend imports no AI SDK and makes no
external AI call. The only thing that leaves the machine is the non-PII search filter used to
query the source. Un-produced AI outputs show **"pending"**, never a non-AI fallback.

Stack: **React** (Vite, TypeScript) front end + **.NET 10** (ASP.NET Core) back end +
**PostgreSQL** (EF Core, append-only migrations). Layered architecture
`Domain → Application → Infrastructure → Web`; the Domain layer is framework-free.

---

## Prerequisites

- **.NET 10 SDK**
- **Node.js 20+** and **npm** (to build / dev-run the SPA)
- **Docker Desktop** (runs the local PostgreSQL container, and the integration tests via
  Testcontainers). A native PostgreSQL 17 install is the Docker-free fallback.
- One-time, to collect the **LinkedIn** source (feature 008 — the manual-login browser path):
  `playwright install chromium`. A LinkedIn scan opens a **headed** Chromium window; you sign in
  yourself (the password is never stored, transmitted, or logged). The session persists under
  `{LocalApplicationData}/JobOfferMatcher/browser-profiles/linkedin` (gitignored) and is reused across
  scans; it lives **outside** `cv-data/`, so it is deliberately **excluded from backups** — re-log-in
  after a restore. Set `Sources:LinkedIn:UseBrowser=false` to run without it (offline/CI).

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

## AI enrichment — the `/enrich` worker

Offer summaries + key skills, the recruiter-style CV profile, and the 0–100 fit score (with
matched/missing skills + a one-line rationale) are **AI-generated**, but the backend never calls an
AI service. Instead it exposes a small **loopback-only enrichment queue** (`/api/enrichment/*`), and
**your own Claude Code session under your Max plan** drains it as a worker. This keeps everything
local and off the paid Anthropic API (FR-012 / SC-005).

> **Runbook:** step-by-step instructions (including the container-CV gotcha and re-run triggers) live
> in [`docs/enrichment-worker.md`](docs/enrichment-worker.md). The essentials follow.

**Run the worker.** With the app running (`./start.ps1`), open Claude Code in this repo and run the
slash command:

```
/enrich
```

It loops `GET /api/enrichment/pending` → produces each output **in-session** (reading the CV PDF
directly for the profile) → `POST /api/enrichment/results`, until the queue is drained. It is
**stateless and safe to re-run** any time; staleness is keyed by an input hash, so only changed
inputs are reprocessed. The feed shows a **pending / failed indicator** with a manual **re-run**
button, and the CV / offer cards render `pending` / `produced` / `failed` (and `unreadable` for an
image-only CV) — never a fabricated score.

**Tuning.** Settings → *Enrichment limits* configures the soft caps (summary/CV-summary words, max
key skills, fit-rationale words, retry limit). The *Fit weights* are **guidance to Claude**, not a
fixed formula — raise an axis to make it weigh more heavily in the score.

**Run-mode caveat (ADR-4).** The worker and the app must share a host + filesystem. The supported
mode is the default `./start.ps1` (Postgres in Docker, **app via `dotnet run`** on
`http://localhost:5180`), where loopback is genuine and the worker can read the CV by local path.
The full-container `docker compose` packaging is **not** supported for enrichment as-is (a host
worker can't reach the container's loopback or read the container-internal CV path); to use it, bind
the published port to `127.0.0.1` and mount `cv-data` to a host path, or run the app as a host
process for the enrichment session. The `/api/enrichment/*` guard is **fail-closed loopback** (any
non-loopback or unknown remote IP → 403), which is the load-bearing privacy control because the
channel carries CV/offer text.

---

## Tailored CV — the `/tailor-cv` worker

Generate a **CV tailored to one specific offer** — re-emphasising that posting's skills — from a
**transparent, editable prompt**, then download it as a polished **A4 PDF**. Like enrichment, the
backend never calls an AI service: it exposes a loopback-only **tailored-CV queue**
(`/api/tailored-cv/*`) and **your own Claude Code session** drains it as a worker. The one new
mechanism is **HTML→PDF rendering**, done in-process with the **already-present Playwright/Chromium**
(no new dependency). The tailored CV is built **only** from your uploaded CV — it re-emphasises and
reorders real experience and **fabricates nothing** (FR-006).

**Use it.** On an offer card, click **Tailor CV** → a modal opens showing the offer's emphasised-skill
chips (toggleable), your attached source CV, and the **exact, editable prompt**. Toggling a skill
updates the prompt; editing the prompt then takes over. Click **Generate** → the request goes
`pending`.

**Run the worker.** With the app running (`./start.ps1`), open Claude Code in this repo and run:

```
/tailor-cv
```

It loops `GET /api/tailored-cv/pending` → reads your source CV **by local path** + the `cv_versions/`
two-column layout (`v2_two_column.html` + `NOTES.md`) → produces tailored **HTML** → `POST
/api/tailored-cv/results`, until the queue is drained. The **backend renders the HTML to the
downloadable PDF** (Playwright). Re-runnable and idempotent; a regenerate while the worker is mid-pass
is harmlessly **superseded** (a monotonic generation-version guards stale write-backs).

**View & manage.** A produced CV is shown **inline** in the modal and **downloadable** as a PDF.
The **Tailored CVs** page (top nav) lists them all, links back to each offer, and offers
view / regenerate / download / remove. A tailored CV **persists** even after its offer is delisted,
and is **included in backup/restore** (the `tailored_cv` table + its flat `cv-data/tailored-*.html/.pdf`
files). The whole `/api/tailored-cv/*` group is **fail-closed loopback** — the source-CV binary is read
from disk by path and never traverses HTTP, and the PDF is served only to the local user (Principle IV).

**Run-mode caveat (ADR-2/ADR-4).** As with `/enrich`, the worker and the app must share a host +
filesystem — the supported mode is the default `./start.ps1` (app via `dotnet run` on
`http://localhost:5180`), where loopback is genuine and the CV path resolves on the worker's filesystem.

---

## Backup & restore

Your data is recoverable (Principle IX — append-only migrations, deliberate destructive ops). The
primary path is the **one-click backup & restore** in the app; the manual dump and the export are
complementary.

**1. In-app backup & restore (recommended).** Settings → **Backup & Restore**:

- **Backup** downloads a single `jobs-backup-<utc>.zip` capturing **everything** — the whole
  PostgreSQL database *and* your uploaded CV files (`cv-data/`, whose PDF bytes are not in the DB).
  It is built entirely in-process via Npgsql `COPY` (no `pg_dump`/Docker needed; works in host and
  container modes), runs while the app stays usable, and never alters live data.
- **Restore** uploads a backup: the app validates it, shows a summary (when it was taken, per-table
  counts, CV count, and a Same/Older/Newer **compatibility** badge), takes an automatic **safety
  pre-backup** of the current state, then — behind an explicit confirm — replaces both stores
  **all-or-nothing** (one DB transaction + an atomic `cv-data` swap; rollback on any failure). An
  older backup loads into the current schema (missing satellite rows are backfilled); a backup from
  a *newer* app version is refused.

The archive is **unencrypted** and holds personal data — it is **gitignored** (`*backup*.zip`,
`backups/`) and must never be committed (Principle IV). Safety pre-backups are written server-side
under `backups/` (also gitignored). The API is loopback-only:

```
GET  /api/backup            # download a complete backup (DB + CV files)
POST /api/backup/inspect    # validate + summarise an upload without restoring
POST /api/backup/restore    # guarded, all-or-nothing restore from an upload
```

**2. Manual database dump (alternative).** The Postgres container is the `db` service
(`jobs-postgres`), database/user `jobs`:

```powershell
docker compose exec -T db pg_dump -U jobs -d jobs -Fc > jobs-backup.dump
docker compose exec -T db pg_restore -U jobs -d jobs --clean < jobs-backup.dump
```

The data also lives in the named Docker volume `jobs_pgdata`, which survives container restarts.
Note: this dumps only the database — it does **not** include the on-disk CV files that the in-app
backup bundles.

**3. In-app export (offers only).** A portable, human-readable record of offers + statuses +
history (complements, does not replace, the full backup):

```
GET /api/export?format=json
GET /api/export?format=csv
```

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
