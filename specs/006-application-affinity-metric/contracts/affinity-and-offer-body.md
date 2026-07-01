# Contract: Affinity Metric & Offer Detail Body

**Feature**: `006-application-affinity-metric` | **Date**: 2026-07-01 | **Plan**: [plan.md](./plan.md)

Two surfaces, both **additive** and reusing existing endpoints (no new route group):
1. The **affinity metric** extends the loopback `/api/enrichment` worker queue with a 4th kind
   (`offerAffinity`) and adds an `affinity` block to the offers read models.
2. The **offer body** rides the existing `GET /api/offers/{id}` (`OfferDetail.descriptionHtml`) — already
   sanitised server-side.

camelCase JSON at the boundary; loopback-only for `/api/enrichment/*` (the load-bearing PII control).

---

## 1. Offers feed & detail (extends `contracts/rest-api.md §Offers`)

### `GET /api/offers` — each item gains `affinity` beside `fit`
```jsonc
{
  "offerId": "…", "title": "…", "fit": { "state": "produced", "score": 82, "matched": [], "missing": [], "rationale": "…" },
  "affinity": {                       // NEW — a DISTINCT second signal (never blended with fit)
    "state": "produced",             // produced | pending | failed | insufficient
    "score": 74,                      // 0..100 when produced; null otherwise
    "resembles": ["Senior .NET", "remote", "B2B"],   // which applied roles/attributes it is close to
    "rationale": "Close to 3 roles you applied to: senior .NET, remote, B2B."
  },
  "affinityState": "produced"         // convenience mirror, like fitState
  // …existing fields unchanged…
}
```
- `state: "insufficient"` ⇒ fewer than **3** applied offers exist (cold start, FR-006); `score` is null and
  the UI shows "not enough application history yet".
- A `produced` affinity is returned **only** when the stored `inputsHash` matches the recomputed current
  hash **and** `appliedCount ≥ 3`; otherwise `pending`/`failed`/`insufficient` (never a fallback number).

`meta` gains: `pendingAffinity`, `failedAffinity`, `appliedCount`, `hasAffinityBasis` (`appliedCount ≥ 3`).

### `GET /api/offers?sort=affinity` — NEW sort value (FR-004)
Produced-affinity score descending, then the default rank. Existing `sort` values (`rank`, `fit`, `salary`,
`recency`, `published`) unchanged.

### `GET /api/offers/{id}` — body now populated (US2)
Response is the **existing** `OfferDetail` shape; `descriptionHtml` is now non-null for offers whose body has
been captured, and is **already sanitised** server-side (`Ganss.Xss`):
```jsonc
{
  "offer": { …OfferListItem incl. fit + affinity… },
  "descriptionHtml": "<p>Sanitised job description &amp; requirements…</p>",  // null ⇒ "description not available"
  "versions": [ … ], "events": [ … ]
}
```
The front-end renders `descriptionHtml` as HTML (safe — server-sanitised) in the offer-detail drawer, with
the external `canonicalUrl` link always present (FR-013/FR-014).

---

## 2. `/api/enrichment` — the `offerAffinity` work-item kind (loopback-only)

### `GET /api/enrichment/pending?limit=25`
`meta` gains `pendingAffinity`, `failedAffinity`, `appliedCount`, `hasAffinityBasis`, and
`guidance.affinityRationaleWords`. `items[]` may include (only when `appliedCount ≥ 3`):
```jsonc
{
  "kind": "offerAffinity",
  "workItemId": "offer:<guid>:affinity",
  "inputsHash": "SHA256:1:…",
  "attempt": 1,
  "offer": {                         // the CANDIDATE (FitOfferView shape)
    "title": "Senior .NET Engineer", "requiredSkills": ["C#",".NET"], "niceToHaveSkills": ["Azure"],
    "seniority": "senior", "workMode": "Remote", "employmentType": "b2b", "normalizedMonthlySalary": 24000
  },
  "appliedBasis": [                  // the user's applied offers (self EXCLUDED), same attribute shape
    { "title": "Lead .NET Developer", "requiredSkills": ["C#",".NET","EF"], "niceToHaveSkills": [],
      "seniority": "senior", "workMode": "Remote", "employmentType": "b2b", "normalizedMonthlySalary": 26000 }
    // …tens at most (single user)…
  ],
  "guidance": { "rationaleWords": 30 }
}
```
Ordering: appended after each offer's `offerSummary`/`offerFit` items, in the existing available-first /
published-desc / offerId order. Below 3 applied offers, **no** affinity items are emitted.

### `POST /api/enrichment/results` — result item for affinity
Reuses `SubmitResultsRequest`/`EnrichmentResultItem` (new fields are additive & optional):
```jsonc
{ "results": [
  { "workItemId": "offer:<guid>:affinity", "kind": "offerAffinity", "inputsHash": "SHA256:1:…",
    "status": "produced", "score": 74, "resembles": ["senior .NET","remote","B2B"],
    "rationale": "Close to 3 roles you applied to." }
] }
```
- Server recomputes the current `inputsHash` (candidate offer-enrich hash + applied-basis version) from live
  inputs; a mismatch or `appliedCount < 3` ⇒ outcome `stale` (the item simply reappears next pass).
- `status: "produced"` requires `score ∈ [0,100]`; else it is treated as `failed` (attempts++ →
  terminal `failed` at `retryLimit`). Response is the existing `SubmitResultsResponse` (per-item outcomes).

### `GET /api/enrichment/status` and `POST /api/enrichment/rerun`
`status` gains the affinity counts + `appliedCount`. `rerun` (`scope=failed|all`) also re-arms affinity rows
(failed→pending, or all→pending), exactly as for summaries/fits.

---

## 3. Worker protocol delta (`/enrich` command)

Extends `specs/002-llm-enrichment-matching/contracts/worker-protocol.md`. The **same** re-runnable,
stateless, loopback-only `/enrich` command gains one case in its loop:
```
- offerAffinity : compare item.offer (the candidate) to item.appliedBasis (the offers the user applied to)
                  and produce a 0–100 affinity score = how closely the candidate resembles that set,
                  plus `resembles` (the applied roles/attributes it is closest to) and a
                  ≤ guidance.rationaleWords rationale. ECHO item.inputsHash and item.kind.
                  If it cannot be produced → status "failed" with a short reason.
```
Privacy unchanged: the candidate + applied-offer text is serialized **only** over the loopback enrichment
channel to the user's own Max-plan Claude session — never to an external service; the backend adds no AI
dependency and makes no outbound AI call (FR-008/SC-007). Affinity reveals *which offers the user applied
to* to that local session only (same trust boundary as fit sending the CV/offer text).

---

## 4. Backward compatibility / invariants

- All additions are optional/additive: existing `/api/offers` and `/api/enrichment` consumers keep working
  (FR-016). Fit's shape, states, ordering, and export are unchanged.
- Affinity `produced` is gated by `inputsHash` match **and** `appliedCount ≥ 3` on both write-back and read
  (FR-005/FR-009 — never a fabricated value).
- `offer_affinity` is an invariant (one `Pending` row per offer), created at scan-upsert and backfilled on
  upgrade + older-restore; included in backup/restore (guarded by `BackupTablesCompletenessTests`).
