# Implementation Plan: Application Affinity Metric & Offer Detail Body

**Branch**: `006-application-affinity-metric` (spec directory; no git branch — repo has no `before_*` hook)
| **Date**: 2026-07-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/006-application-affinity-metric/spec.md` (with Clarifications
Session 2026-07-01: **(1)** affinity is produced by the **local Claude-Code worker** like fit — no
external AI call, no non-AI fallback; **(2)** basis = **all applied offers, weighted equally**
(outcome-agnostic); **(3)** the offer body is fetched **eagerly during a scan**; **(4)** affinity shows
"insufficient history" below **3** applied offers).

**Constitution**: `.specify/memory/constitution.md` v1.1.0 — **not amended**. The NON-NEGOTIABLE Principles
III (trustworthy, no fabricated values), IV (private & local — no external AI call), and IX (append-only,
recoverable) are the load-bearing ones. Feature decisions are ADR-1..ADR-5 (Principle XI).

## Summary

Add a **second per-offer match signal — "affinity"** — that scores how closely each offer resembles the
offers the user has **already applied to**, shown *beside* the existing CV **"fit"** (fit unchanged); and
let the user **read a job's full body — description & requirements — inside the app** by clicking an offer.
Do it **without losing a single byte of existing data**.

Technical approach (from [research.md](./research.md), grounded in the live 001–005 code read during Phase 0):

- **Affinity is an exact `OfferFit` twin.** A new **`OfferAffinity` satellite** (PK `OfferId` = FK →
  `offers(id)` cascade, `EnrichmentState` machine, jsonb `resembles`, `score`, `rationale`, `inputs_hash`)
  and a **fourth work-item kind (`offerAffinity`)** on the **existing kind-agnostic `/api/enrichment`
  queue**, drained by the **existing `/enrich` worker command** (extended). Claude is the sole producer —
  the backend makes **no** AI call (FR-008/SC-007); an un-produced affinity shows pending/failed, never a
  fabricated fallback (FR-009) (ADR-1). Reuses `EnrichmentService`, `IEnrichmentRepository`, the
  `inputs_hash` stale guard, the eager-invalidation hooks, the rerun path, and the backfill machinery.
- **Basis = all applied offers, weighted equally** (ADR-2, clarification #2). A pure
  `AppliedBasisInputs.Version(applied offers' fingerprint hashes)` drives invalidation: **apply / un-apply
  → all affinity pending** (mirrors "weights change → all fits pending"); a per-offer content change →
  that offer's affinity pending. **Cold start**: affinity is **eligibility-gated at ≥ 3 applied offers**
  (like fit is gated on a produced CV profile) — below that the read model returns state **`insufficient`**
  and the queue emits no affinity items (FR-006, clarification #4). Affinity is **independent of the CV
  profile** — orthogonal to fit (FR-010, ADR-5).
- **The offer body needs almost no new code** (ADR-3). `Offer.DescriptionHtml` (Minor-tier, **already
  excluded from the fingerprint**) and the `offers.description_html` column have existed since 001;
  `IJustJoinItClient.FetchDetailAsync(slug)` + `JustJoinItMapper.WithDescription(...)` already fetch and
  map the body; `OfferReadService.GetAsync` **already sanitises** it (`Ganss.Xss`) and returns it in
  `OfferDetail`. The only backend gap is that **collection never calls the detail fetch**. So: fetch the
  body **eagerly during the scan** (clarification #3) — the **`ScanOrchestrator` fetches the detail body
  for new / updated / body-missing offers only** (not every offer every scan — respects the 001 ADR-2
  source-access risk), setting the Minor-tier description; failures/blocks are tolerated (offer still
  collects; body shows "not available"). Existing offers backfill their body on their next scan sighting.
- **No data lost** (ADR-4, the headline): **ONE** append-only migration adds **only** the `offer_affinity`
  table (the body needs **no** migration — its column already exists). Affinity rows are an **invariant**
  (one per offer): created `Pending` at scan-upsert and **backfilled idempotently** by **folding affinity
  into the existing `BackfillEnrichmentAsync`**, which already runs at **startup** and — via the existing
  **`IEnrichmentBackfill`** → `RestoreService` — on an **older-backup restore**. **No new backfill port.**
  Nothing existing is dropped, overwritten, or edited (Principle IX).
- **Backup / export** (ADR-4): add **`offer_affinity`** to `BackupTables.InsertOrder` (Tier 3, after
  `offer_fit`) — the **only** backup change; the existing **`BackupTablesCompletenessTests`** guard fails
  until it is listed, permanently protecting FR-018. The offer body rides in the already-covered `offers`
  table. Export gains the **captured body** (a fact) + the **current produced affinity score** (nullable),
  satisfying SC-006; fit stays excluded (unchanged — FR-016).
- **UI**: the offer card gains an **affinity block beside the fit block** (a distinct second metric,
  FR-003) and an **"insufficient history" state**; clicking an offer opens a new **offer-detail drawer**
  (`GET /api/offers/{id}`) rendering the **sanitised body**, all facts, fit, affinity, versions/events, and
  the external link (FR-011..014). A new **"Affinity" sort** (FR-004). All reuse existing theme
  tokens/chips/buttons and the `ApplyModal` portal/focus-trap idiom.

Full design: [research.md](./research.md), [data-model.md](./data-model.md),
[contracts/affinity-and-offer-body.md](./contracts/affinity-and-offer-body.md), [quickstart.md](./quickstart.md).

## Technical Context

**Language/Version**: C# 14 on **.NET 10** (backend); **TypeScript + React 19** via Vite (frontend).
Unchanged from 001–005.

**Primary Dependencies**:
- Backend: ASP.NET Core 10 minimal APIs; EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL`; `Polly`
  (existing, for the detail fetch's retry/pace); `Ganss.Xss` (existing, already sanitises the body); the
  existing justjoin.it HTTP client. **Added: none** — no new NuGet package; **no AI SDK** (this feature
  makes no external AI call — FR-008/SC-007, enforced by the existing no-AI-package guard test).
- Frontend: React 19, Vite, TS, the hand-rolled typed `fetch` client (`api/client.ts`), the `ApplyModal`
  portal/focus-trap pattern, central design tokens + `theme/index.ts` helpers (`fitColorVar`, chips).
  **Added: none.**

**Storage**: PostgreSQL via EF Core, **append-only** migrations at startup (`MigrateAsync`). **One** new
migration `AffinityMetric` — the single `offer_affinity` table (PK `offer_id`, FK → `offers` cascade, index
on `state`, jsonb `resembles` default `'[]'`). **No** offers-table change (the `description_html` column
already exists). No new directory, no new config key. The worker/AI is the local `/enrich` command.

**Testing**: xUnit unit tests (Domain: `OfferAffinity` state machine mirrors `OfferFitStateTests`;
`AppliedBasisInputs`/`OfferAffinityInputs` hash stability + version bump; the ≥3 eligibility rule).
**Real-PostgreSQL** integration via Testcontainers (Principle V): `offer_affinity` + jsonb round-trip; the
affinity read projection (produced/pending/failed/insufficient by input-hash + applied-count gate); the
`/api/enrichment` pending/results flow for the `offerAffinity` kind incl. the stale-hash guard; **apply /
un-apply → all-affinity-pending**; the **≥3 cold-start gate**; the **body-fetch-on-scan** flow (new +
updated + body-missing → body set + sanitised on read; a fetch failure leaves the offer collected with a
null body); the **no-data-loss affinity backfill** on **upgrade AND older-restore**; backup/restore
round-trip incl. `offer_affinity`; the extended **`BackupTablesCompletenessTests`** (now +1); export
includes body + affinity score; `SchemaInvariantTests` (no-serial) over the new table. The untouched
001–005 suites are the **FR-016 regression contract** (esp. the no-AI-package guard and the fit tests).
Frontend: Vitest + RTL (`frontend/tests/offers/`) for the affinity block (produced/pending/failed/
insufficient, distinct from fit), the offer-detail drawer (renders sanitised body; unavailable state +
external link), and the affinity sort.

**Target Platform**: local-first, single-user; Windows 11 dev. Foreground ASP.NET Core on
`localhost:5180` serving the SPA. The affinity "worker" is the user's local `/enrich` Claude-Code session
(loopback), exactly as for fit/summary.

**Performance Goals**: not latency-bound — single user, ~hundreds of offers, tens of applications. Affinity
is one bounded worker item per eligible offer, recomputed on apply/un-apply. **Body fetch** adds ~1 detail
request (paced ~1 req/s) **only per new/updated/body-missing offer per scan** — not every offer — so steady
scans add little; the **first post-upgrade scan** does a one-time body backfill for existing available
offers (and, because the body enters the enrichment input hash, a one-time summary/fit re-enrich).

**Constraints**: local-first; **no external AI service and no external AI call** (0 records transmitted —
SC-007); offer/applied-offer text reaches only the **loopback** `/api/enrichment` worker (the user's own
Claude session — Principle IV, the loopback guard is the PII control); the body is **sanitised** before
render (FR-015, existing `Ganss.Xss`); **append-only** migration, **no existing data dropped/edited**
(Principle IX); async all the way; nullable on, warnings-as-errors in Domain + Application.

**Scale/Scope**: 1 user; 3 user stories; **1 new table**; **1 migration**; **0 new endpoints** (affinity
extends `/api/enrichment`; the body rides the existing `GET /api/offers/{id}`); **0 new NuGet/npm deps**; a
new offer-detail drawer + an affinity card block + a sort option; the `/enrich` command gains one kind.

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design. All gates PASS — no violations.
Feature decisions are ADR-1..ADR-5 (Principle XI).*

| # | Principle | Status | How this plan satisfies it |
|---|-----------|--------|----------------------------|
| I | Layered architecture, deps inward | ✅ PASS | New Domain lives in `Domain/Enrichment` (framework-free `OfferAffinity` aggregate + `AppliedBasisInputs`/`OfferAffinityInputs` pure hashers); the queue projection/write-back extend the Application-layer `EnrichmentService` + `IEnrichmentRepository` port; EF config/repo/migration in Infrastructure; the body-fetch is an additive method on the existing `IJobSource` port driven by the `ScanOrchestrator`. Commands return `Result`; the feed/detail are read-only. No MediatR (YAGNI). |
| II | Strongly-typed domain, no primitive obsession | ✅ PASS | Reuses the wrapped `OfferId` as the satellite key and `InputHash`/`EnrichmentState` value types; affinity score is validated `0..100`; no raw `Guid`/`int` in Domain/Application. |
| III | The Tracker Reflects Reality (NON-NEG) | ✅ PASS | Affinity is **only** ever shown as `produced` when its stored `inputs_hash` equals the recomputed-from-live-inputs hash **and** ≥3 applications exist — else `pending`/`failed`/`insufficient`; **never a fabricated/fallback score** (FR-009). The body is the source's real text, sanitised, or an honest "not available". |
| IV | Personal Data Private & Local (NON-NEG) | ✅ PASS | **No external AI call, no AI SDK** (no-AI-package guard test upheld). Offer + applied-offer text reaches only the **loopback** `/api/enrichment` worker (the user's own Max-plan Claude session) — the loopback guard is the PII control, exactly as for fit. Body fetch is *collection from the job board* (public postings, the 001 ADR-2 accepted risk), not new PII egress. 0 records transmitted externally (SC-007). |
| V | Real Database in Tests | ✅ PASS | The satellite, jsonb, the read projection + gates, the queue flow, apply/un-apply invalidation, both backfill paths, and backup/restore are tested on **real PostgreSQL** (Testcontainers). No mocked DB. |
| VI | Green Before Done (NON-NEG) | ✅ PASS | Each user story closes only on a green local suite; the untouched 001–005 suites (esp. the no-AI-package guard + fit tests) are the FR-016 regression contract. |
| VII | UI Changes Require Visual Verification | ✅ PASS | The affinity block (all four states), the offer-detail drawer (rendered sanitised body + unavailable state), and the affinity sort are run-and-looked-at (per `quickstart.md`) before "done". |
| VIII | One Source of Design Truth | ✅ PASS | Reuse `.btn*`, `.chip*`, `fitColorVar`/`enrichmentStatusClass`, the `ApplyModal` portal + focus-trap + `.modal*` classes. Any affinity chip/colour is added as a **token pair** in `tokens.css` (light+dark) + a `.chip--*` in `base.css` — never scattered literals. |
| IX | Your Data Is Recoverable | ✅ PASS | **ONE** append-only migration (table add only); **no** prior migration/column edited; the body column pre-exists. Affinity rows are an invariant, backfilled idempotently on **upgrade AND older-restore** (reusing `IEnrichmentBackfill`). `offer_affinity` joins **003 backup/restore** (guarded by the completeness test); export gains the captured body + affinity score. |
| X | Simple by Default (YAGNI) | ✅ PASS | **No new dependency, no new endpoint, no new worker command, no new backfill port, no offers migration.** Affinity is a *fourth kind* on the *existing* queue; the body reuses the *existing* fetch+sanitise+detail path. The one new table is the feature's inherent shape (a per-offer derived cache, exactly like `offer_fit`). |
| XI | Documented Decisions, Immutable History | ✅ PASS | ADR-1..ADR-5 below. Conventional Commits, one logical change each; no `--no-verify`, no history rewrite. |

**Decisions (ADR-style, per Principle XI):**

- **ADR-1 — Affinity is an `OfferFit` twin: an `OfferAffinity` satellite + a 4th `offerAffinity` kind on the
  existing `/api/enrichment` queue, drained by the existing `/enrich` worker.** *Context*: the clarified
  decision is that affinity is AI-produced by the local worker like fit, with no non-AI fallback (FR-008).
  The `EnrichmentService` is already "kind-agnostic". *Decision*: model affinity as a satellite mirroring
  `OfferFit` (state machine, `inputs_hash`, jsonb list) and add it as a fourth work-item kind on the same
  loopback queue; extend `/enrich` to produce it. *Rationale*: maximum reuse (ordering, eligibility, stale
  guard, counts, rerun, backfill, loopback PII control) for the least new surface (Principle X); keeps a
  single AI ingress. *Rejected*: a separate `/api/affinity` group + a new slash command (duplicates the
  whole machinery); extending `OfferFit` with affinity columns (conflates two independent signals, breaks
  the 1:1 fit contract, complicates invalidation).

- **ADR-2 — Basis = all applied offers weighted equally; a basis-version hash drives global invalidation;
  cold-start gate at ≥ 3 applications.** *Context*: clarifications #2 and #4. *Decision*: a pure
  `AppliedBasisInputs.Version(sorted applied {OfferId → fingerprint hash})`; `OfferAffinityInputs.Hash(
  candidate offer-enrich hash, basisVersion)`. Apply/un-apply → basis version changes → **all affinity
  pending** (a new `InvalidateAllAffinityAsync`, mirroring `InvalidateAllFitsAsync`); a candidate's content
  change → that offer's affinity pending (existing scan hook, extended). Affinity is **eligible/emitted/
  counted only when appliedCount ≥ 3**; below that the read model returns `insufficient` and the queue emits
  nothing (mirroring "fit absent when no produced profile"). An applied offer is excluded from **its own**
  basis (no trivial self-match). *Rationale*: encodes the clarified semantics with the proven fit
  invalidation pattern; the `inputs_hash` recompute-from-live-inputs guard is the correctness backstop even
  for an applied-offer content change. *Rejected*: outcome/stage weighting (clarified out — outcome changes
  do NOT invalidate affinity); dismissed-as-negative signal (out of scope); a materialised basis table
  (derive the version instead — no drift, Principle X).

- **ADR-3 — Offer body captured eagerly during a scan by the orchestrator, for new/updated/body-missing
  offers only, reusing the existing fetch+map+sanitise path.** *Context*: clarification #3; the body column,
  `FetchDetailAsync`, `WithDescription`, and read-time `Ganss.Xss` sanitisation all already exist — only
  the scan never fetches. *Decision*: add a body-fetch capability to the `IJobSource` port
  (`FetchBodyAsync`, returning null when unsupported/failed); the `ScanOrchestrator` calls it for **new**,
  **updated**, and **body-missing** offers, sets the Minor-tier description (fingerprint unaffected), and
  tolerates failures/blocks (offer still collects; body null → "not available"). Existing offers fill in on
  their next sighting. *Rationale*: "eager during scan" without re-fetching unchanged offers every scan
  (respects the 001 ADR-2 source-access risk); the body flows through the existing sanitise+`OfferDetail`
  path unchanged (FR-011/015). *Rejected*: fetching every offer's body on every scan (wasteful, higher
  block risk); lazy-on-open (clarified out); a browser-only fetch for the API source (unnecessary — the
  detail JSON is available). *Note*: because the body enters `OfferEnrichmentInputs`, the first post-upgrade
  scan triggers a one-time summary/fit re-enrich for offers that gain a body — correct (summaries improve),
  and bounded.

- **ADR-4 — No data lost: ONE table-only migration; affinity backfill folded into the existing enrichment
  backfill; +1 backup table + export additions.** *Context*: the "no data lost" requirement must hold on an
  in-place upgrade AND an older restore; 003's restore `TRUNCATE`s the full HEAD table list; the enrichment
  backfill already runs on both paths via `IEnrichmentBackfill`. *Decision*: migration adds only
  `offer_affinity`; a `Pending` row is created per offer at scan-upsert; `BackfillEnrichmentAsync` is
  extended to also create the missing `offer_affinity` rows (so **no new port/runner** — `RestoreService`
  already calls `IEnrichmentBackfill` for older backups); add `offer_affinity` to
  `BackupTables.InsertOrder` (guarded by the completeness test); add the captured body + a nullable current
  affinity score to `OfferExport`. *Rationale*: reuses the proven satellite-invariant + dual-path backfill
  exactly; minimal backup change; satisfies SC-006 without touching fit/002 export behaviour. *Rejected*: a
  separate affinity backfill port (needless — the enrichment backfill already runs where needed); a
  migration-`Up()` backfill (never runs on restored data); exporting the full derived affinity payload
  (score suffices; the rest is recoverable via backup).

- **ADR-5 — Affinity is orthogonal to fit and to the CV profile; fit/002 behaviour is unchanged.**
  *Context*: FR-010 / the request wants "another metric, not only fit". *Decision*: affinity depends only
  on application history (not the CV profile), is projected/sorted as its own `AffinityView` beside
  `FitView`, and adds no change to fit's inputs, states, ordering, or export. *Rationale*: two independent
  signals, never a blended number (FR-003); preserves 002 exactly (FR-016). *Rejected*: folding affinity
  into the fit score or the default rank formula (hides which signal is which; changes 002 behaviour).

## Project Structure

### Documentation (this feature)

```text
specs/006-application-affinity-metric/
├── plan.md              # This file
├── research.md          # Phase 0 — R1..R9 decisions, rationale, alternatives
├── data-model.md        # Phase 1 — OfferAffinity satellite, inputs, hooks, migration, backfill, backup, export, read model
├── quickstart.md        # Phase 1 — run & per-user-story validation guide
├── contracts/
│   └── affinity-and-offer-body.md  # Phase 1 — /api/enrichment offerAffinity kind + worker delta; offer-detail body contract
├── spec.md              # Feature spec (with Clarifications 2026-07-01)
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root) — delta over features 001–005

```text
backend/
├── src/
│   ├── Domain/
│   │   └── Enrichment/
│   │       ├── OfferAffinity.cs         # NEW: satellite aggregate (mirrors OfferFit): State/Attempts/Score/
│   │       │                             #      Resembles(jsonb)/Rationale/InputsHash/ProducedAt/LastError + MinApplications const
│   │       └── EnrichmentInputs.cs       # EDIT: +AppliedBasisInputs.Version(...) + OfferAffinityInputs.Hash(...)
│   ├── Application/
│   │   ├── Enrichment/
│   │   │   ├── EnrichmentService.cs      # EDIT: emit offerAffinity work items (gated ≥3, basis snapshot) + write-back
│   │   │   ├── EnrichmentContracts.cs    # EDIT: +OfferAffinityWorkItem/AffinityOfferView/AppliedOfferView; +affinity result/meta fields
│   │   │   └── IEnrichmentRepository.cs   # EDIT: +GetAffinityAsync/AddAffinityAsync; affinity in OfferWorkRow/SatelliteCounts;
│   │   │                                   #      +InvalidateAllAffinityAsync; affinity in RearmFailed/ForceAllPending
│   │   ├── Offers/
│   │   │   ├── OfferReadModels.cs        # EDIT: +AffinityView; OfferListItem +Affinity/+AffinityState; OfferListMeta +affinity counts/appliedCount
│   │   │   ├── OfferListFilter.cs        # EDIT: +OfferSort.Affinity (FR-004)
│   │   │   ├── SetOfferApplication.cs    # EDIT: on apply/clear → InvalidateAllAffinityAsync (basis changed)
│   │   │   └── OfferExport.cs            # EDIT: +Description (captured body) + AffinityScore (nullable) (SC-006)
│   │   └── Scanning/
│   │       └── ScanOrchestrator.cs       # EDIT: fetch body for new/updated/body-missing offers; create Pending offer_affinity; affinity in invalidation
│   ├── Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── Configurations/OfferAffinityConfiguration.cs # NEW (mirrors OfferFitConfiguration)
│   │   │   ├── Migrations/2026XXXX_AffinityMetric.cs        # NEW: offer_affinity table only (+designer +snapshot)
│   │   │   ├── AppDbContext.cs                              # EDIT: +DbSet<OfferAffinity> (OfferId converter already applies)
│   │   │   ├── Repositories/EnrichmentRepository.cs         # EDIT: affinity gets/adds/counts/bulk-invalidate/rearm
│   │   │   ├── Repositories/OfferReadService.cs             # EDIT: project AffinityView (hash guard + ≥3 gate); affinity sort; body already sanitised
│   │   │   ├── Repositories/ExportReader.cs                 # EDIT: project body + current produced affinity score
│   │   │   └── DatabaseInitializer.cs                       # EDIT: BackfillEnrichmentAsync also creates missing offer_affinity rows
│   │   └── Sources/JustJoinIt/
│   │       └── JustJoinItSource.cs        # EDIT: implement IJobSource.FetchBodyAsync (client.FetchDetailAsync + WithDescription)
│   ├── Application/Scanning/IJobSource.cs # EDIT: +FetchBodyAsync(CollectedOffer, ct) (null when unsupported/failed)
│   ├── Application/Backup/BackupTables.cs # EDIT: +"offer_affinity" in InsertOrder (Tier 3, after offer_fit)
│   └── Web/ (no new endpoints — affinity extends /api/enrichment; body rides GET /api/offers/{id})
└── tests/
    ├── Domain.Tests/                     # OfferAffinityStateTests; AppliedBasis/OfferAffinity hash tests; ≥3 rule
    ├── Application.Tests/                # affinity queue projection + write-back stale guard; apply/un-apply → all pending; cold-start gate
    └── Infrastructure.Tests/            # real Postgres: offer_affinity round-trip; read projection (4 states); body-fetch-on-scan;
                                          #   backfill on upgrade AND older-restore; backup/restore + BackupTablesCompletenessTests(+1);
                                          #   export body+score; SchemaInvariantTests; no-AI-package guard still green (FR-016)

frontend/
└── src/
    ├── api/
    │   ├── offers.ts                     # EDIT: +getOfferDetail(id); feed sort accepts 'affinity'
    │   └── types.ts                      # EDIT: +AffinityView + affinity/affinityState on OfferDto; OfferDetailDto (descriptionHtml already returned)
    ├── components/
    │   ├── OfferCard/OfferCard.tsx       # EDIT: +affinity block beside fit (produced/pending/failed/insufficient); open-detail on title/Details
    │   └── OfferDetail/OfferDetailDrawer.tsx # NEW: fetch GET /api/offers/{id}; render sanitised body + facts + fit + affinity + versions/events + external link
    └── pages/Offers/OffersPage.tsx       # EDIT: +Affinity sort option; wire the detail drawer
```

**Structure Decision**: Web-application layout, unchanged. The feature is **additive**: a new
`Domain/Enrichment/OfferAffinity` satellite + two pure hashers, additive edits to the Application-layer
enrichment engine/repository/read-models/export/scan, one new EF config + **one** table-only migration, an
additive `IJobSource.FetchBodyAsync`, one line in `BackupTables`, and a UI surface (an offer-detail drawer +
an affinity card block + a sort option). **No new endpoint, no new dependency, no offers migration, no new
worker command.** 001–005 behaviour is preserved; Domain stays framework-free; **no existing data is dropped
or edited**.

## Phase Status

- [x] Phase 0 — Research (`research.md`): R1–R9 resolved against the live 001–005 code (the `OfferFit`
  satellite + kind-agnostic queue + `inputs_hash` guard + eager invalidation + dual-path backfill; the
  existing `FetchDetailAsync`/`WithDescription`/`Ganss.Xss` body path). All spec clarifications fed the
  design directly; no `NEEDS CLARIFICATION` remain.
- [x] Phase 1 — Design & Contracts (`data-model.md`, `contracts/affinity-and-offer-body.md`,
  `quickstart.md`); agent context (CLAUDE.md active-feature pointer) updated to this feature.
- [ ] Phase 2 — Tasks (`/speckit-tasks`) — **not** produced by this command.

## Complexity Tracking

*No Constitution violations. Two items are worth recording for transparency (Principle X):*

| Item | Why needed | Simpler alternative rejected because |
|------|-----------|--------------------------------------|
| **1 new table** (`offer_affinity`) | Affinity is a per-offer, worker-produced, input-hash-versioned derived cache with pending/produced/failed states — structurally identical to `offer_fit`, which is already its own table for exactly these reasons (write without loading the offer aggregate; independent invalidation). | Adding affinity columns to `offer_fit` conflates two independent signals with different inputs and invalidation triggers, breaks fit's 1:1 contract and its stale-hash guard, and would change 002 behaviour (FR-016). |
| `IJobSource` gains `FetchBodyAsync` | The body must be captured during a scan for new/updated/body-missing offers only (efficient, block-risk-aware); the orchestrator (which knows new/updated) must drive it, so the capability belongs on the source port. | Fetching every offer's body on every scan (in the adapter, blind to persistence state) doubles source requests each scan and raises the anti-bot block risk (001 ADR-2) for no benefit on unchanged offers. |
