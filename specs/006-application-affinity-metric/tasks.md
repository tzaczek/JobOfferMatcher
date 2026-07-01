---
description: "Task list for Application Affinity Metric & Offer Detail Body (006)"
---

# Tasks: Application Affinity Metric & Offer Detail Body

**Input**: Design documents from `specs/006-application-affinity-metric/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/affinity-and-offer-body.md, quickstart.md

**Tests**: INCLUDED — the constitution mandates real-DB integration tests (Principle V) and green-before-done
(Principle VI); the plan ships a test inventory. Test tasks are first-class here.

**Organization**: grouped by user story (US1–US3) so each is an independently testable increment. **US1
(affinity beside fit) is the MVP**; **US2 (offer body) is independent of the affinity infra** and can be
built in parallel with Foundational; US3 layers cold-start/rationale/freshness onto US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US3 (Setup/Foundational/Polish carry no story label)
- Paths are repo-relative; backend = `backend/src` + `backend/tests`, frontend = `frontend/src` + `frontend/tests`.

**Load-bearing constraints (from plan.md):** Affinity is an **`OfferFit` twin** — a new `OfferAffinity`
satellite + a **4th `offerAffinity` kind on the existing `/api/enrichment` queue** drained by the existing
`/enrich` command; **produced only by the local worker, no external AI call, no non-AI fallback**. **ONE**
append-only migration adds **only** `offer_affinity` (the body needs **none** — `offers.description_html`
exists since 001). **No existing table/column/migration is edited.** **No data lost** = affinity rows are an
invariant created at scan-upsert + backfilled by extending the existing `BackfillEnrichmentAsync` (startup
AND older-restore via `IEnrichmentBackfill`) + `offer_affinity` added to `BackupTables.InsertOrder`. Fit and
all 001–005 behaviour are **unchanged** (FR-016).

---

## Phase 1: Setup (Shared)

**Purpose**: prerequisites that aren't code behaviour.

- [X] T001 [P] Confirm **no new dependency** is needed (no NuGet, no npm, **no AI SDK**): affinity reuses the
  existing `/api/enrichment` queue + `/enrich` command; the offer body reuses the existing
  `IJustJoinItClient.FetchDetailAsync` + `JustJoinItMapper.WithDescription` + read-time `Ganss.Xss`
  sanitisation. Note it in `specs/006-application-affinity-metric/quickstart.md` (already documented).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the affinity satellite core (Domain + the single migration + repo port/impl + scan invariant +
backfill + backup inclusion) and the shared read-model/DTO additions that US1 and US3 need. **US2 (offer
body) does not depend on this phase** (see Dependencies) and may proceed after Setup in parallel.

**⚠️ CRITICAL**: US1 and US3 cannot begin until this phase is complete. Because the table ships in **one**
migration, the Domain entity + EF config below must exist before T007 generates it.

### Domain (framework-free)

- [X] T002 [P] Create the `OfferAffinity` aggregate — an `OfferFit` twin (key `OfferId`; `State`
  (`EnrichmentState`), `Attempts`, `Score` int?, `Resembles` jsonb string list, `Rationale`, `InputsHash`,
  `ProducedAt`, `LastError`; methods `CreatePending`/`MarkProduced(score,resembles,rationale,hash,at)`/
  `RecordFailure`/`Invalidate`/`Rearm`/`ForcePending`; `const int MinApplications = 3`; private ctor for EF)
  in `backend/src/Domain/Enrichment/OfferAffinity.cs`.
- [X] T003 [P] Add `AppliedBasisInputs.Version(IReadOnlyList<(OfferId, string fingerprintHash)>)` (null when
  `< OfferAffinity.MinApplications`) and `OfferAffinityInputs.Hash(offerEnrichmentHash, appliedBasisVersion)`
  to `backend/src/Domain/Enrichment/EnrichmentInputs.cs` (same `EnrichmentHashing` sorted-key style).
- [X] T004 [P] Add `AffinityRationaleMaxWords = 30` (soft cap, worker guidance) to
  `backend/src/Domain/Settings/EnrichmentSettings.cs` — additive record field, **no migration** (the
  `enrichment` jsonb default absorbs it; existing rows deserialize the default).

### Persistence (EF Core + the single migration)

- [X] T005 Create `OfferAffinityConfiguration` (table `offer_affinity`, PK `offer_id` `ValueGeneratedNever`
  FK→`offers(id)` **cascade**, `state varchar(20)` `HasConversion<string>`, `resembles` jsonb via
  `HasJsonbListConversion<string>()`, index on `state`) mirroring `OfferFitConfiguration` in
  `backend/src/Infrastructure/Persistence/Configurations/OfferAffinityConfiguration.cs` (depends T002).
- [X] T006 Add `DbSet<OfferAffinity>` to `backend/src/Infrastructure/Persistence/AppDbContext.cs` (the
  `OfferId` converter already applies — no `ConfigureConventions` line; config auto-discovered) (depends T002).
- [X] T007 Generate the append-only migration `AffinityMetric` (`dotnet ef migrations add AffinityMetric`) —
  **only** the `offer_affinity` table (PK `offer_id`, FK→`offers` cascade, index on `state`, `resembles`
  jsonb default `'[]'`, no offers-table change) — committing `*_AffinityMetric.cs` + designer + updated
  `AppDbContextModelSnapshot.cs` under `backend/src/Infrastructure/Persistence/Migrations/` (depends T005, T006).

### Repository port + adapter

- [X] T008 [P] Extend `IEnrichmentRepository`: `GetAffinityAsync`/`AddAffinityAsync`; add `Affinity` to
  `OfferWorkRow`; add `PendingAffinity`/`FailedAffinity` to `SatelliteCounts` + a `countAffinity` param on
  `GetCountsAsync`; `InvalidateAllAffinityAsync`; include affinity in `RearmFailedAsync`/`ForceAllPendingAsync`,
  in `backend/src/Application/Enrichment/IEnrichmentRepository.cs` (depends T002).
- [X] T009 Implement the affinity methods in
  `backend/src/Infrastructure/Persistence/Repositories/EnrichmentRepository.cs` (gets/adds; affinity in the
  work-row join; gated counts; bulk `UPDATE offer_affinity SET state='Pending', attempts=0`; affinity in
  rearm/force) (depends T008, T006).

### Scan invariant + backfill (no data lost)

- [X] T010 Extend `BackfillEnrichmentAsync` to also insert a `Pending` `offer_affinity` row for every offer
  lacking one (idempotent — only fills gaps) in
  `backend/src/Infrastructure/Persistence/DatabaseInitializer.cs`. This runs at **startup** and, via the
  existing `IEnrichmentBackfill`→`RestoreService`, on an **older-backup restore** — **no new port** (depends
  T002, T006). **[no-data-loss — SC-005/SC-006]**
- [X] T011 In `backend/src/Application/Scanning/ScanOrchestrator.cs`, create a `Pending` `offer_affinity` row
  at offer-create (beside `OfferEnrichment`/`OfferFit` in `UpsertAsync`) and include affinity in
  `InvalidateSatellitesAsync` (a candidate content change re-arms its affinity) (depends T002, T008).
  *(Same file as T032 — sequence T011 before the US2 body-fetch edit.)*

### Backup inclusion

- [X] T012 Add `"offer_affinity"` to `BackupTables.InsertOrder` (Tier 3, **after** `offer_fit`) in
  `backend/src/Application/Backup/BackupTables.cs`.
- [X] T013 Confirm the existing **`BackupTablesCompletenessTests`** (model tables == `InsertOrder`) is green
  with `offer_affinity` present (it fails until T012 lands — the guard doing its job) in
  `backend/tests/Infrastructure.Tests/Backup/BackupTablesCompletenessTests.cs` (depends T006, T012).

### Shared read-model / DTO scaffolding (US1 + US3)

- [X] T014 [P] Add `AffinityView(string State, int? Score, IReadOnlyList<string> Resembles, string? Rationale)`;
  add `Affinity` (`AffinityView?`) + `AffinityState` (`string`) to `OfferListItem`; add `PendingAffinity`,
  `FailedAffinity`, `AppliedCount`, `HasAffinityBasis` to `OfferListMeta`, in
  `backend/src/Application/Offers/OfferReadModels.cs`.
- [X] T015 [P] Add `Affinity` to the `OfferSort` enum in `backend/src/Application/Offers/OfferListFilter.cs`
  (FR-004).
- [X] T016 [P] Add the `AffinityView` type + `affinity`/`affinityState` on `OfferDto` + the affinity `meta`
  fields; ensure an `OfferDetailDto` type exists (`descriptionHtml` is already returned by the backend), in
  `frontend/src/api/types.ts` (mirror the `FitView`/`FitDto` style).

**Checkpoint**: foundation ready — `offer_affinity` migrated, an invariant `Pending` row per offer (scan +
backfill), repo affinity methods, backup covers the table, read-model/DTO shape in place. US1 and US3 can begin.

---

## Phase 3: User Story 1 — A second match signal (affinity) beside fit (Priority: P1) 🎯 MVP

**Goal**: each offer shows an **affinity** score (how closely it resembles the offers the user applied to)
*beside* the unchanged CV **fit**, produced by the local `/enrich` worker; the user can sort by affinity.

**Independent Test**: mark ≥ 3 offers of a recognisable kind applied → run `/enrich` → similar offers score
higher on affinity than unrelated ones, fit is still shown separately and unchanged, and `sort=affinity`
orders the feed. `GET /api/offers` carries a distinct `affinity` block (never blended with `fit`).

### Implementation — backend

- [X] T017 [P] [US1] Domain tests: `OfferAffinity` state machine (mirror `OfferFitStateTests` —
  CreatePending/MarkProduced/RecordFailure→terminal at limit/Invalidate/Rearm) in
  `backend/tests/Domain.Tests/OfferAffinityStateTests.cs` (depends T002).
- [X] T018 [P] [US1] Domain tests: `AppliedBasisInputs.Version` (null below 3; stable order; changes on
  set/fingerprint change) + `OfferAffinityInputs.Hash` stability + version bump, in
  `backend/tests/Domain.Tests/AffinityInputsTests.cs` (depends T003).
- [X] T019 [US1] Extend `backend/src/Application/Enrichment/EnrichmentContracts.cs`: add `OfferAffinityWorkItem`
  (`Kind="offerAffinity"`, `WorkItemId="offer:{id}:affinity"`), `AffinityOfferView` (candidate), `AppliedOfferView`
  (basis item); add `Resembles` to `EnrichmentResultItem`, `PendingAffinity`/`FailedAffinity`/`AppliedCount`/
  `HasAffinityBasis` to `PendingMeta`+`EnrichmentStatusView`, and `AffinityRationaleWords` to `WorkGuidance`.
- [X] T020 [US1] Extend `backend/src/Application/Enrichment/EnrichmentService.cs`: compute `appliedCount` +
  `AppliedBasisInputs.Version` once; in `GetPendingWorkAsync`, **only when `appliedCount ≥ 3`**, emit an
  `OfferAffinityWorkItem` (candidate `AffinityOfferView` + the applied basis snapshot **excluding self** +
  `inputsHash`) per offer whose `Affinity.State == Pending`; add a `sub == "affinity"` write-back branch
  (recompute `OfferAffinityInputs.Hash` from live inputs → reject mismatch/`appliedCount<3` as `stale`; on
  `produced` with `score ∈ [0,100]` → `MarkProduced`, else `RecordFailure`); include affinity in the meta
  counts, `GetStatusAsync`, and `TriggerRerunAsync` (depends T019, T009, T008).
- [X] T021 [US1] In `backend/src/Application/Offers/SetOfferApplication.cs`, call
  `IEnrichmentRepository.InvalidateAllAffinityAsync` on `MarkAppliedAsync` **and** `ClearAsync` (the applied
  set changed → basis version changed → all affinity pending) (depends T008).
- [X] T022 [US1] Extend `backend/src/Infrastructure/Persistence/Repositories/OfferReadService.cs`: add
  `ProjectAffinity` (recompute `OfferAffinityInputs.Hash`; `produced`/`failed` only when hash matches;
  `insufficient` when `appliedCount < 3`; else `pending`); load `offer_affinity` + `appliedCount` +
  `basisVersion` into `ReadContext`; set `Affinity`/`AffinityState` in `ToListItem`; populate the affinity
  `OfferListMeta` fields; add the `OfferSort.Affinity` branch to `Sort` (produced-affinity score desc, then
  rank). Fit projection/ordering **unchanged** (depends T014, T015, T009).
- [X] T023 [US1] Extend the existing **`/enrich`** worker command in `.claude/commands/enrich.md` to handle
  the `offerAffinity` kind: compare `item.offer` (candidate) to `item.appliedBasis` and produce a `0..100`
  score + `resembles` + a ≤ `guidance.rationaleWords` rationale, echoing `inputsHash`/`kind`; un-producible →
  `status:"failed"` (per `contracts/affinity-and-offer-body.md §3`) (depends T019, T020).

### Tests — US1

- [X] T024 [P] [US1] Application + Infra tests (real Postgres): affinity work-item emitted only when
  `appliedCount ≥ 3`; write-back **stale-hash guard** rejects a superseded item; produced/pending/failed
  projection via the read model, in `backend/tests/Application.Tests/OfferAffinityTests.cs` +
  `backend/tests/Infrastructure.Tests/OfferAffinityFlowTests.cs` (depends T020, T022).
- [X] T025 [P] [US1] Infra test: **apply / un-apply → all affinity pending** (`InvalidateAllAffinityAsync`
  fires; a produced affinity re-arms), in `backend/tests/Infrastructure.Tests/OfferAffinityFlowTests.cs`
  (depends T021).

### Implementation — frontend

- [X] T026 [US1] Edit `frontend/src/components/OfferCard/OfferCard.tsx`: add an **affinity block beside the
  fit block** — a distinct 0–100 number (reuse `fitColorVar`) + `resembles` chips + rationale for `produced`;
  `enrichmentStatusClass` for `pending`/`failed`; never blended with fit (FR-003) (depends T016).
- [X] T027 [US1] Add an **"Affinity"** sort option to the feed controls (passes `sort=affinity`) in
  `frontend/src/pages/Offers/OffersPage.tsx` (depends T016).
- [X] T028 [P] [US1] Frontend tests (Vitest + RTL): the affinity block renders produced/pending/failed as a
  distinct signal from fit; the affinity sort issues `sort=affinity`, in
  `frontend/tests/offers/OfferCardAffinity.test.tsx` (depends T026, T027).

**Checkpoint**: US1 fully functional — a second, distinct, worker-produced affinity signal beside fit, sortable (MVP).

---

## Phase 4: User Story 2 — Read the full offer body inside the app (Priority: P1)

**Goal**: capture each offer's body during a scan and let the user read the full description & requirements
in an in-app offer-detail view (no forced trip to the external site). *Independent of the affinity infra —
only needs Setup + the existing offer read/detail path.*

**Independent Test**: run a scan → open an offer → its full description/requirements render (server-sanitised)
with facts, fit, affinity, versions/events, and the external link; an offer with no captured body shows
"description not available" + the external link; a per-offer fetch failure never fails the scan.

### Implementation — backend

- [X] T029 [US2] Add `Task<string?> FetchBodyAsync(CollectedOffer offer, CancellationToken ct)` to the
  `IJobSource` port (returns `null` when unsupported/failed) in
  `backend/src/Application/Scanning/IJobSource.cs`.
- [X] T030 [US2] Implement `FetchBodyAsync` in
  `backend/src/Infrastructure/Sources/JustJoinIt/JustJoinItSource.cs` (via `client.FetchDetailAsync(slug)` +
  `JustJoinItMapper.WithDescription` → the `body`); return `null` in
  `backend/src/Infrastructure/Sources/Browser/NotConfiguredInteractiveBrowserSource.cs` (depends T029).
- [X] T031 [US2] Add `Offer.SetDescription(string?)` — a **Minor-tier** mutator that sets `DescriptionHtml`
  only (no fingerprint / version / event / `HasUnseenUpdate` change), in
  `backend/src/Domain/Offers/Offer.cs`.
- [X] T032 [US2] In `backend/src/Application/Scanning/ScanOrchestrator.cs`, fetch + set the body for **new /
  updated / body-missing** offers only (`await adapter.FetchBodyAsync(...)` → `offer.SetDescription(body)`
  when non-null), **before** the existing `EnrichmentHashOf` change-check so the body flows into that offer's
  own summary/fit/affinity invalidation (FR-016: this is the existing description-in-input rule, so a
  **one-time recomputation** of offers that gain a body is expected on the first post-feature scan — a body
  capture never re-pends *another* offer's metrics, as the affinity basis uses Major-tier fingerprints);
  tolerate failures/blocks (null body → offer still collects) (depends T029, T030, T031; **same file as
  T011 — sequence after T011**).

*(No backend change to the detail read: `OfferReadService.GetAsync` already sanitises `DescriptionHtml` via
`Ganss.Xss` and returns it in `OfferDetail`.)*

### Tests — US2

- [X] T033 [P] [US2] Domain test: `SetDescription` is Minor-tier — `CurrentFingerprint`, `FingerprintVersion`,
  and `HasUnseenUpdate` are unchanged after it, in `backend/tests/Domain.Tests/OfferDescriptionTests.cs`
  (depends T031).
- [X] T034 [P] [US2] Infra test (real Postgres): a scan sets the body for new/updated/body-missing offers; a
  `FetchBodyAsync` failure leaves the offer collected with a null body (scan not failed); `GET
  /api/offers/{id}` returns the **sanitised** body (no `<script>`); a null body surfaces the unavailable
  path; and an offer that **gains a body** has its own summary/fit/affinity re-pended (FR-016 recompute —
  the description is part of that offer's AI input), while **another** offer's affinity is untouched, in
  `backend/tests/Infrastructure.Tests/Sources/OfferBodyFlowTests.cs` (depends T032).

### Implementation — frontend

- [X] T035 [US2] Create the **offer-detail drawer** (`GET /api/offers/{id}` → render the **server-sanitised**
  `descriptionHtml` via `dangerouslySetInnerHTML`; show facts + fit + affinity + versions/events + the
  external `canonicalUrl` link; a clear "description not available" state when `descriptionHtml` is null) —
  reuse the `ApplyModal` portal + focus-trap + `.modal*` classes — in
  `frontend/src/components/OfferDetail/OfferDetailDrawer.tsx` (+ `.css`) (depends T016).
- [X] T036 [US2] Wire opening the drawer from the offer card (click the title / a "Details" button) and add
  `getOfferDetail(id)` to `frontend/src/api/offers.ts`, editing
  `frontend/src/components/OfferCard/OfferCard.tsx` + `frontend/src/pages/Offers/OffersPage.tsx` (depends
  T035; **same FE files as T026/T027 — sequence after them**).
- [X] T037 [P] [US2] Frontend tests: the drawer renders the sanitised body; the unavailable state + external
  link show when the body is null, in `frontend/tests/offers/OfferDetailDrawer.test.tsx` (depends T035, T036).

**Checkpoint**: US1 + US2 work — affinity beside fit, and the full offer body readable in-app.

---

## Phase 5: User Story 3 — Affinity that explains itself and stays current (Priority: P2)

**Goal**: affinity is interpretable and trustworthy — a clear **"insufficient history"** state below 3
applications, a rationale/`resembles` when produced, and automatic refresh as the applied set changes (the
invalidation + rationale already land in US1; this story adds the cold-start UX and the freshness/failure proof).

**Independent Test**: with < 3 applied offers, affinity shows "not enough application history yet" (not a
number); after applying to reach ≥ 3 and running `/enrich`, scores + rationale appear; un-applying re-pends
and re-drains without duplicates; a forced-bad affinity retries to terminal `failed`.

### Implementation — frontend

- [X] T038 [US3] In `frontend/src/components/OfferCard/OfferCard.tsx`, render the affinity **`insufficient`**
  state distinctly ("not enough application history yet" — not `pending`) and polish the produced rationale +
  `resembles` display (depends T026; **same file — sequence after T026/T036**).
- [X] T039 [US3] In `frontend/src/pages/Offers/OffersPage.tsx`, surface `meta.hasAffinityBasis`/`appliedCount`
  — e.g. a one-line hint "Apply to at least 3 offers to unlock affinity" when the basis is insufficient
  (depends T027; **same file — sequence after T027/T036**).

### Tests — US3

- [X] T040 [P] [US3] Tests: read projection returns `insufficient` when `appliedCount < 3` and `produced`
  when `≥ 3` with a current hash; recompute-on-apply/un-apply refreshes with **no duplicate/stale** values,
  in `backend/tests/Infrastructure.Tests/OfferAffinityFlowTests.cs` (extend); a cold-start render test in
  `frontend/tests/offers/AffinityColdStart.test.tsx` (depends T022, T021, T038).
- [X] T041 [P] [US3] Test: an affinity `failed` result retries to terminal at `retryLimit` and `/rerun`
  (`scope=failed`/`all`) re-arms affinity rows, in `backend/tests/Application.Tests/OfferAffinityTests.cs`
  (extend) (depends T020).

**Checkpoint**: US1–US3 work — an interpretable, self-refreshing affinity signal with an honest cold start.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T042 [P] **Export (FR-018 / SC-006)**: add `Description` (the captured body — a fact) + `AffinityScore`
  (nullable current *produced* score, guarded by the input-hash + `appliedCount ≥ 3` check) to
  `backend/src/Application/Export/OfferExport.cs`; project both in
  `backend/src/Infrastructure/Persistence/Repositories/ExportReader.cs`; add the CSV columns in
  `backend/src/Application/Export/ExportService.cs`; extend `backend/tests/Application.Tests/ExportServiceTests.cs`.
  Export carries the **body + affinity score only** — the affinity `rationale`/`resembles` are recomputable
  derived detail, **not** exported (recoverable via backup/restore, FR-018); fit stays excluded (unchanged —
  FR-016) (depends T022).
- [X] T043 [P] **Backup/restore round-trip** including `offer_affinity` (backup → wipe → restore → rows
  intact) **and** the **older-restore affinity backfill** (a pre-006 backup restore synthesises the affinity
  rows via `IEnrichmentBackfill`) in
  `backend/tests/Infrastructure.Tests/Backup/BackupRestoreAffinityTests.cs` (depends T010, T012).
- [X] T044 [P] **No-data-loss upgrade** test: starting against a pre-006 database, startup
  `BackfillEnrichmentAsync` creates a `Pending` `offer_affinity` row per offer and re-running changes nothing
  (idempotent), in `backend/tests/Infrastructure.Tests/OfferAffinityFlowTests.cs` (extend) (depends T010).
- [X] T045 [P] **No-external-call / FR-016 regression gate**: the existing **no-AI-package guard** stays
  green (affinity adds no external/AI call); the full **001–005** backend suites (esp. all fit tests) and the
  frontend suite run green — additive design means no existing test should need editing; if one does, that is
  a regression to investigate (depends all prior).
- [X] T046 Run `specs/006-application-affinity-metric/quickstart.md` end-to-end and **visually verify**
  (Principle VII): the affinity block in all four states (produced/pending/failed/insufficient) distinct from
  fit, the affinity sort, the offer-detail drawer (sanitised body + the "not available" state + external
  link), the cold-start hint; plus the **no-data-loss upgrade**, the **backup/restore** round-trip, and the
  **older (pre-006) restore** affinity backfill (final; runs after T045).

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)** → no deps.
- **Foundational (P2)** → after Setup; **BLOCKS US1 + US3** (US2 needs only Setup). Order inside: Domain
  (T002, T003, T004 ∥) → Persistence (T005; T006; **T007 migration** after T005, T006) → repo (T008 → T009) →
  invariant/backfill (T010; T011) → backup (T012 → T013) → read-model/DTO (T014, T015, T016 ∥).
- **US1 (P3)** → after Foundational. **MVP** (affinity beside fit + sort + worker).
- **US2 (P4)** → after **Setup only** (independent of the affinity infra), except its two shared files:
  `ScanOrchestrator.cs` (T032 after T011) and the FE offer card/page (T036 after T026/T027). Otherwise can
  proceed in parallel with Foundational/US1.
- **US3 (P5)** → after US1 (builds on the affinity block + projection + invalidation).
- **Polish (P6)** → after the stories it touches; T046 last.

### Story dependencies

- **US1** — needs Foundational. Delivers the second signal + sort + the `/enrich` affinity kind (MVP).
- **US2** — needs Setup + the existing offer read/detail; the body column, fetch, and sanitisation already exist.
- **US3** — needs US1 (cold-start UX + freshness/failure proofs over the US1 machinery).

### Same-file serialization (NOT parallel)

- `ScanOrchestrator.cs`: T011 (affinity-create, Foundational) → T032 (body-fetch, US2).
- `OfferReadService.cs`: T022 (US1) — single (its `GetAsync` already returns the sanitised body).
- `OfferCard.tsx`: T026 (US1) → T036 (US2 open-detail) → T038 (US3 insufficient state).
- `OffersPage.tsx`: T027 (US1) → T036 (US2) → T039 (US3 cold-start hint).
- `OfferAffinityFlowTests.cs`: T024 (US1) → T025 (US1) → T040 (US3) → T044 (Polish).
- `OfferAffinityTests.cs` (Application): T024 (US1) → T041 (US3).
- `EnrichmentContracts.cs` T019 → `EnrichmentService.cs` T020 (US1) — EnrichmentService uses the new contracts.

### Parallel opportunities

- **Foundational**: T002, T003, T004 ∥; T014, T015, T016 ∥ (after the read-model shape is decided); T008
  after T002.
- **US1**: T017, T018 (domain tests) ∥; T024, T025 (backend tests) ∥ once targets exist; T026/T027 (FE)
  proceed while backend tests run; T028 after the FE pieces.
- **US2** can run in parallel with US1 (different files, save the two shared ones noted above).
- **Polish**: T042, T043, T044, T045 ∥ (T046 — the final manual quickstart + visual verification — last).

---

## Parallel Example: Foundational Domain

```bash
# Different files, no interdependency:
Task: "Create OfferAffinity aggregate in backend/src/Domain/Enrichment/OfferAffinity.cs"          # T002
Task: "Add affinity hashers to backend/src/Domain/Enrichment/EnrichmentInputs.cs"                 # T003
Task: "Add AffinityRationaleMaxWords to backend/src/Domain/Settings/EnrichmentSettings.cs"        # T004
```

## Parallel Example: User Story 1 backend tests

```bash
Task: "OfferAffinity state-machine tests in backend/tests/Domain.Tests/OfferAffinityStateTests.cs"        # T017
Task: "Affinity input-hash tests in backend/tests/Domain.Tests/AffinityInputsTests.cs"                    # T018
Task: "Affinity queue + projection tests in backend/tests/Infrastructure.Tests/OfferAffinityFlowTests.cs" # T024
```

---

## Implementation Strategy

### MVP first (US1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational (one migration, repo, backfill, backup) → 3. Phase 3 US1 →
   **STOP & VALIDATE**: mark ≥ 3 offers applied, run `/enrich`, confirm a distinct affinity number beside fit,
   sort by affinity, and that fit is unchanged → demo.

### Incremental delivery

- Foundational ready → **US1** (affinity beside fit + sort, MVP) → **US2** (offer body + detail drawer — can
  overlap US1) → **US3** (cold-start UX + freshness). Each adds value without breaking the prior; run the
  relevant quickstart checks at each checkpoint.

### Cross-feature caution

- **No data lost is a headline**: T007 (migration) + T010 (backfill on startup **and** older-restore) + T012
  (`BackupTables`) must all land for the guarantee; T043 + T044 are the proofs; T013 (completeness guard) must
  land with T007/T012 so a backup taken after the migration includes `offer_affinity`.
- **FR-016 (nothing existing changes)**: fit, 002 enrichment, and the no-AI-package guard must stay green
  (T045). Affinity is a *new* kind/table/signal — never fold it into fit or the default rank.

---

## Notes

- [P] = different files, no incomplete-task dependency. [Story] maps a task to US1–US3 for traceability.
- Tests are required (Principle V/VI): Domain/Application unit tests + **real-Postgres** integration
  (Testcontainers); the DB is never mocked; affinity/offer text never leaves the loopback `/enrich` worker.
- **Additive only**: one new table, no existing table/column/migration edited; the body column pre-exists;
  affinity reuses the `/api/enrichment` queue + `/enrich` command; fit/001–005 behaviour unchanged (FR-016).
- Reuse, don't reinvent: `OfferFit`(+`OfferFitConfiguration`/`OfferFitStateTests`) as the satellite template;
  the `inputs_hash` recompute-from-live-inputs guard; the kind-agnostic `EnrichmentService`; the eager
  invalidation hooks; `BackfillEnrichmentAsync`/`IEnrichmentBackfill`; `BackupTables` + its completeness
  guard; the existing `FetchDetailAsync`/`WithDescription`/`Ganss.Xss` body path; the `ApplyModal`
  portal/focus-trap + `.modal*` classes; theme chips/`fitColorVar`/`enrichmentStatusClass`.
- Keep `Domain` framework-free; async all the way; **never add an external/AI call** (Principle IV, SC-007).
- Commit per task or logical group; stop at any checkpoint to validate a story; run `/speckit-analyze` before
  `/speckit-implement`.
