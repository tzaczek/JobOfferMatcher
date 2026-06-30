# Contract: Enrichment REST API

**Feature**: `002-llm-enrichment-matching` | **Date**: 2026-06-29

New HTTP surface for the LLM-enrichment feature, plus the changed offer/CV/settings DTOs. Conventions
follow feature-001 exactly: routes under `/api`, list payloads wrapped in `{ data }` (offers add
`{ data, meta }`), request bodies as nested `record` types, camelCase JSON, `Result → HTTP` via
`Web/Infrastructure/ResultExtensions.cs` (`NotFound/*NotFound → 404`, `ScanInProgress/Reentrancy →
409`, else `400`; error body `{ error: { code, message } }`), wrapped IDs parsed via `XxxId.TryParse`,
enums via case-insensitive `ParseEnum`.

All `/api/enrichment/*` endpoints are **loopback-only**: a **fail-closed** guard rejects any request
whose `HttpContext.Connection.RemoteIpAddress` is not `IsLoopback` — **and** rejects null/unknown
remote IPs — with **403** (Principle IV; this is the load-bearing PII control because these endpoints
serialize CV/offer text over the channel). The guard holds in the supported **host-process** run mode
(`./start.ps1`, app on `localhost:5180` — ADR-4); the full-container `docker-compose` packaging is not
supported for enrichment as-is (see ADR-4). No auth beyond the loopback binding.

---

## 1. `GET /api/enrichment/pending?limit=N`

Returns an ordered, self-contained batch of pending work for the Claude Code worker. `limit` default
**25**, max **100**. Ordering = FR-019 (see [data-model.md §8](../data-model.md)).

**200** response:
```jsonc
{
  "meta": {
    "pendingTotal": 59, "pendingProfiles": 1, "pendingSummaries": 30, "pendingFits": 28,
    "failedTotal": 2, "returned": 25, "hasProducedProfile": true,
    "guidance": { "offerSummaryWords": 60, "cvSummaryWords": 60, "maxKeySkills": 10, "rationaleWords": 30 },
    "retryLimit": 3
  },
  "items": [ /* discriminated by `kind` */ ]
}
```

**Work items** (camelCase; `inputsHash` MUST be echoed back on the result):
```jsonc
// kind = cvProfile  (emitted FIRST — FR-019)
{ "kind": "cvProfile", "workItemId": "cv:{cvId}:profile", "cvId": "<guid>",
  "inputsHash": "SHA256:1:<hex>", "attempt": 1,
  "document": { "path": "C:\\…\\cv-data\\<id>.pdf", "fileName": "cv.pdf", "readable": true,
                "fallbackText": null /* PdfPig text; included ONLY when readable=false (image-only PDF),
                                        to keep PII off the wire; the worker reads the original by path otherwise — FR-003 */ },
  "guidance": { "summaryWords": 60, "maxSkills": 10 } }

// kind = offerSummary
{ "kind": "offerSummary", "workItemId": "offer:{offerId}:summary", "offerId": "<guid>",
  "inputsHash": "SHA256:1:<hex>", "attempt": 1,
  "offer": { "title": "...", "company": "...", "location": "...", "workMode": "Remote",
             "employmentType": "...", "seniority": "...", "salaryBands": [ /* … */ ],
             "requiredSkills": ["…"], "niceToHaveSkills": ["…"],
             "descriptionText": "<sanitized plain text via existing IHtmlSanitizer>" },
  "guidance": { "summaryWords": 60, "maxSkills": 10 } }

// kind = offerFit  (emitted ONLY when a current produced CV profile exists)
{ "kind": "offerFit", "workItemId": "offer:{offerId}:fit", "offerId": "<guid>",
  "inputsHash": "SHA256:1:<hex>", "attempt": 1,
  "offer": { "title": "...", "requiredSkills": ["…"], "niceToHaveSkills": ["…"],
             "seniority": "...", "workMode": "Remote", "employmentType": "...",
             "normalizedMonthlySalary": 18000 },
  "profile": { "skills": ["…"], "seniority": "Senior", "summary": "…" },
  "weights": { "skills": 45, "seniority": 20, "workMode": 12, "employment": 8, "salary": 15 },
  "preferences": { "salaryFloor": 16000, "salaryTarget": 22000,
                   "preferredWorkModes": ["Remote"], "preferredEmployment": ["B2B"] },
  "guidance": { "rationaleWords": 30 } }
```

---

## 2. `POST /api/enrichment/results`

Batch write-back. Each result echoes `inputsHash` + `kind` + `status`. The server **recomputes the
current input hash from live inputs** (offer content / produced-profile version / current weights) and
compares it to the echoed `inputsHash` — it does **not** compare against the stored column (which, after
eager invalidation, still holds the last-produced hash). Server handling **per item**:

- echoed `inputsHash` ≠ freshly-recomputed current → outcome **`stale`** (ignored, row stays pending).
- `status = "produced"` and passes **loose** validation (reject only: empty summary; missing skills
  array; score outside 0..100 — soft word/skill overage accepted, optionally trimmed to the caps) →
  store payload, `state=Produced`, `producedAt=now`, `attempts=0`.
- `status = "unreadable"` (cvProfile only) → `state=Unreadable` (no retry; counted separately).
- `status = "failed"` or validation fails → `attempts++`, `lastError=reason`; if `attempts ≥
  retryLimit` → `state=Failed` (outcome `failed`), else stays `Pending` (outcome `pendingRetry`).

Re-POSTing an identical `inputsHash` + payload is **idempotent**.

**Request**:
```jsonc
{ "results": [
  { "workItemId": "offer:{id}:summary", "kind": "offerSummary", "inputsHash": "SHA256:1:<hex>",
    "status": "produced", "summary": "…", "keySkills": ["…"] },
  { "workItemId": "offer:{id}:fit", "kind": "offerFit", "inputsHash": "SHA256:1:<hex>",
    "status": "produced", "score": 82, "matched": ["C#","EF Core"], "missing": ["Kafka"], "rationale": "…" },
  { "workItemId": "cv:{id}:profile", "kind": "cvProfile", "inputsHash": "SHA256:1:<hex>",
    "status": "produced", "skills": ["…"], "seniority": "Senior", "summary": "…" },
  { "workItemId": "offer:{id}:summary", "kind": "offerSummary", "inputsHash": "SHA256:1:<hex>",
    "status": "failed", "reason": "model returned empty output" }
] }
```

**200** response:
```jsonc
{ "accepted": 3, "rejected": 1,
  "results": [ { "workItemId": "...", "outcome": "produced|stale|pendingRetry|failed|unknown",
                 "attempt": 0, "state": "Produced" } ] }
```

---

## 3. `GET /api/enrichment/status`

Drives the pending/failed indicators (FR-010/FR-016/SC-007). Counts are **eligibility-gated** queries:
`pendingFits` counts only offers that have a current **produced** CV profile (so an absent/pending/
unreadable/failed profile never leaves fits "stuck pending" and `pendingTotal` can reach 0).
```jsonc
{ "pendingTotal": 59, "pendingProfiles": 1, "pendingSummaries": 30, "pendingFits": 28,
  "failedTotal": 2, "hasProducedProfile": true, "lastResultAt": "2026-06-29T10:31:00Z" }
```

---

## 4. `POST /api/enrichment/rerun`

In-app trigger (FR-009 + FR-014). **Does not call AI** — it only mutates app state; the user then runs
the `/enrich` worker. Body `{ "scope": "failed" | "all" }`:
- `failed` (default) — re-arm `Failed` rows whose stored hash still matches current inputs → `Pending`.
- `all` — force every `Produced` row to `Pending` (a full forced re-run).

**200** → the new counts (same shape as `/status`). The one-time backfill (FR-014) is handled at
startup by `DatabaseInitializer` (after `MigrateAsync`): it idempotently creates a `Pending`
`offer_enrichment` + `offer_fit` row for every pre-existing offer and computes the existing CV's
`enrichment_input_hash`, so the pending **count** is correct on first enablement (not 0) and the next
`/enrich` pass processes everything. `/rerun` then re-arms `failed` items / forces a full re-run.

---

## 5. Settings: `GET` / `PUT /api/settings/enrichment` (FR-018)

Mirrors the existing `/api/settings/weights` pattern; `SettingsService.UpdateEnrichmentAsync`
validates all caps `> 0` and `retryLimit ≥ 1` (else **400 `InvalidEnrichmentSettings`**).
```jsonc
// GET / PUT body
{ "offerSummaryMaxWords": 60, "cvSummaryMaxWords": 60, "maxKeySkills": 10,
  "fitRationaleMaxWords": 30, "retryLimit": 3 }
```
Existing `/api/settings/weights` is **unchanged** (relabelled "guidance to Claude" in the UI only).

---

## 6. Changed existing DTOs (additive; FR-013-safe)

`GET /api/offers/` `OfferListItem` and `GET /api/offers/{id}` `OfferDetail.Offer` gain:
```jsonc
{ /* …existing fields… */
  "summary": "…" | null,
  "keySkills": ["…"],
  "enrichmentState": "pending" | "produced" | "failed",
  "fit": null  // when there is NO current produced CV profile (no CV, or profile pending/unreadable/failed)
       | { "state": "pending" }                                   // produced profile exists, fit not yet matched/invalidated
       | { "state": "failed" }                                    // retries exhausted
       | { "state": "produced", "score": 82, "matched": ["…"], "missing": ["…"], "rationale": "…" },
  "fitState": "pending" | "produced" | "failed"
}
```
Fit-absence is keyed on the **produced CV profile**, not the PdfPig `isReadable` gauge (an image-only
CV that Claude profiles still shows fits). `OfferListMeta` gains `"pendingEnrichment": n,
"failedEnrichment": m` and replaces `noReadableCv` with `"hasProducedProfile": bool`.
`GET /api/cv/` items gain `"state"`, `"summary"`, `"skills"`, `"seniority"`, `"attemptCount"`.

**Invariant (FR-005)**: a `score` is present **only** under `"state": "produced"`. The API never
returns a non-AI fallback score; unproduced items are `pending`/`failed`.
