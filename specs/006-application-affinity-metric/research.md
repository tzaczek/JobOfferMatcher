# Phase 0 Research: Application Affinity Metric & Offer Detail Body

**Feature**: `006-application-affinity-metric` | **Date**: 2026-07-01 | **Plan**: [plan.md](./plan.md)

Grounded in a direct read of the live 001–005 code. Each item: **Decision → Rationale → Alternatives**.
The four spec clarifications (Session 2026-07-01) are inputs, not open questions; no `NEEDS CLARIFICATION`
remain.

---

## R1 — Where does affinity live? (satellite vs extend fit vs new subsystem)

**Decision**: A new **`OfferAffinity`** satellite table/aggregate, a structural twin of `OfferFit`
(`Domain/Enrichment/OfferFit.cs`): PK `OfferId` = FK → `offers(id)` cascade, `EnrichmentState`
machine, `Score` (`0..100`), `Resembles` (jsonb string list — the fit `Matched` analogue), `Rationale`,
`InputsHash`, `ProducedAt`, `Attempts`, `LastError`. Same methods (`CreatePending`, `MarkProduced`,
`RecordFailure`, `Invalidate`, `Rearm`, `ForcePending`).

**Rationale**: Affinity is a per-offer, worker-produced, input-hash-versioned derived cache with
pending/produced/failed — *exactly* what `offer_fit` already is. Cloning the proven shape gives the
`inputs_hash` stale guard, the standalone write (worker writes without loading the `Offer` aggregate),
independent invalidation, and index-on-`state` for free.

**Alternatives**: Add affinity columns to `offer_fit` — conflates two independent signals (different
inputs, different invalidation), breaks fit's 1:1 contract and its single `inputs_hash`, and would alter
002 behaviour (FR-016). A bespoke non-satellite store — throws away all the reuse.

---

## R2 — Who computes affinity? (local worker vs backend deterministic)

**Decision**: The **local Claude-Code `/enrich` worker**, as a **fourth work-item kind (`offerAffinity`)**
on the **existing kind-agnostic `/api/enrichment` queue** (`EnrichmentService` is documented as
"kind-agnostic"; `EnrichmentEndpoints` is loopback-only). The backend makes **no** AI call; there is **no**
non-AI fallback score.

**Rationale**: Clarification #1. Reuses the entire machinery — pending ordering, eligibility gating,
recompute-from-live-inputs stale guard, counts, `/rerun`, the loopback PII control, and the no-AI-package
guard test — for one added kind, and keeps a single AI ingress (Principle X). Consistent with fit's
`FR-005` "no non-AI fallback".

**Alternatives**: A separate `/api/affinity` group + a new slash command (duplicates the whole subsystem).
A deterministic backend similarity (skill/title overlap) — clarified out, and it reintroduces the non-AI
scoring that 002 deliberately removed (FuzzySharp/`Scorer.Score` deleted in 002 data-model §10).

---

## R3 — What are affinity's inputs, and when does it recompute?

**Decision**: Two pure hashers in `Domain/Enrichment/EnrichmentInputs.cs`, in the existing
`EnrichmentHashing` style (SHA-256 over canonical sorted-key JSON, versioned):

- `AppliedBasisInputs.Version(IReadOnlyList<(OfferId, string fingerprintHash)> applied)` → an `InputHash`
  over the **sorted set of applied offers and their Major-tier fingerprint hashes**. `null` when
  `appliedCount < 3` (insufficient basis).
- `OfferAffinityInputs.Hash(InputHash offerEnrichmentHash, InputHash appliedBasisVersion)` → the candidate
  offer's own enrichment hash (offer content **incl. description** — reuses `OfferEnrichmentInputs`)
  composed with the basis version. Mirrors `OfferFitInputs.Hash(offerEnrichHash, effectiveProfileVersion,
  weights)`.

Propagation (identical discipline to fit; §7 of 002 data-model):
- **Apply / un-apply an offer** → the applied set changes → `AppliedBasisInputs.Version` changes → every
  `OfferAffinityInputs.Hash` differs → **all affinity pending** (an eager bulk `InvalidateAllAffinityAsync`,
  mirroring `InvalidateAllFitsAsync` on a weights/CV change).
- **A candidate offer's content changes** (scan `ApplyUpdate` / Minor `RefreshMinorContent`) → that offer's
  affinity pending (the existing per-offer scan invalidation hook, extended to affinity).
- **An applied offer's content changes** → its fingerprint hash changes → the basis version changes → the
  `inputs_hash` recompute guard renders all produced affinities as `pending` on read (correctness backstop);
  the eager bulk-invalidate on the same scan tick keeps the `state` column and counts accurate.
- **An application outcome/stage changes (005)** → **no** affinity change (basis is outcome-agnostic —
  clarification #2).

**Rationale**: The recompute-from-live-inputs `inputs_hash` guard is the correctness invariant (a superseded
value renders `pending`, never stale); the eager hooks make the queue/counts cheap. This is the exact model
that already keeps fit correct.

**Alternatives**: A materialised basis table (drift risk; the version is cheap to derive — Principle X).
Recomputing on outcome changes (contradicts clarification #2).

---

## R4 — Cold start: what below "enough" applications?

**Decision**: Affinity is **eligibility-gated at `appliedCount ≥ 3`** (a `Domain` constant
`OfferAffinity.MinApplications = 3`, clarification #4). Below 3: the queue emits **no** affinity items and
the read model returns a distinct state **`insufficient`** (not `pending`), exactly as fit is **absent**
when there is no produced CV profile (`ProjectFit` returns `null` in `OfferReadService`). Crossing the
threshold (2↔3) flips eligibility naturally via the same gate.

**Rationale**: Matches the clarified threshold and reuses fit's proven "eligibility-gated counts so nothing
is stuck pending" pattern (`SatelliteCounts`, `GetCountsAsync(countFits)` → `countAffinity`). A dedicated
`insufficient` state (vs `null`/`pending`) lets the UI say "not enough application history yet" honestly
(FR-006, SC-003).

**Alternatives**: A configurable threshold (spec doesn't ask; a constant is simpler). Showing `pending`
below 3 (misleading — implies a score is coming).

---

## R5 — Affinity work-item payload: how does the worker get the basis?

**Decision**: The `OfferAffinityWorkItem` carries the candidate `AffinityOfferView` (title, required/nice
skills, seniority, work mode, employment, normalised monthly salary — the `FitOfferView` shape) **plus** a
compact `AppliedOfferView[]` basis snapshot (the same attribute shape for each applied offer, **excluding
the candidate itself**), plus `guidance.rationaleWords` and the echoed `inputsHash`. The worker returns
`{ score 0..100, resembles: string[], rationale }`.

**Rationale**: Self-contained items match the existing contract (a fit item already embeds the whole
profile). At single-user scale (tens of applications) the per-item basis is small. Excluding self avoids a
trivial 100-score self-match.

**Alternatives**: Send the basis once in `meta` and have the worker apply it to all items — breaks the
"items are processed independently / re-runnable" contract. A backend-computed aggregate "target profile"
(frequency vectors) — that edges toward backend scoring; keep the backend to data-shaping only and let
Claude judge similarity.

---

## R6 — Offer body: what already exists, and the one gap

**Decision**: Reuse everything; close the single gap in collection.
- `Offer.DescriptionHtml` + the `offers.description_html` column exist since 001 and are **Minor-tier**
  (explicitly excluded from `ContentFingerprint` — confirmed in `OfferContent`/002 data-model §3), so
  capturing it never affects new-vs-seen or the "updated" flag.
- `IJustJoinItClient.FetchDetailAsync(slug)` and `JustJoinItMapper.WithDescription(offer, detail)` already
  fetch + map the detail `body`.
- `OfferReadService.GetAsync` **already** sanitises it (`Ganss.Xss.IHtmlSanitizer`) and returns it in
  `OfferDetail.DescriptionHtml`; `GET /api/offers/{id}` already serves `OfferDetail`.
- **The only gap**: `JustJoinItSource.CollectAsync` never fetches the detail (its own comment says "detail
  is fetched only on demand (not during scan)"), so `DescriptionHtml` is always `null` in practice.

**Rationale**: US2 is ~90 % already built; the work is to *populate* the body during a scan + add a
front-end detail view.

**Alternatives**: none — the infrastructure is present and correct.

---

## R7 — Body-fetch timing & placement (eager during scan, efficiently)

**Decision**: Fetch the body **eagerly during a scan** (clarification #3), driven by the **`ScanOrchestrator`**
for **new**, **updated**, and **body-missing** offers only — not every offer every scan. Mechanism: add
`Task<string?> FetchBodyAsync(CollectedOffer, CancellationToken)` to the `IJobSource` port (returns `null`
when unsupported/failed); `JustJoinItSource` implements it via `FetchDetailAsync` + `WithDescription`; the
orchestrator sets the Minor-tier description on the offer (a new `Offer.SetDescription(string?)` or via
`RefreshMinorContent`). Failures/blocks are tolerated — the offer still collects; a null body renders as
"description not available" (FR-014). Existing offers backfill their body on their **next scan sighting**
(the "body-missing" clause), so no startup network calls are needed.

**Rationale**: Honours "eager during scan" while respecting the 001 ADR-2 source-access accepted risk
(re-fetching unchanged offers every scan would ~double source requests and raise the anti-bot block risk).
The orchestrator is the only place that knows new/updated. Because the body enters `OfferEnrichmentInputs`,
setting it fires the existing per-offer enrichment invalidation — so the **first post-upgrade scan**
re-enriches offers that gain a body (summaries/fit improve with the description); one-time and bounded.

**Alternatives**: The adapter fetches every offer's body (blind to persistence state → wasteful + block
risk). Lazy-on-open (clarified out). A separate body-backfill job at startup (adds startup network calls;
the next-scan clause is simpler and the scheduler is normally off per the run notes).

---

## R8 — No data lost: migration, backfill, backup, export

**Decision**:
- **ONE** append-only migration `AffinityMetric` adds **only** the `offer_affinity` table (PK `offer_id`,
  FK → `offers` cascade, index on `state`, jsonb `resembles` default `'[]'`). **No** offers-table change
  (the body column pre-exists). `AppDbContext` adds `DbSet<OfferAffinity>`; the config is auto-discovered by
  `ApplyConfigurationsFromAssembly`; the `OfferId` converter already applies (no `ConfigureConventions`
  line).
- Affinity rows are an **invariant** (one per offer): created `Pending` at scan-upsert (beside
  enrichment/fit), and **`DatabaseInitializer.BackfillEnrichmentAsync` is extended** to also insert a
  `Pending` `offer_affinity` row for every offer lacking one. That method already runs at **startup** and,
  via the existing **`IEnrichmentBackfill` (`EnrichmentBackfillRunner`) → `RestoreService`**, after an
  **older-backup restore** (003 `TRUNCATE`s the full HEAD table list). **No new backfill port** is needed.
- **Backup**: add `"offer_affinity"` to `BackupTables.InsertOrder` (Tier 3, after `offer_fit`). The COPY
  snapshot is catalog-driven, so that one line is the only backup change; the existing
  `BackupTablesCompletenessTests` (model tables == backup list) fails until it is listed (protects FR-018).
- **Export**: `OfferExport` gains `Description` (the captured body — a fact, aids portability) and
  `AffinityScore` (nullable current *produced* score) — satisfying SC-006. Fit stays excluded (unchanged —
  FR-016).

**Rationale**: Reuses the proven satellite-invariant + dual-path backfill exactly (002/003/005), with the
smallest possible surface. The body needs no migration.

**Alternatives**: A dedicated affinity backfill port (needless duplication). A migration-`Up()` backfill
(never runs on restored data — 003 lesson). Exporting the full derived affinity payload (score suffices;
the rest is a recomputable cache recoverable via backup).

---

## R9 — Read projection, ordering, and orthogonality to fit

**Decision**: In `OfferReadService`, add `AffinityView(State, Score, Resembles, Rationale)` to
`OfferListItem` (`Affinity` + `AffinityState`), projected exactly like fit: `produced` only when the stored
`inputs_hash` equals the recomputed current hash **and** `appliedCount ≥ 3`; else `failed`/`pending`; and
**`insufficient`** when `appliedCount < 3`. Add `OfferSort.Affinity` (produced-affinity score desc, then
rank) for "prioritise by affinity" (FR-004). Affinity depends **only** on application history — **not** the
CV profile — so it is fully orthogonal to fit; the default two-tier rank and all fit projection/ordering are
**unchanged** (FR-010/FR-016). `OfferListMeta` gains affinity counts + `appliedCount`/`hasAffinityBasis` so
the UI can message the cold-start state.

**Rationale**: One more independent metric, presented as its own block/number (never blended — FR-003),
reusing fit's exact projection discipline while leaving fit itself untouched.

**Alternatives**: Folding affinity into the default rank or the fit score (hides which signal is which;
changes 002 behaviour).

---

## Resolved unknowns

All Technical-Context items are known (stack unchanged from 001–005; no new dependency). The spec's four
clarifications resolved the only open scope questions (compute locality, basis weighting, body-fetch timing,
cold-start threshold). **No `NEEDS CLARIFICATION` remain.**
