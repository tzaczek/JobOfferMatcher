# Data Model: Application Affinity Metric & Offer Detail Body

**Feature**: `006-application-affinity-metric` | **Date**: 2026-07-01 | **Plan**: [plan.md](./plan.md)

This feature **adds** one derived-cache satellite (`offer_affinity`) and **populates** an existing
Minor-tier column (`offers.description_html`). It does **not** alter any 001–005 entity (FR-016); those are
referenced only where the new data attaches. Layering unchanged: framework-free **Domain**, ports in
**Application**, EF Core in **Infrastructure**. Conventions retained: wrapped IDs, jsonb string arrays via
`HasJsonbListConversion<string>()`, **append-only** migrations, `Result<T>` for expected failures, the
`InputHash` (`algo:version:hash`) recompute-from-live-inputs stale guard.

---

## 1. `OfferAffinity` (new aggregate / table `offer_affinity`) — an `OfferFit` twin

1:1 with `Offer` (PK = `OfferId` = FK → `offers(id)` **cascade**). Standalone so the worker writes it
without loading the `Offer` aggregate. **`Score` is only ever displayed when `State == Produced`, the
stored `InputsHash == current`, AND `appliedCount ≥ MinApplications`** — otherwise the read path shows
`pending`/`failed`/`insufficient`; **never a fabricated fallback** (FR-009).

| Field | Type | Notes |
|---|---|---|
| `OfferId` | `OfferId` (PK/FK) | reuse existing wrapped id; FK → `offers(id)` cascade |
| `State` | `EnrichmentState` | `varchar(20)` via `HasConversion<string>()` — reuse the existing enum |
| `Attempts` | `int` | resets to 0 on produce/invalidate |
| `Score` | `int?` | `0..100` (validated on write-back) |
| `Resembles` | `IReadOnlyList<string>` | `resembles` jsonb — which applied roles/attributes it is close to (the fit `Matched` analogue) |
| `Rationale` | `string?` | ≤ `AffinityRationaleMaxWords` (soft) |
| `InputsHash` | `string?` | freshness/stale-write guard — `OfferAffinityInputs` serialized |
| `ProducedAt` | `DateTimeOffset?` | |
| `LastError` | `string?` | reason of last failed attempt |

Constant: `public const int MinApplications = 3;` (clarification #4). Methods mirror `OfferFit` exactly:
`CreatePending(OfferId)`, `MarkProduced(score, resembles, rationale, inputsHash, at)`,
`RecordFailure(error, retryLimit)` (`Attempts++`; `Failed` at limit), `Invalidate()`, `Rearm()`,
`ForcePending()`. Index on `State`.

There is **no** offer-body table or column change: `offers.description_html` (Minor-tier) already exists.

---

## 2. Input-hash composers (Domain, pure) — additions to `EnrichmentInputs.cs`

Same `EnrichmentHashing` style (SHA-256 over canonical sorted-key JSON; versioned so a formula change forces
a global recompute).

### `AppliedBasisInputs`
```
public const int CurrentVersion = 1;

// applied = EVERY offer marked Applied (the candidate is NOT excluded from the version — the version is a
// single global value shared by all offers). Self-exclusion happens only later, when building a specific
// offer's work payload (§5/§7): that offer's own snapshot is dropped from its `appliedBasis` list.
static InputHash? Version(IReadOnlyList<(OfferId Id, string FingerprintHash)> applied)
  // null when applied.Count < OfferAffinity.MinApplications  → "insufficient basis"
  // else SHA-256 over the sorted array of { "id": <guid>, "fp": <fingerprintHash> }
```
Any change to the applied **set** (apply/un-apply) or to an applied offer's **content** (fingerprint hash)
changes the version → every affinity input hash differs → all affinity pending (§4).

### `OfferAffinityInputs`
```
public const int CurrentVersion = 1;

static InputHash Hash(InputHash offerEnrichmentHash, InputHash appliedBasisVersion)
  // root = { "basis": appliedBasisVersion.Serialized, "offer": offerEnrichmentHash.Serialized }
```
`offerEnrichmentHash` is the candidate offer's existing `OfferEnrichmentInputs.Hash(...)` (offer content
incl. Minor-tier description) — so a candidate content/description change also re-flips its affinity, and
the affinity input naturally follows the newly-captured body.

---

## 3. Applied basis & the offer body (no new tables)

- **Applied basis** is *derived* at query/write-back time from `offers WHERE applied = true` (their
  `OfferId` + `CurrentFingerprint.Hash`). No basis table (Principle X). `appliedCount = COUNT(applied)`.
- **Offer body** is the existing `offers.description_html` (Minor-tier). It is **captured during a scan**
  (§5) and **sanitised on read** (existing `Ganss.Xss` in `OfferReadService.GetAsync`), served by the
  existing `GET /api/offers/{id}` → `OfferDetail.DescriptionHtml`.

---

## 4. Invalidation & creation hooks (eager `state` writes; additive, FR-016-safe)

Extends the existing table in 002 data-model §7 with the affinity column.

| Trigger (existing write path) | Action (added) |
|---|---|
| Offer created (scan upsert, `ScanOrchestrator.UpsertAsync` new branch) | also create `Pending` `offer_affinity` row (beside enrichment/fit) |
| Offer content change on scan (`ApplyUpdate` / `RefreshMinorContent` changed the enrichment hash) | also `Invalidate()` that offer's affinity (existing `InvalidateSatellitesAsync`, extended) |
| **Apply / un-apply** (`SetOfferApplication.MarkAppliedAsync` / `ClearAsync`) | `InvalidateAllAffinityAsync()` — `UPDATE offer_affinity SET state='Pending', attempts=0` (basis changed → all affinity pending). Also re-arms terminal `Failed` rows. |
| Body captured for an offer during a scan (§5) sets `description_html` | flows through the existing enrichment-hash change → that offer's enrichment/fit/affinity invalidate (correct: the body improves the summary and the affinity candidate signal) |
| First enablement / older-restore (backfill, §6) | idempotently insert a `Pending` `offer_affinity` row for every offer lacking one |
| Application **outcome/stage** change (005) | **no affinity change** (basis is outcome-agnostic — clarification #2) |

The `inputs_hash` recompute-from-live-inputs guard (§2) is the correctness backstop on both the worker
write-back (server recomputes the current hash + basis version from live inputs and rejects a mismatch as
`stale`) and the read path (a `Produced` row whose hash ≠ current, or with `appliedCount < 3`, renders as
`pending`/`insufficient`). The eager hooks only keep the `state` column and counts cheap.

---

## 5. Offer-body capture on scan (US2)

`IJobSource` gains `Task<string?> FetchBodyAsync(CollectedOffer offer, CancellationToken ct)` (returns
`null` when unsupported or on failure/block). `JustJoinItSource` implements it via
`client.FetchDetailAsync(slug)` + `JustJoinItMapper.WithDescription`. In `ScanOrchestrator.UpsertAsync`,
after classifying the offer, fetch + set the body **only** when the offer is **new**, **updated**, or its
stored `DescriptionHtml is null` (existing offers backfill on next sighting):

```
if (isNew || kind == Updated || existing.DescriptionHtml is null) {
    var body = await adapter.FetchBodyAsync(collected, ct);   // resilient: null on failure/block
    if (body is not null) offer.SetDescription(body);          // Minor-tier; fingerprint unaffected
}
```
`Offer.SetDescription(string?)` is a new Minor-tier mutator (sets `DescriptionHtml`; no version/event/
fingerprint/`HasUnseenUpdate` change) — the same tier as `RefreshMinorContent`. Setting it before the
existing `EnrichmentHashOf` change-check lets the body flow into the summary/fit/affinity invalidation.
`SetDescription` stores the **raw** body; sanitisation stays at the read boundary (`Ganss.Xss`), matching
the current design (do not double-sanitise; the stored value is the captured fact).

---

## 6. Backfill (idempotent; upgrade AND older-restore) — extend the existing enrichment backfill

`DatabaseInitializer.BackfillEnrichmentAsync` (already run at startup and, via `EnrichmentBackfillRunner`
→ `RestoreService`, after an older restore) gains an affinity pass:

```
var withAffinity = (await db.OfferAffinities.Select(a => a.OfferId).ToListAsync(ct)).ToHashSet();
foreach (var id in offerIds.Where(id => !withAffinity.Contains(id)))
    await db.OfferAffinities.AddAsync(OfferAffinity.CreatePending(id), ct);
```
Re-running only fills gaps → the affinity row is an invariant (one per offer); there is **no** "row absence
= pending" path. **No new port** (`IEnrichmentBackfill` already carries this method to the restore path).
Offer **bodies** are *not* backfilled here (no startup network calls) — they fill in on the next scan (§5).

---

## 7. Pending-work ordering & counts (FR-006/FR-019) — extend `EnrichmentService`/`IEnrichmentRepository`

- `OfferWorkRow` gains `Affinity` (`OfferAffinity`). `GetPendingWorkAsync` computes `appliedCount` and the
  `AppliedBasisInputs.Version` once; **only when `appliedCount ≥ 3`**, for each ordered offer whose
  `Affinity.State == Pending`, it emits an `OfferAffinityWorkItem` (candidate view + the basis snapshot
  excluding self + `inputsHash`) — appended after that offer's summary/fit.
- `SatelliteCounts` gains `PendingAffinity`/`FailedAffinity`, **gated on `appliedCount ≥ 3`** (like fits are
  gated on a produced profile) so affinity never sits "stuck pending" below the threshold and `pendingTotal`
  can reach 0. `PendingMeta`/`EnrichmentStatusView` gain the affinity counts + `appliedCount` /
  `hasAffinityBasis`.
- Write-back (`SubmitResultsAsync` → `HandleResultAsync`): a new `sub == "affinity"` branch mirrors the fit
  branch — recompute the current `OfferAffinityInputs.Hash` from live inputs (offer enrich hash + basis
  version); reject a mismatch or `appliedCount < 3` as `stale`; on `produced` with `score ∈ [0,100]` call
  `MarkProduced(score, resembles, rationale, current, now)`; else `RecordFailure`.
- `IEnrichmentRepository` gains `GetAffinityAsync`/`AddAffinityAsync`, affinity in `GetOfferWorkRowsAsync`
  and `GetCountsAsync(bool countAffinity)`, `InvalidateAllAffinityAsync`, and affinity in
  `RearmFailedAsync`/`ForceAllPendingAsync` (so `/rerun` covers affinity too).

`WorkItemId` scheme reuses the existing `offer:{guid}:{sub}` (`sub = "affinity"`), so `TryParseWorkItem`
needs no change.

---

## 8. Read-model changes (`Application/Offers/OfferReadModels.cs` + `OfferReadService`)

- New `AffinityView(string State, int? Score, IReadOnlyList<string> Resembles, string? Rationale)` — `State
  ∈ produced | pending | failed | insufficient`.
- `OfferListItem` gains `Affinity` (`AffinityView?`) + `AffinityState` (`string`), beside the existing
  `Fit`/`FitState` (never blended — FR-003).
- `OfferListMeta` gains `PendingAffinity`, `FailedAffinity`, `AppliedCount`, `HasAffinityBasis`
  (`AppliedCount ≥ 3`) for the cold-start messaging.
- `OfferReadService.ProjectAffinity(offer, offerEnrichHash, ctx)`: if `ctx.AppliedCount < 3` →
  `AffinityView("insufficient", …)`; else recompute `OfferAffinityInputs.Hash(offerEnrichHash,
  ctx.BasisVersion)` and return `produced` (hash match) / `failed` (hash match) / `pending`. The
  `ReadContext` gains `AppliedCount`, `BasisVersion`, and the `offer_affinity` dictionary (loaded like
  `Fits`). Fit projection/ordering are **unchanged**.
- `OfferSort` gains `Affinity`; `Sort(...)` adds `OfferSort.Affinity =>` produced-affinity score desc, then
  rank (mirrors the `Fit` sort). The default two-tier rank is unchanged (FR-010/FR-016).
- `OfferDetail` already carries `DescriptionHtml` (sanitised) — no change needed; the front-end detail view
  consumes it.

---

## 9. Export changes (`OfferExport` + `ExportReader`) — SC-006

`OfferExport` gains:
- `string? Description` — the captured offer body (a fact; aids portability, Principle IX). CSV carries it
  as a field (may be large); JSON verbatim.
- `int? AffinityScore` — the current **produced** affinity score (null when not produced / insufficient).
  Fit stays excluded from export (unchanged — FR-016).

`ExportReader` projects both (the affinity score with the same input-hash + `appliedCount ≥ 3` guard as the
read model, so a stale value is exported as null rather than misleading).

---

## 10. Migration plan — `AffinityMetric` (one new migration; Principle IX)

`dotnet ef migrations add AffinityMetric` (next after the 005 `ApplicationTracking` migration). **New
migration only — no prior migration edited.** `Up()`:

1. `CreateTable("offer_affinity", …)` — PK `offer_id`; FK → `offers(id)` **cascade**; `state varchar(20)
   NOT NULL`; `attempts int NOT NULL DEFAULT 0`; `score int NULL`; `resembles jsonb NOT NULL DEFAULT '[]'`;
   `rationale text NULL`; `inputs_hash varchar(80) NULL`; `produced_at timestamptz NULL`; `last_error text
   NULL`; index on `state`.

`Down()` drops `offer_affinity`. Migration is **schema only**; the per-offer `Pending` **backfill** is the
runtime step in `BackfillEnrichmentAsync` (§6), consistent with the codebase convention that migrations
never seed offer data. `AppDbContext` adds `DbSet<OfferAffinity>`; the configuration is auto-discovered by
`ApplyConfigurationsFromAssembly`; the `OfferId` value converter already applies (no `ConfigureConventions`
line). **No change** to the `offers` table (the body column pre-exists).

---

## 11. Entity → requirement map

| Entity / change | Requirements |
|---|---|
| `OfferAffinity` satellite (score/resembles/rationale/state + `inputs_hash` guard) | FR-001, FR-003, FR-005, FR-009 |
| `AppliedBasisInputs` / `OfferAffinityInputs` + apply/un-apply invalidation | FR-002, FR-007 |
| `MinApplications = 3` gate + `insufficient` read state | FR-006 |
| `offerAffinity` kind on `/api/enrichment` + `/enrich` (local worker, no external call) | FR-002, FR-005, FR-008 |
| `OfferSort.Affinity` | FR-004 |
| Affinity orthogonal to fit/profile; fit unchanged | FR-010, FR-016 |
| Body capture on scan (`IJobSource.FetchBodyAsync` + orchestrator) + Minor-tier `SetDescription` | FR-011, FR-016 |
| Existing `OfferDetail` + `Ganss.Xss` sanitisation + external link | FR-012, FR-013, FR-014, FR-015 |
| One table-only migration; invariant rows; backfill on upgrade + older-restore | FR-017 |
| `offer_affinity` in `BackupTables.InsertOrder` + completeness guard; export body + score | FR-017, FR-018 |
| Loopback-only worker; no external AI call; nothing committed | FR-008, FR-019 |
