# Implementation Plan: Job Offer Aggregation & CV-Based Matching

**Branch**: `001-job-offer-matcher` | **Date**: 2026-06-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-job-offer-matcher/spec.md`

**Constitution**: `.specify/memory/constitution.md` v1.1.0 (Technology Stack locked by this plan)

## Summary

A **local-first, single-user web app** that periodically collects job offers from a
configured search, tracks them with append-only history, scores each offer against the
user's CV, and presents a **new-vs-seen**, **salary-and-fit-ranked** feed.

Technical approach (from Phase 0 research, all live-verified 2026-06-28):

- **Collection** prefers the *lightest reliable method*: justjoin.it exposes a public,
  unauthenticated JSON API (`api.justjoin.it`), so the primary path is plain server-side
  `HttpClient` GETs — no browser, no login. A real-browser **manual-login fallback**
  (Playwright) sits behind the **same `IJobSource` port** but its adapter is **deferred**
  until a source actually blocks direct access (FR-040, Principle X). The escalation
  *trigger* (403/anti-bot → "needs manual login") is built now; the heavy adapter is not.
- **Scheduling**: an in-process `BackgroundService` + **Cronos** runs scans ≥3×/day on a
  configurable cron, on-demand via API, independent of the UI. A **short poll-tick**
  (every 30–60 s) computes the previous cron occurrence and runs **one** catch-up if the
  last run predates it — robust to laptop sleep/resume, not just process restart.
- **Identity / dedup / change detection**: a **two-key model** separates stable *identity*
  (justjoin.it `guid`) from mutable *content* (a `ContentFingerprint` hash). New-vs-seen is
  decided purely by **whether the identity exists in our store** (never by source dates).
  Disappearance is reconciled **only after a `Complete` scan**, with a sanity guard.
- **CV matching**: fully local (no external LLM by default). **PdfPig** extracts CV text;
  a curated skill catalog + **FuzzySharp** derives the profile; a transparent weighted model
  yields a **0–100 fit score** plus an explicit **matched/missing** breakdown (FR-025).
- **Storage**: **PostgreSQL** via EF Core 10, **append-only** migrations; raw salary bands
  stored authoritatively, normalized figures **derived** (never persisted as fact).

The full design is in [research.md](./research.md), [data-model.md](./data-model.md),
[contracts/](./contracts/), and [quickstart.md](./quickstart.md).

## Technical Context

**Language/Version**: C# 14 on **.NET 10** (backend); **TypeScript + React 19** via Vite (frontend).

**Primary Dependencies**:
- Backend: ASP.NET Core 10 (host + minimal API + static SPA hosting), EF Core 10 +
  `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x, **Cronos 0.13.0** (cron parsing, in a
  `BackgroundService`), **UglyToad.PdfPig** (CV text extraction, pin exact alpha),
  **FuzzySharp** (fuzzy skill matching), **Polly** (HTTP retry/backoff for politeness),
  `Microsoft.AspNetCore.SpaProxy` (dev only). **Microsoft.Playwright** — *referenced by the
  Infrastructure project but the manual-login adapter is deferred* (see Summary).
- Frontend: React 19, Vite, TypeScript, a typed API-client layer; central design tokens/theme.

**Storage**: **PostgreSQL** (local instance, `docker compose up -d db` or native install),
EF Core **append-only** migrations applied at host startup via `MigrateAsync()`.

**Testing**: xUnit + an assertion library (Domain/Application unit tests); **real-PostgreSQL**
integration tests via **Testcontainers.PostgreSql** (Principle V); the outbound HTTP boundary
to job sources is tested against **checked-in recorded JSON fixtures** (offline, deterministic),
with at most one opt-in "live smoke" test. Frontend: Vitest + React Testing Library.

**Target Platform**: local-first, single-user. Windows 11 dev. Runs as a **foreground**
ASP.NET Core process on `localhost` (the headed manual-login browser needs an interactive
desktop session). For unattended ≥3×/day while the app is "closed", install the host as a
**Windows Service / Task Scheduler login-item** — required for that goal, noted as ops setup.

**Project Type**: **Web application** (separate `backend/` + `frontend/`).

**Performance Goals**: SC-001 — a scan's new offers visible in the UI within **2 minutes** of
completion. A full justjoin.it scan ≈ **9 list requests** + **1 detail request only per new/
changed offer**, paced ~1 req/s with Polly backoff (FR-007). Not throughput-bound.

**Constraints**: local-first; **no required external service**; **no stored credentials**;
polite, rate-limited collection; **append-only** history; **async all the way** (no
`.Result`/`.Wait()`); nullable reference types on, warnings-as-errors in Domain + Application.

**Scale/Scope**: 1 user; ~180 offers per source per scan; ~3 scans/day; 4 user stories
(P1 collect+display → P2 new-vs-seen+schedule → P3 CV ranking → P4 more sources). The
`offer_observation` table grows at offers × scans (fine at this scale; retention is a future,
non-built option).

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design. All gates PASS.*

| # | Principle | Status | How this plan satisfies it |
|---|-----------|--------|----------------------------|
| I | Layered architecture, deps inward | ✅ PASS | `Domain → Application → Infrastructure → Web`. Domain is framework-free (VOs, fingerprint fn, scoring rules, reconciliation rules). EF Core, Npgsql, PdfPig, Playwright, Cronos, HttpClient live **only** in Infrastructure; the Web host is the only HTTP/scheduling entry point. Commands return `Result<T>`; queries are read-only. No MediatR (YAGNI) until cross-cutting repeats. |
| II | Strongly-typed domain, no primitive obsession | ✅ PASS | Wrapped IDs (`OfferId`, `SourceId`, `ScanRunId`, `OfferVersionId`, `RoleGroupId`, `CvId`). Value objects: `ExternalRef`, `ContentFingerprint`, `Money`, `Currency`, `SalaryBand`, `NormalizedSalary`, `FitScore`, `MatchConfidence`. Source `guid`/`slug` are mapped to wrapped types **at the Infrastructure boundary** — never leak raw. `Result<T>` for expected failures. |
| III | The Tracker Reflects Reality (NON-NEG) | ✅ PASS | Append-only `OfferVersion` / `OfferObservation` / `OfferEvent` / `ScanRun`. **No fabricated/demo offers** (FR-035): only genuinely collected offers persist. Normalized salary is **derived on demand**, never written as a captured fact. Availability lifecycle has explicit states + events. |
| IV | Personal Data Private & Local (NON-NEG) | ✅ PASS | CVs, salary, offers stay on the machine. **No external service required**. Only the **non-PII search filter** leaves the machine during collection (no name/email). No stored portal credentials (FR-005); the Playwright session profile is local + **gitignored**. `.gitignore` excludes DB, exports, CVs, `.env`, browser profiles, user-secrets. **Accepted-risk recorded** (see below): the source's `robots.txt`/ToS. |
| V | Real Database in Tests | ✅ PASS | Integration tests run on **real PostgreSQL** (Testcontainers). The outbound job-source HTTP boundary is mocked with **recorded fixtures** (an allowed external boundary); the DB itself is never mocked. |
| VI | Green Before Done (NON-NEG) | ✅ PASS (process) | Each user story closes only when the full local suite is green. Tests are offline/deterministic (no live API in the normal suite). |
| VII | UI Changes Require Visual Verification | ✅ PASS | React UI: plan → edit → **run the app and look** → confirm. Constitution v1.1.0 maps this principle to the web UI. |
| VIII | One Source of Design Truth | ✅ PASS | Colors/spacing/typography/status colors from **central design tokens / one theme** (the web equivalent of XAML resource dictionaries) — no scattered literals. No heavyweight UI kit unless justified. |
| IX | Your Data Is Recoverable | ✅ PASS | EF migrations **append-only**, applied at startup; **export to JSON/CSV** (FR-037); documented backup path (DB dump + export). Destructive ops deliberate. |
| X | Simple by Default (YAGNI) | ✅ PASS | `BackgroundService` + Cronos over Quartz/Hangfire; **polling** the scan status over SignalR; **deferred** Playwright adapter; deterministic cross-source dedup (no ML); docker-compose Postgres over Aspire/k8s. |
| XI | Documented Decisions, Immutable History | ✅ PASS | ADR-style notes recorded (stack lock = constitution v1.1.0; access-method accepted-risk; scheduler choice). Conventional Commits, one logical change each. |

**Decisions & Accepted Risks (ADR-style, per Principles XI + IV):**

- **ADR-1 — Stack locked**: React + .NET 10 + PostgreSQL web app. Recorded as constitution
  **v1.1.0** (MINOR amendment) per spec clarification Session 2026-06-28.
- **ADR-2 — Source access accepted-risk** *(load-bearing; corrected by adversarial review)*:
  `api.justjoin.it/robots.txt` contains a `User-agent: *` → `Disallow: /` group whose
  allowlist does **not** include the offer endpoints, and the path `/v2/user-panel/...`
  signals an internal endpoint; the Regulamin/ToS may restrict automation. The endpoints are
  technically reachable unauthenticated (HTTP 200) but **"reachable ≠ permitted."** Accepted
  as a **deliberate, mitigated risk** for a single-user, low-volume, **local**, personal tool:
  polite pacing (~1 req/s, Polly backoff, Cloudflare-cache-friendly), **detail fetched only
  for new/changed offers**, a **generic non-PII `User-Agent`** (no name/email — Principle IV),
  **no redistribution** of collected data, and a **built-in escalation switch** to the
  manual-login browser path on 403/challenge (FR-040). **Action for the user**: review the
  justjoin.it Regulamin; the design keeps the source adapter swappable if access terms change.
- **ADR-3 — Scheduler**: `BackgroundService` + Cronos with a **poll-tick** catch-up
  (sleep/resume-robust). Quartz.NET + AdoJobStore (`FireOnceNow`) is the documented fallback
  if hand-rolled correctness becomes a burden.

**Complexity Tracking**: No constitution violations — table omitted (N/A).

## Project Structure

### Documentation (this feature)

```text
specs/001-job-offer-matcher/
├── plan.md              # This file
├── research.md          # Phase 0 output — decisions, rationale, accepted risks
├── data-model.md        # Phase 1 output — entities, VOs, lifecycle, schema
├── quickstart.md        # Phase 1 output — run & validation guide
├── contracts/           # Phase 1 output — REST API + IJobSource port + source-payload contract
│   ├── rest-api.md
│   ├── ijobsource-port.md
│   └── justjoinit-payload.md
├── spec.md              # Feature spec (with clarifications)
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root)

```text
backend/
├── JobOfferMatcher.sln
├── src/
│   ├── Domain/                 # zero framework deps
│   │   ├── Offers/             # Offer aggregate, OfferVersion, OfferObservation, OfferEvent, ExternalRef, ContentFingerprint, AvailabilityStatus
│   │   ├── Sources/            # JobSource, SourceId, IdentityKind
│   │   ├── Salary/             # Money, Currency, SalaryBand, SalaryPeriod, EmploymentBasis, TaxTreatment, NormalizedSalary, SalaryNormalizer, SalaryNormalizationSettings
│   │   ├── Matching/           # CandidateProfile, SkillCatalog, FitScore, FitBreakdown (matched/missing), Scorer
│   │   ├── Scans/              # ScanRun, ScanOutcome, TriggerType, result counts
│   │   ├── RoleGroups/         # RoleGroup (cross-source cluster), MatchConfidence
│   │   └── Common/             # Result<T>, wrapped-ID base, guards
│   ├── Application/            # use cases + ports (interfaces)
│   │   ├── Scanning/           # IScanRunner, ScanOrchestrator, IJobSource port, reconciliation use case
│   │   ├── Cv/                 # ICvTextExtractor, ICvProfileParser ports, profile use cases
│   │   ├── Scheduling/         # schedule config use cases, catch-up policy (uses TimeProvider)
│   │   ├── Offers/             # queries (list/filter/sort), user-status commands
│   │   └── Export/             # export to JSON/CSV use case
│   ├── Infrastructure/
│   │   ├── Persistence/        # EF Core DbContext, configurations, migrations (append-only), repositories
│   │   ├── Sources/JustJoinIt/ # JustJoinItSource (HttpClient + Polly), payload mapping, category/filter config
│   │   ├── Sources/Browser/    # IInteractiveBrowserSession port + DEFERRED Playwright adapter (stub + trigger)
│   │   ├── Cv/                 # PdfPig text extractor
│   │   └── Scheduling/         # Cronos-based tick worker (BackgroundService)
│   └── Web/                    # ASP.NET Core host (Presentation)
│       ├── Program.cs          # API, static SPA (UseStaticFiles + MapFallbackToFile), BackgroundService, MigrateAsync
│       ├── Endpoints/          # /api/scans, /api/offers, /api/sources, /api/cv, /api/schedule, /api/export, /api/scan-session (login handshake)
│       ├── Contracts/          # request/response DTOs
│       └── wwwroot/            # built SPA copied here for "run for real"
└── tests/
    ├── Domain.Tests/           # scoring, fingerprint, salary-normalizer, reconciliation rules
    ├── Application.Tests/      # orchestration, catch-up policy (TimeProvider), with fakes
    └── Infrastructure.Tests/   # real Postgres (Testcontainers); source mapping vs recorded fixtures

frontend/
├── package.json
├── vite.config.ts
├── src/
│   ├── api/                    # typed client (mirrors contracts/rest-api.md)
│   ├── components/             # OfferCard (raw+≈normalized salary, fit chip, matched/missing), filters, status banner
│   ├── pages/                  # Offers feed, Scan/Session, Sources, CV, Settings (schedule, FX/normalization)
│   ├── theme/                  # central design tokens (colors, spacing, typography, status colors)
│   └── lib/                    # status polling (default) / optional SignalR
└── tests/                      # Vitest + RTL

docker-compose.yml              # postgres:17-alpine, named volume, password from gitignored .env
start.ps1                       # docker compose up -d db; dotnet run --project backend/src/Web
.gitignore                      # DB, exports, CVs, .env, browser profiles, user-secrets
```

**Structure Decision**: **Web application** layout. The backend is a single ASP.NET Core
host that *is* the app for "run it for real" — it serves the Vite-built SPA from `wwwroot`,
exposes `/api`, runs the scheduler as a `BackgroundService`, and (when needed) launches the
headed manual-login browser. The four backend projects enforce the constitution's inward
dependency rule (Domain has no framework references). `frontend/` is the React SPA, proxied
in dev via `SpaProxy` and copied into `wwwroot` at publish — no Node at runtime.

## Phase Status

- [x] Phase 0 — Research (`research.md`): 7 topics resolved + adversarial verification; corrections folded in.
- [x] Phase 1 — Design & Contracts (`data-model.md`, `contracts/`, `quickstart.md`); agent context updated.
- [ ] Phase 2 — Tasks (`/speckit-tasks`) — **not** produced by this command.

## Complexity Tracking

> No Constitution Check violations — this section is intentionally empty (N/A).
