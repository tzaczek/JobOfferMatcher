# Implementation Plan: LinkedIn Recommended Jobs Source

**Branch**: `008-linkedin-recommended-source` (spec directory; no git branch — repo has no `before_*` hook)
| **Date**: 2026-07-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/008-linkedin-recommended-source/spec.md` (with
Clarifications Session 2026-07-15: **(1)** interactive manual login in an app-controlled browser,
session persisted & reused, **password never stored**; **(2)** the login window **auto-launches
mid-scan** and the attended scan pauses until login; **(3)** the session is **excluded** from the
backup archive (re-login after restore); **(4)** only **manual/attended** scans auto-launch the login
window — **scheduled/unattended** scans record "login required" and never hang the scheduler).

**Constitution**: `.specify/memory/constitution.md` v1.1.0 — **not amended**. Load-bearing here:
Principle IV (personal data private & local — **NON-NEGOTIABLE**: the LinkedIn password is never
stored/transmitted/logged; the session stays local and out of backups), III (no fabricated offers),
IX (recoverable — no data dropped/edited; no migration), X (simple — reuse the deferred port, add no
table). Feature decisions are ADR-1..ADR-6 (Principle XI).

## Summary

Add a **LinkedIn job source** that collects the user's **personalized "Recommended" feed** (P1) and
**saved keyword searches** (P2) using the user's **own logged-in LinkedIn account**, feeding the
existing offers pipeline (dedup, enrichment, fit/affinity, body, tailored CV, application tracking)
with **no special-casing**. Authentication is a **manual, interactive login** in an app-controlled
**headed** browser whose session **persists** locally and is **reused** across scans; the app **never
stores the password**. The login window **auto-launches mid-scan** on manual scans and the scan waits;
**scheduled** scans reuse the persisted session and, if it's invalid, record **"login required"**
without opening a window (so the background scheduler never hangs). The session is **excluded from
backups** (re-login after restore).

Technical approach (from [research.md](./research.md), grounded in the live 001–007 code read during
Phase 0):

- **LinkedIn is the first real `SourceKind.InteractiveBrowser` adapter** — it *activates* the
  previously deferred manual-login path (FR-040). A new `LinkedInSource : IJobSource` replaces the
  `NotConfiguredInteractiveBrowserSource` in the factory's `InteractiveBrowser` arm (routing by
  **kind**, since LinkedIn is the sole interactive source — a per-site discriminator is YAGNI). One
  LinkedIn source is **seeded** (config only; `DefaultLinkedInSourceId = 4444…`; `SeedSourceAsync`
  parameterized for kind + `requiresLogin`). The orchestrator's identity dedup, version/event history,
  reconciliation, Pending enrichment/fit/affinity satellites, `/enrich` worker, export, and backup are
  all reused unchanged — LinkedIn offers are first-class via `ExternalRef(source_id, native_key =
  LinkedIn job id)` + `description_html` body (ADR-1).
- **Authenticated, persisted, headed Playwright context** (ADR-2). A third Playwright consumer,
  `PlaywrightLinkedInClient` (singleton, gate, `IAsyncDisposable` — the `PlaywrightTheProtocolClient`
  shape) but using `LaunchPersistentContextAsync(userDataDir, Headless=false)` so cookies persist. The
  profile dir is OS-app-data-rooted, gitignored, and **outside `cv-data/`** → excluded from the 003
  backup with **zero** backup-code change (FR-012a). The password is only ever typed by the user into
  the headed window (FR-008/009/012; Principle IV).
- **Attended-vs-unattended gate via a scoped `ScanContext`** (ADR-3) — the **only** orchestrator edit.
  `ScanContext.AllowInteractiveLogin = (Trigger == Manual)`, set by `ScanOrchestrator.RunCoreAsync`,
  read by `LinkedInSource`, passed to `EnsureLoggedInAsync`. Manual + no session → launch headed login
  and wait (bounded by `LoginTimeoutMs`); Scheduled/CatchUp/Initial + no session →
  `Failed(LoginNotCompleted)` with no window. `IScanRunner` is scoped and both entry points run in a DI
  scope (request / `ScanSchedulerService.CreateAsyncScope`), so the scoped context is safe. No change
  to `IJobSource.CollectAsync` / `IJobSourceFactory.Create` / the four existing adapters (FR-006/011).
- **One LinkedIn source, multi-pass collection** (ADR-4) — `CollectAsync` runs a Recommended pass then
  N saved-search passes, all under one `source_id`, so a job in several passes upserts **once** (US2
  AC2); each pass is independently tolerant (US2 AC3). Bounded + paced per pass (FR-013/014).
- **Zero migrations** (ADR-5). Search config is additive **jsonb** fields on `JobSourceSearch`
  (`IncludeRecommended`, `LinkedInSearches`) — schemaless, back-compatible with existing rows. No new
  table; `job_source` is already backed up; the session is on disk. `NoAiDependencyTests` +
  `BackupTablesCompletenessTests` stay green untouched (Principles IX/X; FR-016 regression contract).
- **Login UX without a dispatch change** (ADR-6) — the headed browser *is* the login surface; the web
  UI shows an optimistic `waiting_for_login` hint (state already exists) and surfaces
  `LoginNotCompleted` afterward. No new endpoint, no handshake registry, no background executor for a
  single local user (Principle X).

Full design: [research.md](./research.md), [data-model.md](./data-model.md),
[contracts/linkedin-source.md](./contracts/linkedin-source.md), [quickstart.md](./quickstart.md).

## Technical Context

**Language/Version**: C# 14 on **.NET 10** (backend); **TypeScript + React 19** via Vite (frontend).
Unchanged from 001–007.

**Primary Dependencies**:
- Backend: ASP.NET Core 10 minimal APIs; EF Core 10 + `Npgsql` (jsonb); **`Microsoft.Playwright`
  1.61.0 (already referenced)** — now used with `LaunchPersistentContextAsync` (headed). **Added:
  none** — no new NuGet; **no AI SDK** (collection is browser automation, not an AI call — the no-AI
  guard test stays green).
- Frontend: React 19 + Vite + TS, the hand-rolled typed `fetch` client, central design tokens.
  **Added: none.**

**Storage**: PostgreSQL via EF Core. **No new table, no migration.** The only DB shape change is
additive **jsonb** fields on the existing `job_source.search_criteria` column (schemaless). The
LinkedIn login session is an on-disk Playwright persistent-context directory (not a store).

**Testing**: xUnit — Domain (`JobSourceSearch` jsonb round-trip + back-compat; `ScanContext`
attended-ness mapping), Application (`LinkedInSource` passes/dedup/tolerance + login gate, via a fake
`ILinkedInClient`), and **real-PostgreSQL** Infrastructure (Testcontainers): seed + scan-with-fake-
client upsert (identity/body/Pending satellites), unattended-no-session → `Failed/LoginNotCompleted`
with prior offers retained, backup/restore incl. profile-exclusion assertion, and the untouched
`NoAiDependencyTests` + `BackupTablesCompletenessTests`. The **real** `PlaywrightLinkedInClient` is
verified manually per `quickstart.md` (live auth — never in the automated suite). Frontend: Vitest +
RTL for the source-editor LinkedIn fields + the scan-banner login states.

**Target Platform**: local-first, single-user; Windows 11 dev. Foreground ASP.NET Core on
`localhost:5180` serving the SPA. The **headed** login window opens on the user's desktop.

**Performance Goals**: not latency-bound — single user, bounded collection (`MaxResultsPerSearch`,
paced ~<1 req/s per the 001 ADR-2 polite-access risk). The login wait is user-bounded
(`LoginTimeoutMs`, default 3 min).

**Constraints**: **no external AI call** (Principle IV — LinkedIn text reaches only the loopback
`/api/enrichment` worker like every other offer); **password never stored/transmitted/logged**;
session **local + backup-excluded** (FR-012a); polite pacing + tolerate blocks (Partial/Incomplete,
never crash); **no migration, no existing data dropped/edited** (Principle IX); async all the way;
nullable on, warnings-as-errors in Domain + Application.

**Scale/Scope**: 1 user; 3 user stories; **0 new tables**; **0 migrations**; **0 new NuGet/npm deps**;
**0 new REST endpoints** (login rides the existing manual scan); 1 new adapter + 1 new Playwright
client (+ a no-browser fallback) + 1 scoped `ScanContext`; additive jsonb search fields; a small
source-editor + scan-banner UI delta.

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design. All gates PASS — no violations.
Feature decisions are ADR-1..ADR-6 (Principle XI).*

| # | Principle | Status | How this plan satisfies it |
|---|-----------|--------|----------------------------|
| I | Layered architecture, deps inward | ✅ PASS | New Domain is framework-free (`LinkedInSearch` value object; `JobSourceSearch` fields; `ScanContext` is a plain Application holder). Collection orchestration extends the Application-layer `IJobSource`/`IInteractiveBrowserSession` ports; Playwright is confined to Infrastructure (`PlaywrightLinkedInClient`). Commands return `Result`; the feed/scan are read-only. No MediatR (YAGNI). |
| II | Strongly-typed domain, no primitive obsession | ✅ PASS | Reuses wrapped `SourceId`/`OfferId`/`ScanRunId`, `ExternalRef`, `SourceKind`, `TriggerType`, `WorkMode`. `LinkedInSearch` is an immutable record with a non-blank `Keywords` invariant. No raw `Guid`/`int` in Domain/Application. |
| III | The Tracker Reflects Reality (NON-NEG) | ✅ PASS | Identity is the real LinkedIn job id; a missing salary/body shows "not available"; a blocked/expired scan yields an honest `Partial`/`Failed` outcome — **never fabricated offers**. New-vs-seen by identity existence, never source dates (reused). |
| IV | Personal Data Private & Local (NON-NEG) | ✅ PASS | **No external AI call, no AI SDK** (guard test upheld). The **password is only typed into the headed browser** — never read/stored/transmitted/logged. The session (cookies) lives on disk, gitignored, and **excluded from the 003 backup** (FR-012a). No session/cookie material crosses any endpoint. LinkedIn text reaches only the loopback `/enrich` worker, exactly like other offers. Collection from LinkedIn is the 001 ADR-2 accepted source-access risk. |
| V | Real Database in Tests | ✅ PASS | Seed, the jsonb search round-trip, scan upsert (identity/body/Pending satellites), the unattended login-required outcome + prior-offer retention, and backup/restore are tested on **real PostgreSQL** (Testcontainers) with a **fake `ILinkedInClient`** (offline). No mocked DB. |
| VI | Green Before Done (NON-NEG) | ✅ PASS | Each story closes only on a green local suite; the untouched `NoAiDependencyTests` + `BackupTablesCompletenessTests` + 001–007 suites are the regression contract. LinkedIn tests never hit live network (fake client). |
| VII | UI Changes Require Visual Verification | ✅ PASS | The source-editor LinkedIn fields, the `waiting_for_login`/`LoginNotCompleted` banner states, and the **real headed-login flow** are run-and-looked-at per `quickstart.md`; if the headed browser can't launch in-session, that's stated, not claimed working. |
| VIII | One Source of Design Truth | ✅ PASS | The source-editor + banner reuse existing form/chip/button classes + design tokens; any new label/state styling is a token/class, not a scattered literal. |
| IX | Your Data Is Recoverable | ✅ PASS | **Zero migrations**; additive jsonb only; **no** prior migration/column/data edited. `job_source` (LinkedIn config) is already in the backup; **no new table** → the completeness guard is untouched. The session is deliberately backup-excluded (Q3/FR-012a) — a documented, recoverable-by-re-login choice. |
| X | Simple by Default (YAGNI) | ✅ PASS | Reuses the deferred `InteractiveBrowser` kind + `IInteractiveBrowserSession` port + the TheProtocol Playwright/DI-swap pattern + the whole downstream pipeline. **No new table, no migration, no new endpoint, no new dependency, no new worker command, no handshake registry, no dispatch change.** The attended gate rides a tiny scoped context; search config rides existing jsonb. |
| XI | Documented Decisions, Immutable History | ✅ PASS | ADR-1..ADR-6 below. Conventional Commits, one logical change each; no `--no-verify`, no history rewrite. |

**Decisions (ADR-style, per Principle XI):**

- **ADR-1 — LinkedIn is the first real `SourceKind.InteractiveBrowser` adapter; routed by kind; one
  seeded source.** *Context*: the port, kind, stub, routing hook, gitignore reservations, and
  Playwright NuGet all pre-exist for exactly this (001 research §2 / FR-040); LinkedIn is the only
  login-gated source. *Decision*: `LinkedInSource : IJobSource` replaces the `NotConfiguredInteractive
  BrowserSource` in the factory's `InteractiveBrowser` arm; seed one LinkedIn `job_source`
  (`DefaultLinkedInSourceId`, `SeedSourceAsync` parameterized). *Rationale*: max reuse of the
  source-agnostic orchestrator + downstream pipeline for the least new surface (Principle X); routing
  by kind supports FR-007 without a discriminator column. *Rejected*: per-GUID routing (blocks
  user-created LinkedIn sources); a new `SourceKind.LinkedIn` (enum churn for no gain); a "site"
  column (migration for one adapter).

- **ADR-2 — Authenticated, persisted, headed Playwright context; password never stored.** *Context*:
  Clarification Q1; the recommended feed is only available to the logged-in user. *Decision*:
  `PlaywrightLinkedInClient` (singleton, gate, `IAsyncDisposable`) via
  `LaunchPersistentContextAsync(userDataDir, Headless=false)`; profile OS-app-data-rooted, gitignored,
  outside `cv-data/`. *Rationale*: persistent context is the one feature giving "log in once, reuse"
  (SC-003) with **zero** credential handling; OS-app-data rooting survives `dotnet clean` and keeps the
  session out of the backup (FR-012a) for free. *Rejected*: stored credentials / headless auto-login
  (Q1 rejected — Principle IV, brittle vs 2FA); importing the everyday Chrome profile (Q1 rejected);
  persisting the session in the DB or `cv-data/` (would land in the backup — FR-012a).

- **ADR-3 — Attended gate via a scoped `ScanContext`; the single orchestrator edit.** *Context*:
  Clarifications Q2 + Q4; a scheduled scan must never hang on a human. *Decision*: scoped `ScanContext`
  (`AllowInteractiveLogin = Trigger == Manual`) set by `ScanOrchestrator.RunCoreAsync`, read by
  `LinkedInSource`, passed to `EnsureLoggedInAsync(interactive, …)`; manual → launch & wait, scheduled
  → `Failed(LoginNotCompleted)` no window. *Rationale*: the orchestrator already knows the trigger;
  `IScanRunner` is scoped and both entry points run in a DI scope (verified) — so the context is
  isolated per scan and threads exactly what's needed **without** touching `IJobSource.CollectAsync` or
  the four existing adapters (Principle X). *Rejected*: a `CollectAsync` parameter (ripples through
  five adapters); always-launch (Q4 rejected — hangs the scheduler); a standalone connect endpoint (Q2
  rejected).

- **ADR-4 — One LinkedIn source, multi-pass collection under one `source_id`.** *Context*: US2 AC2
  requires a job in *both* the feed and a search to be one offer; offer identity is `(source_id,
  native_key)`. *Decision*: `CollectAsync` runs a Recommended pass + N saved-search passes into the
  same `onOffer`/`context.Seen`; passes are independently tolerant. *Rationale*: cross-pass dedup falls
  out of the existing machinery for free; independent tolerance satisfies AC3; simpler than N sources.
  *Rejected*: a source row per search (same job in two rows → two offers, violating AC2); a single
  blended pass (loses per-pass tolerance).

- **ADR-5 — Zero migrations: additive jsonb search fields; no new table.** *Context*: `search_criteria`
  is jsonb; `job_source` is already backed up; the session is on disk. *Decision*: add
  `IncludeRecommended` + `LinkedInSearches` to `JobSourceSearch` (schemaless jsonb) and nothing else to
  the schema. *Rationale*: the strongest possible Principle IX/X story — no migration, no backup-list
  change, no completeness-guard change, back-compatible with every existing source row. *Rejected*: a
  new `linkedin_config` column (migration); a searches table (table + migration + backup entry for a
  few strings).

- **ADR-6 — Login UX without a dispatch change; headed window is the surface.** *Context*: Q2
  (auto-launch mid-scan; scan pauses); the manual scan already `await`s `RunAsync`; `waiting_for_login`
  already exists in the frontend. *Decision*: keep synchronous dispatch; the headed browser is the
  login UI; show an optimistic `waiting_for_login` hint + surface `LoginNotCompleted`; bound with
  `LoginTimeoutMs`. *Rationale*: satisfies "the scan pauses until login" literally (the awaited login
  step) with no new endpoint/registry/executor for one local user (Principle X). *Rejected (noted for a
  future need)*: background-dispatch + a `ScanRunId`-keyed handshake registry + status-driven
  `waiting_for_login` (001 research §2) — more robust for multiple viewers, deferred as YAGNI.

## Project Structure

### Documentation (this feature)

```text
specs/008-linkedin-recommended-source/
├── plan.md              # This file
├── research.md          # Phase 0 — R1..R9 + testing strategy, grounded in the live 001–007 code
├── data-model.md        # Phase 1 — jsonb search fields, offer mapping, session, ScanContext, outcomes, config, backup/export, invariants
├── quickstart.md        # Phase 1 — run & per-user-story validation (real headed-login path)
├── contracts/
│   └── linkedin-source.md  # Phase 1 — IJobSource/IInteractiveBrowserSession/ILinkedInClient/ScanContext + DTO deltas + URL shapes
├── spec.md              # Feature spec (with Clarifications 2026-07-15)
└── checklists/
    └── requirements.md  # Spec quality checklist (16/16)
```

### Source Code (repository root) — delta over features 001–007

```text
backend/
├── src/
│   ├── Domain/
│   │   └── Sources/
│   │       ├── JobSourceSearch.cs        # EDIT: +IncludeRecommended (bool), +LinkedInSearches (IReadOnlyList<LinkedInSearch>) — additive jsonb
│   │       └── LinkedInSearch.cs          # NEW: value object {Keywords(req), Location?, GeoId?, Distance?, Recency?}
│   ├── Application/
│   │   └── Scanning/
│   │       ├── ScanContext.cs             # NEW: scoped holder {RunId, Trigger, AllowInteractiveLogin=Trigger==Manual, Begin(...)}
│   │       ├── ScanOrchestrator.cs        # EDIT (ONE line): scanContext.Begin(run.Id, request.Trigger) at RunCoreAsync start; +ctor ScanContext
│   │       └── IInteractiveBrowserSession.cs # EDIT: EnsureLoggedInAsync gains `bool interactive`
│   ├── Infrastructure/
│   │   ├── Sources/
│   │   │   ├── LinkedIn/
│   │   │   │   ├── LinkedInSource.cs       # NEW: IJobSource (Kind=InteractiveBrowser); reads ScanContext; recommended+search passes → onOffer; FetchBodyAsync
│   │   │   │   ├── ILinkedInClient.cs      # NEW: port : IInteractiveBrowserSession + FetchListAsync + FetchBodyAsync (+ request/result/card records)
│   │   │   │   ├── PlaywrightLinkedInClient.cs # NEW: singleton persistent HEADED context; login-detect poll; bounded/paced list+body reads; IAsyncDisposable
│   │   │   │   ├── NotConfiguredLinkedInClient.cs # NEW: UseBrowser=false fallback (login-not-completed / empty)
│   │   │   │   └── LinkedInOptions.cs      # NEW: Sources:LinkedIn (UseBrowser, Headless=false, ProfilePath, timeouts, MaxResultsPerSearch, URLs, UA/Locale)
│   │   │   ├── JobSourceFactory.cs         # EDIT: InteractiveBrowser arm → LinkedInSource (was NotConfiguredInteractiveBrowserSource)
│   │   │   └── Browser/NotConfiguredInteractiveBrowserSession.cs # EDIT: signature +bool interactive (still returns NotConfigured)
│   │   ├── Persistence/Seed/DatabaseSeeder.cs # EDIT: +DefaultLinkedInSourceId; seed the LinkedIn source; SeedSourceAsync +kind +requiresLogin params
│   │   └── DependencyInjection.cs          # EDIT: Configure<LinkedInOptions>; UseBrowser-gated ILinkedInClient (Playwright vs NotConfigured), both as IInteractiveBrowserSession
│   ├── Application/Scanning/ScanContext.cs # (registered) DI: AddScoped<ScanContext> in Application/DependencyInjection.cs
│   └── Web/Endpoints/SourceEndpoints.cs    # EDIT: SearchCriteriaDto +IncludeRecommended +LinkedInSearches; ToSearch/ToDto map them (no new endpoint)
└── tests/
    ├── Domain.Tests/                        # JobSourceSearch jsonb round-trip + back-compat; LinkedInSearch invariant; ScanContext mapping
    ├── Application.Tests/                    # LinkedInSource: passes+dedup (AC2), one-pass-fail→Partial (AC3), attended vs unattended login gate (fake client+context)
    └── Infrastructure.Tests/                # real PG: seed LinkedIn source; scan-with-fake-client upsert (identity/body/Pending satellites, SC-005);
                                              #   unattended-no-session → Failed/LoginNotCompleted + prior offers retained (SC-004/FR-015);
                                              #   backup/restore + profile-exclusion assertion (FR-012a); NoAiDependencyTests + BackupTablesCompletenessTests still green

frontend/
└── src/
    ├── api/
    │   ├── types.ts                         # EDIT: SearchCriteriaDto +includeRecommended? +linkedInSearches?[]; (ScanState already has waiting_for_login/challenge_detected)
    │   └── sources.ts                       # EDIT: carry the new criteria fields through create/update
    ├── pages/Sources/SourcesPage.tsx        # EDIT: when kind==='InteractiveBrowser' → "Include recommended" + saved-searches editor (keywords/location/geoId/distance/recency)
    └── pages/Offers/ScanBanner.tsx          # EDIT: manual login-required scan → waiting_for_login hint; finished LoginNotCompleted → "run a manual scan to sign in"
```

**Structure Decision**: Web-application layout, unchanged. The feature is **additive**: one new
`IJobSource` adapter + its Playwright client (and a no-browser fallback) + `LinkedInOptions`, a small
`LinkedInSearch` value object + two additive jsonb fields, a tiny scoped `ScanContext` (one
orchestrator line), a factory arm swap, a seeder entry, extended source DTOs (no new endpoint), and a
modest source-editor + scan-banner UI delta. **No new table, no migration, no new dependency, no new
endpoint.** 001–007 behaviour is preserved; Domain stays framework-free; **no existing data is dropped
or edited**.

## Phase Status

- [x] Phase 0 — Research (`research.md`): R1–R9 + testing strategy resolved against the live 001–007
  code (the source-agnostic orchestrator + `ExternalRef` identity/dedup; the deferred
  `InteractiveBrowser`/`IInteractiveBrowserSession` path; the TheProtocol Playwright + `UseBrowser`
  DI-swap pattern; the scoped `IScanRunner` + `ScanSchedulerService.CreateAsyncScope`; the jsonb
  `search_criteria`; the gitignored `browser-profiles/`; the frontend `waiting_for_login` state). All
  four spec clarifications fed the design directly; no `NEEDS CLARIFICATION` remain.
- [x] Phase 1 — Design & Contracts (`data-model.md`, `contracts/linkedin-source.md`, `quickstart.md`);
  agent context (CLAUDE.md active-feature pointer) updated to this feature.
- [ ] Phase 2 — Tasks (`/speckit-tasks`) — **not** produced by this command.

## Complexity Tracking

*No Constitution violations. Two items are worth recording for transparency (Principle X):*

| Item | Why needed | Simpler alternative rejected because |
|------|-----------|--------------------------------------|
| LinkedIn search fields added to the shared `JobSourceSearch` record | Reuses the existing jsonb column + source CRUD/endpoints/UI with **no migration** (Principle IX); the LinkedIn source must carry recommended-toggle + saved searches somewhere persisted. | A dedicated `linkedin_config` column or table means a migration + backup-list change for a handful of strings; overloading the neutral fields is lossy (no home for location/distance/recency). Mild "source-neutral" stretch accepted. |
| A **headed**, persistent Playwright context (a third browser consumer, launched differently from the two ephemeral ones) | Manual login + session reuse across scans is impossible with an ephemeral anonymous context; only `LaunchPersistentContextAsync` (headed) lets the user type credentials/2FA once and stay logged in (Clarification Q1, SC-003). | An ephemeral/headless context can't hold a login or show a login UI; a shared browser host doesn't exist today and building one now is speculative (each existing consumer owns its browser — Principle X). |
