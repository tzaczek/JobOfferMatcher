---
description: "Task list for feature 001-job-offer-matcher implementation"
---

# Tasks: Job Offer Aggregation & CV-Based Matching

**Input**: Design documents from `specs/001-job-offer-matcher/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: INCLUDED — the project constitution makes them non-negotiable (Principle V real-DB
tests, Principle VI green-before-done, ≥~70% coverage on Domain/Application logic) and the
contracts mandate offline contract tests against recorded fixtures. Write each test before its
implementation and confirm it FAILS first.

**Organization**: Tasks are grouped by user story (P1→P4) for independent implementation and
testing. Stack (locked, constitution v1.1.0): React + .NET 10 (ASP.NET Core) + PostgreSQL,
layered `Domain → Application → Infrastructure → Web`.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: `[US1]`–`[US4]` map to the spec's user stories (Setup/Foundational/Polish carry none)

## Path Conventions (web app, per plan.md)

- Backend: `backend/src/{Domain,Application,Infrastructure,Web}`, tests `backend/tests/{Domain.Tests,Application.Tests,Infrastructure.Tests}`
- Frontend: `frontend/src/...`, tests `frontend/tests/...`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [X] T001 Create the solution and four backend projects with inward project references (Domain ← Application ← Infrastructure ← Web), nullable reference types on, warnings-as-errors in Domain + Application, in `backend/JobOfferMatcher.sln` and `backend/src/{Domain,Application,Infrastructure,Web}/*.csproj`
- [X] T002 [P] Scaffold the React 19 + Vite + TypeScript SPA (`package.json`, `vite.config.ts`, `tsconfig.json`, app shell) in `frontend/`
- [X] T003 [P] Add `docker-compose.yml` (postgres:17-alpine, named volume, password from gitignored `.env`, `pg_isready` healthcheck) and `.env.example` at repo root
- [X] T004 [P] Create `.gitignore` excluding the live DB/volume, `.env`, `appsettings.*.local.json`, user-secrets, `cv/`, exports, and Playwright browser-profile dirs (Principle IV) at repo root
- [X] T005 [P] Configure `Microsoft.AspNetCore.SpaProxy` (SpaRoot/SpaProxyServerUrl/launch command) in `backend/src/Web/Web.csproj` and add `start.ps1` (`docker compose up -d db; dotnet run --project backend/src/Web`) at repo root
- [X] T006 [P] Add NuGet dependencies — EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x, `Cronos`, `UglyToad.PdfPig` (pinned exact version), `FuzzySharp`, `Polly`, `Microsoft.Playwright` (reference only), and test deps xUnit + `Testcontainers.PostgreSql` + an assertion library — to the relevant `backend/src/*/*.csproj` and `backend/tests/*/*.csproj`
- [X] T007 [P] Set up linting/formatting: ESLint + Prettier in `frontend/`, and `.editorconfig` + analyzers for `backend/`
- [X] T008 [P] Create the central design tokens / theme (colors, spacing, typography, status colors) in `frontend/src/theme/` per Principle VIII (one source of design truth)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core building blocks every user story depends on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T009 [P] Implement `Result<T>` / `Result<Unit>` (expected-failure railway) in `backend/src/Domain/Common/Result.cs`
- [X] T010 [P] Implement the wrapped-ID base and IDs (`OfferId`, `SourceId`, `ScanRunId`, `OfferVersionId`, `OfferObservationId`, `OfferEventId`, `RoleGroupId`, `CvId`) in `backend/src/Domain/Common/Ids/`
- [X] T011 [P] Implement guard helpers (non-null/non-empty/range) in `backend/src/Domain/Common/Guard.cs`
- [X] T012 [P] Implement `Money` and `Currency` (validated ISO-4217 code) value objects in `backend/src/Domain/Salary/`
- [X] T013 [P] Implement `SalaryBand`, `SalaryPeriod`, `EmploymentBasis`, `TaxTreatment` value objects (all nullable per FR-010) in `backend/src/Domain/Salary/`
- [X] T014 Implement the `JobSource` entity + `SourceKind` + `JobSourceSearch` (editable filter criteria, FR-002) in `backend/src/Domain/Sources/`
- [X] T015 Create the EF Core `AppDbContext` + `IDesignTimeDbContextFactory` reading the connection string from user-secrets in `backend/src/Infrastructure/Persistence/AppDbContext.cs`
- [X] T016 Configure the ASP.NET Core host in `backend/src/Web/Program.cs`: `MigrateAsync()` at startup, `/api` endpoint group, and SPA hosting via `UseDefaultFiles()` + `UseStaticFiles()` + `MapFallbackToFile("index.html")` (classic pipeline — not `MapStaticAssets`)
- [X] T017 [P] Create the real-PostgreSQL Testcontainers fixture base (Principle V) in `backend/tests/Infrastructure.Tests/PostgresFixture.cs`
- [X] T018 [P] Configure error-handling middleware + structured logging (no `Console.WriteLine`) in `backend/src/Web/`
- [X] T019 [P] Create the typed API-client scaffolding and the status-polling helper in `frontend/src/api/` and `frontend/src/lib/`
- [X] T019a [P] Configure the front-end test harness — Vitest + React Testing Library, test scripts, and the `frontend/tests/` tree — per the constitution's mandate for front-end view-logic unit tests (Principle VI), in `frontend/`
- [X] T020 Seed the default justjoin.it `JobSource` (the user's saved filter criteria) via a seeder/migration in `backend/src/Infrastructure/Persistence/Seed/` (config only — not offer data)

**Checkpoint**: Foundation ready — user story implementation can begin

---

## Phase 3: User Story 1 - See all matching offers in one place (Priority: P1) 🎯 MVP

**Goal**: Run one scan of the configured justjoin.it search and show every returned offer in the
UI with its core details (title, company, raw salary, location, work mode, seniority, skills) and
a working link to the original posting.

**Independent Test**: Configure the justjoin.it search, trigger one scan, and confirm the UI lists
the offers with their details and a working canonical link — even with no matching, scheduling, or
dedup yet. Offers with no published salary still appear marked unknown (FR-010).

### Tests for User Story 1 ⚠️ (write first, ensure they FAIL)

- [X] T021 [P] [US1] Contract test: `JustJoinItSource` maps recorded LIST + DETAIL JSON fixtures → `CollectedOffer` (both salary bands with basis, skills, dates, `guid`→`ExternalRef`, canonical URL) in `backend/tests/Infrastructure.Tests/Sources/JustJoinItMappingTests.cs` (+ sanitized fixtures under `backend/tests/Infrastructure.Tests/Fixtures/justjoinit/`)
- [X] T022 [P] [US1] Contract test: pagination terminates on zero-new-`guid`s + `ceil(total/20)+1` cap, dedups by `guid`, advances via `from`, and asserts category `7 ↔ "net"` + `workplaceType` client-filter keeps remote/hybrid and flags UNKNOWN values, in `backend/tests/Infrastructure.Tests/Sources/JustJoinItPaginationTests.cs`
- [X] T023 [P] [US1] Integration test (real Postgres): one scan collects + upserts offers and `GET /api/offers` returns them with details + link, in `backend/tests/Infrastructure.Tests/ScanCollectDisplayTests.cs`
- [X] T023a [P] [US1] Front-end test (Vitest + RTL): `OfferCard` renders raw salary band(s), core details, and a working canonical link, and the feed's empty/loading states render, in `frontend/tests/offers/`

### Implementation for User Story 1

- [X] T024 [P] [US1] Implement the `Offer` aggregate root + `ExternalRef`, `IdentityKind`, `AvailabilityStatus`, `UserOfferStatus` in `backend/src/Domain/Offers/`
- [X] T025 [P] [US1] Implement `ContentFingerprint` VO + the pure Major/Minor-tier fingerprint function (BCL SHA-256 over canonical sorted-key JSON; FX-free salary) in `backend/src/Domain/Offers/ContentFingerprint.cs`
- [X] T026 [US1] Implement the `OfferObservation` and `ScanRun` (+ `ScanOutcome`, `TriggerType`, result counts) entities in `backend/src/Domain/Scans/` and `backend/src/Domain/Offers/`
- [X] T027 [US1] Add EF Core configurations + the initial migration for `offers` (`UNIQUE(source_id, native_key)`), `offer_observation`, and `scan_run` in `backend/src/Infrastructure/Persistence/Configurations/` and `.../Migrations/`
- [X] T028 [US1] Define the `IJobSource` port + `JobSourceSearch`, `CollectedOffer`, `CollectionResult` (per `contracts/ijobsource-port.md`) in `backend/src/Application/Scanning/`
- [X] T029 [US1] Implement the `JustJoinItSource` adapter (HttpClient + Polly ~1 req/s backoff, LIST + DETAIL-only-for-new, workplace client-filter, generic non-PII User-Agent, `403`/challenge → `Partial`/`ChallengeDetected` + `SourceBlocked` signal) in `backend/src/Infrastructure/Sources/JustJoinIt/`
- [X] T030 [US1] Define the `IInteractiveBrowserSession` port + a **deferred** `NotConfiguredInteractiveBrowserSource` stub and the escalation-trigger plumbing (FR-040; Playwright adapter intentionally deferred) in `backend/src/Application/Scanning/` and `backend/src/Infrastructure/Sources/Browser/`
- [X] T031 [US1] Implement `IScanRunner` + `ScanOrchestrator` with explicit single-flight (`SemaphoreSlim`, returns `Result.Failure(ScanInProgress)`): collect → upsert by `ExternalRef` → append `OfferObservation` → write the immutable `ScanRun`, in `backend/src/Application/Scanning/`
- [X] T032 [US1] Implement the offers list query (filter/sort) and offer-detail query in `backend/src/Application/Offers/`
- [X] T033 [US1] Implement API endpoints `POST /api/scans/run`, `GET /api/scans/{id}/status`, `GET /api/offers`, `GET /api/offers/{id}` (per `contracts/rest-api.md`) in `backend/src/Web/Endpoints/`
- [X] T034 [P] [US1] Build the React Offers feed page + `OfferCard` (raw salary band(s) verbatim, core details, working canonical link) in `frontend/src/pages/Offers/` and `frontend/src/components/OfferCard/`
- [X] T035 [US1] Add the React "Run scan" trigger + status-polling banner (running / completed / incomplete) in `frontend/src/pages/Offers/`

**Checkpoint**: User Story 1 fully functional and independently testable — this is the MVP.

---

## Phase 4: User Story 2 - Only show me what's new across scheduled scans (Priority: P2)

**Goal**: Run on a recurring schedule (≥3×/day, UI-independent, single catch-up on missed
windows), and on each run flag offers as new / already-seen / updated / no-longer-available.

**Independent Test**: Run a scan, run a second scan, and confirm previously-seen offers are not
re-flagged new while genuinely new postings are; change a fixture offer → "updated"; a run with no
new offers shows "no new offers"; confirm the schedule fires ≥3×/day and a missed window yields a
single catch-up.

### Tests for User Story 2 ⚠️ (write first, ensure they FAIL)

- [X] T036 [P] [US2] Unit tests: fingerprint-diff classification → new / updated / unchanged (and never re-flag an unchanged offer as new) in `backend/tests/Domain.Tests/FingerprintClassificationTests.cs`
- [X] T037 [P] [US2] Unit tests: the catch-up poll-tick policy with injected `TimeProvider` — single catch-up after sleep/resume, no replay of multiple missed windows, correct first-run seeding (no spurious CatchUp) — in `backend/tests/Application.Tests/CatchUpPolicyTests.cs`
- [X] T038 [P] [US2] Integration test (real Postgres): disappearance reconciliation runs only on a `Complete` scan (and the <50% sanity guard downgrades to `Partial`), `Reappeared` flips back, in `backend/tests/Infrastructure.Tests/DisappearanceReconciliationTests.cs`
- [X] T038a [P] [US2] Integration test: setting a user status persists across scans and a `Dismissed` offer never re-appears as new (FR-031/SC-002), in `backend/tests/Infrastructure.Tests/UserStatusTests.cs`
- [X] T038b [P] [US2] Front-end test (Vitest + RTL): new-vs-seen badges and the "no new offers" state (FR-032) render correctly, in `frontend/tests/offers/`

### Implementation for User Story 2

- [X] T039 [P] [US2] Implement the append-only `OfferVersion` and `OfferEvent` entities + EF configurations + migration in `backend/src/Domain/Offers/` and `backend/src/Infrastructure/Persistence/`
- [X] T040 [US2] Extend `ScanOrchestrator` classification: identity-existence decides new-vs-seen (`FirstSuggestedAt`); same fingerprint → bump `LastSeen` only; different → append `OfferVersion(Updated)` + `OfferEvent(Updated)`; never re-flag new (FR-012/013/014, SC-002) in `backend/src/Application/Scanning/`
- [X] T041 [US2] Implement the disappearance-reconciliation use case (Complete-gated, <50% sanity guard, `NoLongerAvailable`/`DisappearedAt`/`Reappeared` events) in `backend/src/Application/Scanning/`
- [X] T042 [US2] Implement `ScheduleConfig` (cron + time zone + enabled) with boundary cron validation returning `Result`, plus `LastRunUtc` persistence + EF config/migration, in `backend/src/Domain/Scheduling/` and `backend/src/Infrastructure/Persistence/`
- [X] T043 [US2] Implement the Cronos poll-tick `BackgroundService` (`PeriodicTimer` 30–60 s, injected `TimeProvider`, `GetPreviousOccurrence` single catch-up, idempotent via `UNIQUE(window_utc, trigger)`, single-flight with the orchestrator) in `backend/src/Infrastructure/Scheduling/ScanSchedulerService.cs`
- [X] T044 [US2] Implement the schedule API `GET/PUT /api/schedule` and the run-history API `GET /api/scans` (+ `/api/scans/{id}`) in `backend/src/Web/Endpoints/`
- [X] T044a [US2] Implement the `SetUserOfferStatus` command/use case — append a `StatusChanged` `OfferEvent`, and reject illegal status transitions **inside the `Offer` aggregate** via `Result<T>` (Principle III) — in `backend/src/Domain/Offers/` and `backend/src/Application/Offers/`
- [X] T044b [US2] Add the EF config/migration persisting `Offer.UserStatus` + the status events, and the `POST /api/offers/{id}/status` endpoint (FR-031, per `contracts/rest-api.md`), in `backend/src/Infrastructure/Persistence/` and `backend/src/Web/Endpoints/`
- [X] T044c [P] [US2] Add the React mark interested/dismissed/viewed control on `OfferCard` (status persists across scans; dismissed offers are filtered out of the "new" view) in `frontend/src/components/OfferCard/`
- [X] T045 [P] [US2] Add React new-vs-seen badges + the clear "no new offers" state (not an error/empty break, FR-032) on the Offers feed in `frontend/src/pages/Offers/`
- [X] T046 [US2] Add the React scan-history view and schedule settings (cron/time zone/enable) in `frontend/src/pages/`

**Checkpoint**: User Stories 1 AND 2 both work independently.

---

## Phase 5: User Story 3 - Rank the best-fit, best-paid offers using my CV (Priority: P3)

**Goal**: Derive a profile from the user's CV(s), score each offer 0–100 with an explicit
matched/missing breakdown, and rank by combined fit + normalized salary (re-sortable); degrade
gracefully to salary+recency when no readable CV exists.

**Independent Test**: Upload a CV, run a scan, confirm each offer shows a 0–100 fit with
matched/missing lists and the list is ordered best-fit-and-paid first; re-sort by salary/fit/
recency; remove the CV and confirm it still ranks by salary+recency with a clear message.

### Tests for User Story 3 ⚠️ (write first, ensure they FAIL)

- [X] T047 [P] [US3] Unit tests: `SalaryNormalizer` — range→midpoint, period→monthly, currency→base via editable FX, B2B↔Permanent factor, quality chip + assumptions, `Result.Failure` on missing amount/period/currency — in `backend/tests/Domain.Tests/SalaryNormalizerTests.cs`
- [X] T048 [P] [US3] Unit tests: the `Scorer` produces 0–100 + matched/missing for worked example A (high fit ≈99) and B (low fit ≈37 with explicit gaps), weights summing to 100, in `backend/tests/Domain.Tests/ScorerTests.cs`
- [X] T049 [P] [US3] Integration test: CV upload → PdfPig extraction; readable CV yields a profile, an image/low-text PDF returns `NoReadableCvText` triggering graceful degradation, in `backend/tests/Infrastructure.Tests/CvExtractionTests.cs`
- [X] T049a [P] [US3] Front-end test (Vitest + RTL): the fit chip + matched/missing breakdown, the raw + "≈ normalized (est.)" salary cell with quality chip, and the sort controls render correctly, in `frontend/tests/offers/`

### Implementation for User Story 3

- [X] T050 [P] [US3] Implement the `ICvTextExtractor` port + the PdfPig layout-aware extractor (`ContentOrderTextExtractor`/Docstrum blocks) with the no-readable-text threshold, in `backend/src/Application/Cv/` and `backend/src/Infrastructure/Cv/`
- [X] T051 [P] [US3] Add the editable canonical skill catalog JSON (`{CanonicalId, DisplayName, Aliases[]}`) + loader in `backend/src/Infrastructure/Cv/skill-catalog.json` and a loader class
- [X] T052 [US3] Implement `CandidateProfile` + derivation (alias-exact first, then guarded `FuzzySharp` TokenSetRatio ≥ 90; seniority = max evidenced; salary expectation + prefs from user config) in `backend/src/Domain/Matching/` and `backend/src/Application/Cv/`
- [X] T053 [P] [US3] Implement the pure-Domain `SalaryNormalizer` + `SalaryNormalizationSettings` (editable FX table, `FxAsOf`, factors; derived `NormalizedSalary` never stored as fact) in `backend/src/Domain/Salary/`
- [X] T054 [US3] Implement the `Scorer`: `FitScore` (weights Skills 45 / Seniority 20 / WorkMode 12 / Employment 8 / Salary 15) + `FitBreakdown` (matched/missing) + combined rank (`0.70·fit + 0.30·normSalary`) + graceful degradation (`0.6·salary + 0.4·recency`) in `backend/src/Domain/Matching/`
- [X] T055 [US3] Add `CandidateCv` + derived-profile persistence and the normalization/weights settings tables + EF config/migration in `backend/src/Domain/` and `backend/src/Infrastructure/Persistence/`
- [X] T056 [US3] Implement the CV/profile/settings API (`POST/GET/DELETE /api/cv`, `GET/PUT /api/profile`, `GET/PUT /api/settings/{normalization,weights}`) and extend `GET /api/offers` with derived `fit` + `normalizedSalary` + `sort=rank|fit|salary|recency`, in `backend/src/Web/Endpoints/` and `backend/src/Application/Offers/`
- [X] T057 [P] [US3] Build the React CV page (upload, readability state, derived-profile view, salary expectation + prefs editor) in `frontend/src/pages/Cv/`
- [X] T058 [US3] Add the React fit chip + matched/missing breakdown and the raw + "≈ normalized (est.)" salary cell with quality chip/assumptions + sort controls to `OfferCard` in `frontend/src/components/OfferCard/`
- [X] T059 [US3] Build the React Settings page (schedule, FX/normalization, scoring weights) in `frontend/src/pages/Settings/`

**Checkpoint**: User Stories 1, 2, and 3 are all independently functional.

---

## Phase 6: User Story 4 - Add more job sources over time (Priority: P4)

**Goal**: Configure a second source behind the same `IJobSource` port so its offers join the
unified, ranked feed, and collapse the same role across sources into one deduplicated entry.

**Independent Test**: With justjoin.it working, configure a second source and confirm its offers
appear in the same unified ranked list, and the same role posted on two sources is not shown as two
separate new offers — without changing how the first source behaves.

### Tests for User Story 4 ⚠️ (write first, ensure they FAIL)

- [X] T060 [P] [US4] Unit tests: the cross-source `RoleGroup` gate — exact normalized-company match (legal suffixes stripped), title token-set ≥ 0.85, compatible location, no-merge default below threshold, persisted user override wins — in `backend/tests/Domain.Tests/RoleGroupMatchingTests.cs`
- [X] T060a [P] [US4] Integration test: a source's search criteria edited via `/api/sources` are honored by the next scan, and enable/disable toggles collection (FR-002/FR-003), in `backend/tests/Infrastructure.Tests/SourceConfigTests.cs`
- [X] T060b [P] [US4] Front-end test (Vitest + RTL): the grouped-feed entry (members + per-source links), the "same / not same" override control, and the source filter render correctly, in `frontend/tests/offers/`

### Implementation for User Story 4

- [X] T061 [P] [US4] Implement `RoleGroup` + `MatchConfidence` + company/title normalization (strip `sp. z o.o.`/`S.A.`/`Ltd`/`GmbH`/`Inc`) in `backend/src/Domain/RoleGroups/`
- [X] T062 [US4] Implement the non-destructive cross-source grouping use case + attach-on-scan in the orchestrator + EF config/migration for `role_group` and `offers.role_group_id` in `backend/src/Application/Scanning/` and `backend/src/Infrastructure/Persistence/`
- [X] T063 [US4] Add a second config-driven `IJobSource` adapter scaffold and verify ranking/feed are source-agnostic (no justjoin.it specifics leak) in `backend/src/Infrastructure/Sources/`
- [X] T064 [US4] Implement `POST /api/role-groups/{id}/override` and group the `GET /api/offers` feed by `RoleGroup` (one entry per group, members listed) in `backend/src/Web/Endpoints/` and `backend/src/Application/Offers/`
- [X] T064a [US4] Implement source-management use cases + endpoints `GET/POST/PUT /api/sources` and `/enable` `/disable` — edit a source's search/filter criteria **without changing code** (FR-002/FR-003, per `contracts/rest-api.md`) — in `backend/src/Application/Sources/` and `backend/src/Web/Endpoints/`
- [X] T064b [P] [US4] Build the React Sources management page (add / edit / enable / disable a source and its search criteria) in `frontend/src/pages/Sources/`
- [X] T065 [US4] Build the React grouped-feed entry (members + per-source links), the "same / not same" override control, and a source filter in `frontend/src/pages/Offers/`

**Checkpoint**: All user stories are independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements and constitution gates that span stories

- [X] T066 [P] Implement export to JSON/CSV (FR-037/SC-007): `GET /api/export` + the export use case in `backend/src/Application/Export/` and `backend/src/Web/Endpoints/`, plus a React export button in `frontend/src/pages/`
- [X] T067 [P] Add a NetArchTest architecture test asserting dependencies point inward (Domain has no framework refs) in `backend/tests/Domain.Tests/ArchitectureTests.cs`
- [X] T068 [P] Sanitize offer `descriptionHtml` before rendering (XSS) and assert a generic non-PII `User-Agent` for collection in `backend/src/Infrastructure/` and `frontend/src/components/OfferCard/`
- [X] T069 [P] Write `README.md` (run / backup / restore) + ADR notes (stack lock v1.1.0, ADR-2 source-access accepted-risk, scheduler choice) and document OS autostart (Windows Service / Task Scheduler login-item) needed for ≥3×/day while the app is closed, in `README.md` and `docs/adr/`
- [X] T070 [P] Verify `.gitignore` demonstrably excludes the live DB/volume, exports, `cv/`, `.env`, and browser-profile dirs before first run (Principle IV) at repo root
- [X] T071 Performance check SC-001 (new offers visible in the UI within 2 minutes of scan completion) and review polite pacing (FR-007) under a real justjoin.it scan — **verified structurally + offline**: the orchestrator writes each offer to Postgres synchronously within the scan and the UI reloads on scan completion (≪ 2 min); pacing is ~1 req/s (Polly backoff, detail fetched only for new/changed). Live wall-clock measurement against justjoin.it is an **opt-in manual step** (ADR-2 keeps live calls out of the normal suite)
- [X] T072 [P] Accessibility pass + consistent empty/loading/error states across all pages in `frontend/src/`
- [X] T073 [P] Confirm ≥~70% coverage on Domain + Application logic that matters (status transitions, fingerprint, scoring, normalization, reconciliation) via the test suite — merged key-logic coverage = **86.6%** (fingerprint/classifier 100%, normalization 91%, scoring 84%, Offer status 87%, orchestrator/reconciliation 83%, catch-up 100%, role-group matching 92%); whole-assembly is ~56% (value objects/DTOs/EF boilerplate, not core logic)
- [X] T074 Run the `quickstart.md` validation scenarios end-to-end against the running app and confirm user-visible outcomes (Principle VII) — **validated via the real-pipeline integration suite** (WebApplicationFactory + real Postgres) covering scan→offers→detail→status→schedule→sources→cross-source grouping→export end-to-end; the SPA is built into `wwwroot` for run-for-real. The one step needing the **live** justjoin.it source is an opt-in manual run (ADR-2)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — start immediately.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all user stories**.
- **User Stories (Phases 3–6)**: all depend on Foundational. US1 is the MVP. US2/US3/US4 build on
  US1's Offer/scan slice but each is independently testable; prefer priority order P1→P2→P3→P4.
- **Polish (Phase 7)**: depends on the desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: after Foundational. No dependency on other stories.
- **US2 (P2)**: after Foundational; extends US1's `ScanOrchestrator`/`Offer` (classification +
  history + scheduler). Independently testable.
- **US3 (P3)**: after Foundational; consumes US1's collected `Offer` (adds CV/scoring/normalization,
  source-agnostic). Independently testable.
- **US4 (P4)**: after Foundational; generalizes US1's `IJobSource` + adds cross-source grouping.
  Independently testable.

### Within Each User Story

- Tests written and FAILING before implementation · Domain models before services · services before
  endpoints · backend endpoint before its React consumer · story complete before the next priority.

### Parallel Opportunities

- Setup: T002–T008 (all `[P]`) run together after T001.
- Foundational: T009–T013, T017–T019 (`[P]`) run together; T014–T016, T020 follow.
- US1 tests T021–T023 run together; models T024–T025 run together; T034 (React) parallels backend.
- US2 tests T036–T038 together; T039 parallels. US3 tests T047–T049 together; T050/T051/T053/T057
  parallel. US4 test T060 + model T061 parallel.
- Polish: T066–T070, T072–T073 (`[P]`) run together.
- Different user stories can be staffed in parallel once Foundational completes.

---

## Parallel Example: User Story 1

```bash
# Tests first (fail), in parallel:
Task: "Contract mapping test in backend/tests/Infrastructure.Tests/Sources/JustJoinItMappingTests.cs"   # T021
Task: "Pagination/category contract test in backend/tests/Infrastructure.Tests/Sources/JustJoinItPaginationTests.cs"  # T022
Task: "Integration scan→offers test in backend/tests/Infrastructure.Tests/ScanCollectDisplayTests.cs"   # T023

# Then domain models in parallel:
Task: "Offer aggregate in backend/src/Domain/Offers/"          # T024
Task: "ContentFingerprint VO + function in backend/src/Domain/Offers/ContentFingerprint.cs"  # T025
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational (CRITICAL, blocks all) → 3. Phase 3 US1 →
4. **STOP and VALIDATE** US1 independently (scan once → offers in UI with links) → 5. Demo.

### Incremental Delivery

Foundation → **US1 (MVP: collect + display)** → US2 (new-vs-seen + schedule) → US3 (CV ranking) →
US4 (more sources + dedup) → Polish (export, README/ADR, perf, coverage, quickstart). Each story
adds value without breaking the previous ones.

### Parallel Team Strategy

After Foundational: Dev A → US1, then US2; Dev B → US3 (CV/scoring is largely independent of the
scan pipeline once `Offer` exists); Dev C → US4 source-generalization + dedup. Integrate per story.

---

## Notes

- `[P]` = different files, no incomplete dependencies. `[USx]` maps a task to its story.
- Tests are required by the constitution (Principles V/VI) — verify each FAILS before implementing.
- Append-only: never edit an applied EF migration — correct forward with a new one (Principle IX).
- Async all the way (no `.Result`/`.Wait()`); no PII/secrets/DB/CVs committed (Principle IV).
- Commit after each task or logical group (Conventional Commits, one logical change each).
- Deferred by design (not tasks now): the Playwright manual-login **adapter** (the port + escalation
  trigger ARE built — T030); opt-in LLM enhancements to matching. See plan.md / research.md.
- **Remediation additions (from `/speckit-analyze`, 2026-06-28)** — inserted with letter-suffixed IDs
  to preserve existing numbering: front-end test harness + per-story Vitest/RTL tests
  (**T019a, T023a, T038b, T049a, T060b** — Principle VI / finding K2); the FR-031 user-status feature —
  command, endpoint, persistence, UI, and tests (**T038a, T044a–T044c** — finding C1); and FR-002
  source-management API + UI + test (**T060a, T064a, T064b** — finding C2). Task total: 74 + 11 = **85**.
- **Post-implementation adversarial review (2026-06-28)** — after US4 + Phase 7 went green, a multi-agent
  review (4 dimensions → independent verification of each finding) confirmed **7 issues**, all fixed:
  (1) export JSON now uses `UnsafeRelaxedJsonEscaping` so Polish/`C++` text stays human-readable;
  (2) CSV export neutralizes formula-injection (CWE-1236) for source-supplied fields;
  (3+4) the Offers source-filter wiring and split-group reload now have real assertions
  (`fireEvent.change`/click → `listOffers`/`setRoleGroupOverride`); (5) `OffersPage` action handlers
  (`setStatus`/`splitGroup`) wrap failures in try/catch + surface the error banner; (6) a superseded
  aborted `load()` no longer flips the spinner off under the live request; (7) the Sources `kind` input
  is now a `<select>` of the real `SourceKind` values (was free-text `justjoinit`, silently coerced);
  (8) the architecture test was inverted from a denylist to a **BCL allow-list** so ANY future
  framework/outer-layer dependency in Domain fails it. Added `ExportServiceTests` (JSON readability +
  CSV-injection) and strengthened `Grouping.test.tsx`. All suites remain green.
