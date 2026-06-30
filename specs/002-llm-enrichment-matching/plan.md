# Implementation Plan: LLM Enrichment & Matching (Claude-as-Worker)

**Branch**: `002-llm-enrichment-matching` (spec directory; no git branch — repo has no `before_*` hook)
| **Date**: 2026-06-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/002-llm-enrichment-matching/spec.md` (with Clarifications
Session 2026-06-29)

**Constitution**: `.specify/memory/constitution.md` v1.1.0 — **not amended**; one load-bearing
*decision* is superseded for this feature via ADR-1 below (Principle XI; Principle IV upheld).

## Summary

Turn the raw, inconsistent offer feed and the keyword-derived CV profile into **AI-generated**
outputs — per-offer summary + key skills, a recruiter-style CV profile (skills/seniority/summary), and
a 0–100 fit score with matched/missing + rationale — where the **LLM engine is Claude Code running
under the user's Claude Max plan, never the paid Anthropic API**. The backend stays a passive,
local-first data store + **enrichment queue**; the user's own Claude Code session is the **worker**
that drains the queue and writes results back (FR-012 / SC-005). Claude is the **sole** source of
these outputs — un-produced items render as **"pending"**, never a non-AI fallback (FR-005).

Technical approach (from [research.md](./research.md), grounded in the feature-001 code, 2026-06-29):

- **Queue (pull-based, loopback-only)**: new `/api/enrichment` endpoints — `GET /pending` (ordered,
  self-contained work items + inputs + guidance), `POST /results` (batch write-back), `GET /status`
  (counts), `POST /rerun` (in-app trigger / backfill). A re-runnable Claude Code slash command
  `.claude/commands/enrich.md` (`/enrich`) is the worker. The backend imports **no AI SDK** and makes
  **no** outbound AI call. (ADR-1, [contracts/](./contracts/).)
- **Staleness by input-hash + eager state**: framework-free `Domain/Enrichment` hash composers
  (reusing `ContentFingerprint`) key each output to its own inputs (summary ← offer content incl.
  description; fit ← offer + profile + weights; profile ← CV bytes). Known changes eagerly write
  `state=Pending` (eligibility-gated `COUNT` for FR-010); on write-back the server **recomputes the
  current hash from live inputs** and rejects a mismatch as `stale` (the stored `inputs_hash` is only a
  read-path backstop), so a value based on superseded inputs is never shown (FR-006/FR-007/SC-004).
- **Persistence**: two new 1:1 satellite tables `offer_enrichment` + `offer_fit` (PK=`OfferId`, FK→
  `offers` cascade), plus AI-profile columns on `candidate_cv` and one `enrichment` jsonb column on
  `app_settings`. Lists as jsonb; **no new wrapped IDs**; **one** append-only migration `LlmEnrichment`.
  Satellite rows are eagerly materialized (one per offer); an **idempotent startup backfill**
  (`DatabaseInitializer`) creates them for pre-existing offers (FR-014). Feature-001 collection
  *behavior* is unchanged — the scan path is only **additively** extended (satellite create/invalidate
  + a silent Minor-content refresh, ADR-3); a satellite write never rolls back the offers upsert (FR-013).
- **CV reading**: the worker (Claude Code on the **same host** as the app) reads the **original PDF**
  by local path (the binary never traverses HTTP); retained **PdfPig** is only a readability gauge +
  text fallback (FR-003). "Unreadable" is a distinct terminal worker verdict (no retries), separate
  from retry-exhausted "failed". CV/offer **text** (path, fallback text, descriptions, profile) is
  serialized only over the **loopback-restricted** enrichment channel — never to an external service
  (SC-005). The supported worker run mode is the **host-process** deployment (see ADR-4).
- **Configurable limits**: `EnrichmentSettings` (summary/skills/rationale caps + retry limit) on the
  `AppSettings` singleton, surfaced via `GET/PUT /api/settings/enrichment` (FR-018). Existing weight
  sliders relabelled "guidance to Claude" (FR-011).
- **Supersede the non-AI path**: remove `Scorer.Score`/`FitScore`/`FitBreakdown` and the keyword
  profiler (`CvProfileBuilder`/`SkillCatalog`/`SkillRef`/…) — dropping the only **FuzzySharp**
  consumer — per FR-005; retain PdfPig and the pure feed-ordering helpers `CombinedRank`/`DegradedRank`.

The full design is in [research.md](./research.md), [data-model.md](./data-model.md),
[contracts/](./contracts/), and [quickstart.md](./quickstart.md).

## Technical Context

**Language/Version**: C# 14 on **.NET 10** (backend); **TypeScript + React 19** via Vite (frontend).
Unchanged from feature 001.

**Primary Dependencies**:
- Backend: ASP.NET Core 10, EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL`, **UglyToad.PdfPig**
  (CV readability gauge + text fallback — **retained**), Cronos (scan scheduler — untouched),
  `Ganss.Xss` (`IHtmlSanitizer`, reused for offer description → plain text). **Removed**:
  **FuzzySharp** (its only consumer, the keyword CV profiler, is deleted per FR-005). **Added**:
  **none** — explicitly **no** `@anthropic-ai`/AI SDK (FR-012).
- Frontend: React 19, Vite, TypeScript, the existing typed API-client layer + central design tokens.
- **Worker**: Claude Code (the user's Max plan) — an *external tool*, not a backend dependency.

**Storage**: PostgreSQL via EF Core, **append-only** migrations applied at startup (`MigrateAsync`).
One new migration `LlmEnrichment` (two satellite tables + `candidate_cv`/`app_settings` columns).

**Testing**: xUnit unit tests (Domain/Application); **real-PostgreSQL** integration tests via
Testcontainers (Principle V) for the new tables, jsonb round-trips, and the end-to-end
pending→results→feed path (offline, deterministic). The **loopback guard** is an HTTP-layer concern
tested as a middleware/filter unit test (incl. the **fail-closed** null/unknown `RemoteIpAddress`
case), not via the DB suite. An **architecture/guard test** asserts no AI package is referenced
(`Directory.Packages.props`/csprojs) so FR-012/SC-005 is enforced, not just inspected. The untouched
feature-001 suite (incl. the scan *Unchanged* branch — ADR-3) is the FR-013 regression contract.
Frontend: Vitest + React Testing Library for the pending/produced/failed/unreadable rendering and the
enrichment client.

**Target Platform**: local-first, single-user; Windows 11 dev. The app runs as a foreground ASP.NET
Core process on `localhost`; the worker is an interactive Claude Code session in the same repo.

**Project Type**: Web application (existing `backend/` + `frontend/`).

**Performance Goals**: not latency-bound — enrichment is **on-demand** and asynchronous; un-processed
items show "pending". A worker pass processes pending work in batches (default 25, max 100) until
drained. Targets are completeness, not latency: SC-001 100% summaries, SC-003 ≥95% fits (both over
non-failed available offers), SC-004 100% fit refresh on CV/weights change.

**Constraints**: local-first; **no external AI service**; backend transmits **0** offer/CV records
externally (SC-005); CV bytes never leave disk; `/api/enrichment/*` loopback-only; append-only
migrations; async all the way; nullable on, warnings-as-errors in Domain + Application.

**Scale/Scope**: 1 user; ~180 offers/source/scan ⇒ ~180 `offer_enrichment` + ~180 `offer_fit` rows;
1 active CV; 5 user stories (US1 ✅ delivered; US2–US5 unbuilt). Each derived output is a 1:1 mutable
cache — table size is bounded by offer count.

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design. All gates PASS (one decision
superseded by ADR-1; Principle IV — the only relevant NON-NEGOTIABLE — is upheld).*

| # | Principle | Status | How this plan satisfies it |
|---|-----------|--------|----------------------------|
| I | Layered architecture, deps inward | ✅ PASS | New hashing + state machines + VOs live in framework-free **Domain/Enrichment**; ports (`IEnrichmentRepository`, `EnrichmentService`) in **Application**; EF config + endpoints in **Infrastructure/Web**. Commands return `Result<T>`; the pending-work query is read-only. No MediatR (YAGNI). |
| II | Strongly-typed domain, no primitive obsession | ✅ PASS | Reuse `OfferId`/`CvId` as satellite PKs (no raw `Guid`); new VOs `InputHash`, `CvProfile`, `EnrichmentSettings`; enums `EnrichmentState`, `CvProcessingState`. `Result<T>` for expected failures (stale write, invalid settings, CV-not-found). |
| III | The Tracker Reflects Reality (NON-NEG) | ✅ PASS | AI outputs are derived caches **explicitly** marked pending/produced/failed/unreadable; **no fabricated fit** is ever shown (FR-005). Fit is no longer "derived on read, never stored" — it is now **stored**, the accepted change recorded in **ADR-1**. No demo/placeholder enrichment is persisted. |
| IV | Personal Data Private & Local (NON-NEG) | ✅ PASS | Backend calls **no** external AI API and adds **no** AI SDK (enforced by an automated no-AI-package guard test, FR-012/SC-005). The CV **binary** is delivered as a **filesystem path** (never sent over HTTP); however the enrichment channel *does* transmit CV/offer **text** (path, PdfPig fallback text, profile, offer descriptions) to the local worker over **loopback** — so the load-bearing PII control is (a) zero external egress + (b) an **enforceable, fail-closed loopback guard** on `/api/enrichment/*` (403 for non-loopback/unknown remote IP). This holds in the supported **host-process** run mode (ADR-4); `cv-data/` stays gitignored. SC-005 (0 records transmitted externally) holds in all modes. |
| V | Real Database in Tests | ✅ PASS | New tables, migration, jsonb round-trips, loopback guard, and the pending→results→feed path tested on **real PostgreSQL** (Testcontainers). The DB is never mocked. |
| VI | Green Before Done (NON-NEG) | ✅ PASS (process) | Each user story closes only on a green local suite. The untouched feature-001 suite is the FR-013 regression contract. |
| VII | UI Changes Require Visual Verification | ✅ PASS | New states (pending/produced/failed/unreadable on offer cards + CV page; the pending/failed indicator; the enrichment settings section) are run-and-looked-at before "done". |
| VIII | One Source of Design Truth | ✅ PASS | New status colors (pending/failed/unreadable) added to the central design tokens / theme (`theme/tokens.css`, `base.css`, `index.ts`) — no scattered literals; reuse the existing `statusChipClass` pattern. |
| IX | Your Data Is Recoverable | ✅ PASS | **One** new append-only migration; **no** prior migration edited. AI outputs are recomputable caches (no irreplaceable user data). Export behavior (FR-013) unchanged. |
| X | Simple by Default (YAGNI) | ✅ PASS | Pull queue with optimistic `inputs_hash` concurrency over a leased/dead-letter queue; the `state` column **is** the queue (no separate work-queue table); reuse `OfferId`/`CvId` (no new id types); jsonb arrays (no child skill tables); a slash-command worker (no hosted service / no subprocess driving). |
| XI | Documented Decisions, Immutable History | ✅ PASS | **ADR-1** records the superseded "CV matching fully local" decision (not a silent rewrite). Conventional Commits, one logical change each. |

**Decisions (ADR-style, per Principles XI + IV):**

- **ADR-1 — Persisted AI outputs via a local Claude-Code worker (accepted; no constitution
  amendment, no semver bump).**
  - *Context*: Feature 001 used a **local keyword/FuzzySharp matching implementation** and the rule
    "fit derived on read, never stored (FR-035)". Feature 002 makes Claude the sole source of
    summaries/profile/fit and **persists** those outputs, recomputing on input change.
  - *Decision*: Supersede precisely two **load-bearing decisions** (neither is a numbered Principle),
    for feature 002 only: (a) the **keyword/FuzzySharp local-compute matching implementation** and
    (b) **"fit derived on read, never stored (FR-035)"**. **Locality is preserved** — the LLM engine
    is **Claude Code under the user's Max plan**, the backend calls no external AI API and sends no
    offer/CV data off-machine, so "CV matching fully local" still holds and the NON-NEGOTIABLE
    **Principle IV is upheld**. The constitution's External-services clause already requires "any LLM
    integration … must satisfy Principle IV before it ships", so an ADR (Principle XI), not an
    amendment, is the correct reconciliation.
  - *Consequences*: fit is now stored (two satellite tables); the non-AI fit/profile producers are
    removed (FR-005, see ADR-2); `/api/enrichment/*` is loopback-only; the CV is read locally by the
    worker. SC-005 (0 records transmitted externally) is the measurable guarantee.
- **ADR-2 — Remove the non-AI scorer + keyword profiler (FR-005); drop FuzzySharp; retain PdfPig.**
  **Remove** `Scorer.Score`/`FitScore`/`FitBreakdown`/`ScoringInput`; `CvProfileBuilder`/
  `ICvProfileBuilder`/`SkillCatalog`/`SkillCatalogLoader`/`skill-catalog.json`/`SkillRef`/
  **`CandidateProfile`**/`CandidateProfileMerger` (the only **FuzzySharp** consumer → remove the
  package + version + the `skill-catalog.json` copy directive). In the `LlmEnrichment` migration
  **drop the `derived_profile` column** and its `HasJsonbConversion<CandidateProfile>()` mapping
  (a recomputable keyword cache, no user data — Principle IX intact). **Remove DI**: the
  `SkillCatalog` singleton + `ICvProfileBuilder→CvProfileBuilder`, and the `ICvProfileBuilder` param
  from `CvService`. **Rework overlooked consumers** (build/Principle-VI): `ProfileService`
  (`BuildEffectiveProfileAsync`/`GetAsync`/`ProfileView` source the effective profile from the new
  `CvProfile` + preferences, not `CandidateProfileMerger`/`DerivedProfile`); `CvEndpoints.ToCvDto`
  (project the new AI profile + `state`/`summary`/`attemptCount`). **Tests**: delete `ScorerTests.cs`;
  rewrite `CvExtractionTests.cs` (keep the PdfPig degradation test, drop the `CvProfileBuilder`
  assertion). **Retain** **PdfPig** (readability gauge + fallback) and the pure feed-ordering helpers
  `Scorer.CombinedRank`/`DegradedRank` (ordering only — never a *displayed* fit). Keeping any non-AI
  fit/profile producer would be exactly the fallback FR-005 forbids.
- **ADR-3 — Feature-001 collection *behavior* preserved (FR-013) via additive, separately-owned
  data.** All new data lives in new tables/columns; the worker writes the satellites. The scan path
  is **additively extended** (not "writes only offers"): it creates `Pending` satellite rows for new
  offers and **eagerly invalidates** them on content change, and performs a **silent Minor-content
  refresh** (`Offer.RefreshMinorContent`) so a description-only change is persisted for the summary
  hash (FR-006). This adds writes to **new** tables and updates the **displayed** denormalized
  description/company/location (intended; no "Updated" marker/version/event/availability/dedup/
  grouping/salary/export change). A satellite-row write **must not roll back** the `offers` upsert (the
  read-path hash backstop covers any drift). The feature-001 suite — explicitly including the scan
  *Unchanged* branch — is the regression contract.
- **ADR-4 — Worker runs against the host-process deployment; loopback + shared filesystem.** The
  enrichment worker is Claude Code on the **same host** as the app; the supported mode is the default
  `./start.ps1` (Postgres in docker, **app via `dotnet run`** on `localhost:5180`), where loopback is
  genuine and the worker shares the `cv-data/` filesystem to read the CV by path. The `/api/enrichment/*`
  guard is **fail-closed** loopback. For the full-container `docker-compose` `app` packaging, enrichment
  is **not** supported as-is (the host worker can't reach the container's loopback or read the
  container path); to enable it, bind the published port to `127.0.0.1`, mount `cv-data` to a host
  path so the emitted path resolves, or run the app as a host process for the enrichment session.
  This is documented rather than papered over with a bridge-subnet allowance (which would expose CV
  text on `0.0.0.0`).

**Complexity Tracking**: No constitution violations — table omitted (N/A).

## Project Structure

### Documentation (this feature)

```text
specs/002-llm-enrichment-matching/
├── plan.md              # This file
├── research.md          # Phase 0 — R1..R5 decisions, rationale, alternatives
├── data-model.md        # Phase 1 — entities, VOs, hashing, state machines, migration
├── quickstart.md        # Phase 1 — run & per-user-story validation guide
├── contracts/           # Phase 1 — REST + worker contracts
│   ├── enrichment-api.md
│   └── worker-protocol.md
├── spec.md              # Feature spec (with Clarifications)
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root) — delta over feature 001

```text
backend/
├── src/
│   ├── Domain/
│   │   ├── Enrichment/                 # NEW: EnrichmentState, InputHash, OfferEnrichment, OfferFit,
│   │   │                               #      OfferEnrichmentInputs, OfferFitInputs, EffectiveProfile (pure)
│   │   ├── Cv/                         # +CvProcessingState, +CvProfile; CandidateCv extended (profile state/attempts/hash)
│   │   ├── Settings/                   # +EnrichmentSettings; AppSettings.Enrichment
│   │   ├── Offers/                     # +Offer.RefreshMinorContent (silent Minor refresh; ADR-3)
│   │   └── Matching/                   # REMOVE Scorer.Score/FitScore/FitBreakdown/ScoringInput/SkillRef/
│   │                                   #   CandidateProfile/CandidateProfileMerger;
│   │                                   # RETAIN CombinedRank/DegradedRank, ScoringWeights, SeniorityLevels
│   ├── Application/
│   │   ├── Enrichment/                 # NEW: IEnrichmentRepository, EnrichmentService (pending/results/status/rerun),
│   │   │                               #      ordering + eligibility-gated counts + recompute guard + loose validation
│   │   ├── Cv/                         # CvService: drop ICvProfileBuilder param/keyword path; ProfileService:
│   │   │                               #   rework BuildEffectiveProfileAsync/GetAsync/ProfileView off the new CvProfile
│   │   ├── Settings/                   # SettingsService.UpdateEnrichmentAsync
│   │   └── Offers/                     # OfferReadModels: +summary/keySkills/state, FitView +rationale/state;
│   │                                   #   OfferReadService: two-tier default order (produced-fit first)
│   ├── Infrastructure/
│   │   ├── Persistence/Configurations/ # NEW OfferEnrichment/OfferFit configs; extend CandidateCv (drop derived_profile
│   │   │                               #   mapping) + AppSettings configs
│   │   ├── Persistence/Migrations/     # NEW 2026XXXX_LlmEnrichment.cs (+ snapshot): 2 tables, CV/settings cols, drop derived_profile
│   │   ├── Persistence/               # DatabaseInitializer: idempotent Pending-satellite-row BACKFILL after MigrateAsync (FR-014)
│   │   ├── Persistence/Repositories/   # NEW EnrichmentRepository; OfferReadService stops calling Scorer.Score
│   │   ├── Cv/                         # PdfPig RETAINED; REMOVE CvProfileBuilder/SkillCatalogLoader/skill-catalog.json
│   │   └── DependencyInjection.cs      # REMOVE SkillCatalog singleton + ICvProfileBuilder reg; Infrastructure.csproj +
│   │                                   #   Directory.Packages.props: drop FuzzySharp package/version + json copy directive
│   └── Web/
│       └── Endpoints/                  # NEW EnrichmentEndpoints (pending/results/status/rerun, fail-closed loopback guard);
│                                       # SettingsEndpoints +enrichment GET/PUT; CvEndpoints.ToCvDto → project AI profile;
│                                       # FeatureEndpoints wires the group
└── tests/
    ├── Domain.Tests/                   # hashing determinism + description-inclusion; state machines; DELETE ScorerTests.cs
    ├── Application.Tests/              # ordering (profile-before-fit, available-first, newest-first), eligibility-gated
    │                                   #   counts, recompute-guard stale rejection, retry→failed, Failed-rearm, backfill
    └── Infrastructure.Tests/          # real Postgres: new tables, migration, jsonb, e2e path; rewrite CvExtractionTests.cs
                                        #   (keep PdfPig degradation, drop keyword builder); + no-AI-package guard test;
                                        #   loopback guard tested at the HTTP/middleware layer (not via Testcontainers)

frontend/
└── src/
    ├── api/                           # +enrichment client + types; settings.ts +getEnrichment/updateEnrichment
    ├── components/                    # OfferCard: summary/keySkills + fit (score/rationale) + pending/failed states
    ├── pages/                         # CV page: profile state + skills/seniority/summary; Settings: EnrichmentSection;
    │                                   # global pending/failed enrichment indicator + manual re-run
    └── theme/                         # +pending/failed/unreadable chip tokens (tokens.css, base.css, index.ts)

.claude/commands/enrich.md             # NEW: the /enrich worker slash command (worker-protocol.md)
```

**Structure Decision**: Web-application layout, unchanged. The feature is **additive**: new
Domain/Application/Infrastructure/Web pieces for enrichment, two new satellite tables, and a
repo-local Claude Code worker command. Feature-001 collection/scan code paths are not modified except
the single additive `RefreshMinorContent` hook (ADR-3). Domain stays framework-free; the worker is an
external Claude Code session, not a runtime backend dependency.

## Phase Status

- [x] Phase 0 — Research (`research.md`): 5 unknowns resolved (R1–R5) via codebase-grounded
  fan-out + reconciliation; all `NEEDS CLARIFICATION` resolved.
- [x] Phase 1 — Design & Contracts (`data-model.md`, `contracts/enrichment-api.md`,
  `contracts/worker-protocol.md`, `quickstart.md`); agent context (CLAUDE.md) updated to point here.
- [ ] Phase 2 — Tasks (`/speckit-tasks`) — **not** produced by this command.

## Complexity Tracking

> No Constitution Check violations — this section is intentionally empty (N/A). The one superseded
> *decision* (not a Principle) is recorded in ADR-1, not as a violation.
