# Contract: Tailored-CV REST API

**Feature**: `004-tailored-cv-generation` | **Date**: 2026-06-30

A new route group **`/api/tailored-cv`**, mounted under `/api` (via `FeatureEndpoints.MapFeatureEndpoints`)
and guarded by the existing **`.AddEndpointFilter<LoopbackOnlyFilter>()`** (the same fail-closed loopback
control used by `/api/enrichment/*` and `/api/backup/*`). JSON is camelCase at the boundary. Expected
failures use the existing `{ error: { code, message } }` envelope via `Result<T>.ToHttp(...)`; the
queue/list reads return view DTOs directly (the 002 style).

The group has **two audiences**: UI endpoints (driven by the modal + the dedicated page) and **worker**
endpoints (drained by the `/tailor-cv` Claude Code session — see
[worker-protocol.md](./worker-protocol.md)). Both are loopback-only.

---

## UI endpoints

### `GET /api/tailored-cv`
List all tailored CVs (the dedicated page). → `200 { data: TailoredCvView[] }`, newest `generatedAt`
first (then `createdAt`). Each item links back to its offer.

### `GET /api/tailored-cv/offer/{offerId}`
The tailored CV for one offer (reopen from the offer). → `200 TailoredCvView` or `404 { error: { code:
"TailoredCvNotFound" } }`.

### `GET /api/tailored-cv/offer/{offerId}/draft[?skills=a,b,c]`
The **prefilled, non-persisted** modal contents (FR-013/FR-003). Server-assembles the default prompt +
emphasised-skills selection + the attached source-CV reference. If the offer already has a tailored CV,
the draft is seeded from its stored `prompt`/`emphasisedSkills` (so reopening shows what produced it).
The optional **`skills`** query (a subset of `allOfferSkills`) **recomposes** the default `prompt` for
that selection — this backs the modal's "toggle a skill ⇒ the visible prompt updates" behaviour (FR-004)
while the prompt is still the (unedited) default. → `200 TailoredCvDraftView`. Failures:
`404 OfferNotFound`; `409 { error: { code: "NoCvOnFile" } }` when the user has no CV to tailor from
(the modal shows "add a CV first").

```jsonc
// TailoredCvDraftView
{
  "offerId": "…", "offerTitle": "Senior .NET Engineer", "company": "…",
  "prompt": "Tailor the attached CV for the role of Senior .NET Engineer at … . Emphasise: …\nUse ONLY information present in the attached CV — re-emphasise and reorder, never invent … . Lay it out using the cv_versions two-column A4 layout …",
  "emphasisedSkills": ["PostgreSQL", ".NET", "EF Core", "React"],   // default selection (toggleable)
  "allOfferSkills": ["PostgreSQL", ".NET", "EF Core", "React", "Docker", "Azure"], // full pool for the toggles
  "sourceCv": { "id": "…", "fileName": "3fa85f64….pdf" }            // null ⇒ NoCvOnFile path
}
```

### `POST /api/tailored-cv/offer/{offerId}`
Create **or** regenerate (idempotent on the offer — latest-only). Persists the prompt + emphasised
skills, sets `Pending`, bumps `GenerationVersion`, and enqueues the work. Body:

```jsonc
{ "prompt": "…edited or default…", "emphasisedSkills": ["…"], "sourceCvId": "…optional override…" }
```

→ `200 TailoredCvView` (state `pending`). Failures: `404 OfferNotFound`; `409 NoCvOnFile`;
`400 { error: { code: "InvalidTailoredCvRequest" } }` (empty prompt). **Calls
`MaintenanceGate.WaitWhileActiveAsync`** so it defers through a restore.

### `GET /api/tailored-cv/offer/{offerId}/preview`
Serve the produced **HTML** for **in-app viewing** (US1 "shown to me" — rendered in a modal iframe;
distinct from the polished PDF download of US3). → `200` `text/html` (the stored
`tailored-{offerId:N}.html`), `Content-Disposition: inline`. Failures: `404 TailoredCvNotFound`;
`409 { error: { code: "TailoredCvNotReady" } }` when state ≠ `produced`.

### `GET /api/tailored-cv/offer/{offerId}/download`
Stream the produced PDF. → `200` `application/pdf`, `Content-Disposition: attachment; filename="…"`
(via `Results.File(absolutePath, "application/pdf", downloadName)`). Failures: `404 TailoredCvNotFound`;
`409 { error: { code: "TailoredCvNotReady" } }` when state ≠ `produced` (FR-009 — download unavailable
while pending/failed). Download name is human-friendly, e.g. `CV - {company} - {offerTitle}.pdf`.

### `DELETE /api/tailored-cv/offer/{offerId}`
Remove the tailored CV (row + both files). → `204`. Idempotent (`404`→treated as already-gone is
acceptable; or `404 TailoredCvNotFound`). Used by the dedicated page's "Remove".

---

## Worker endpoints (drained by `/tailor-cv`)

### `GET /api/tailored-cv/pending?limit=N`
Pending generation requests with everything the worker needs. `limit` clamped 1..50 (default 10).
**Does not** consult the gate (read). The `sourceCv` mirrors 002's `CvDocumentView` (path + fallback,
read by the worker off disk — never streamed).

```jsonc
{
  "meta": { "pendingTotal": 2, "failedTotal": 0, "returned": 2, "retryLimit": 3 },
  "items": [
    {
      "workItemId": "tailored:{offerGuid}",      // entity:guid, echoed on write-back
      "offerId": "…",
      "generationVersion": 3,                     // ECHO this back
      "prompt": "…the exact stored prompt…",
      "emphasisedSkills": ["PostgreSQL", ".NET", "EF Core"],
      "offer": { "title": "…", "company": "…", "seniority": "…",
                 "requiredSkills": ["…"], "niceToHaveSkills": ["…"] },
      "sourceCv": { "path": "C:/…/cv-data/3fa85f64….pdf", "fileName": "3fa85f64….pdf",
                    "readable": true, "fallbackText": null }
    }
  ]
}
```

### `POST /api/tailored-cv/results`
Batch write-back. The backend **renders each `produced` item's HTML to a PDF** (Playwright), stores both
files, and `MarkProduced`; render failure or `failed` status → `RecordFailure`. **Calls
`MaintenanceGate.WaitWhileActiveAsync`.** The supersede guard (`Accepts`) rejects a stale
`generationVersion`.

```jsonc
// request
{ "results": [
    { "workItemId": "tailored:{offerGuid}", "generationVersion": 3, "status": "produced", "html": "<!doctype html>…" },
    { "workItemId": "tailored:{offerGuid2}", "generationVersion": 1, "status": "failed", "reason": "source CV unreadable" }
] }
// response
{ "accepted": 1, "rejected": 1,
  "results": [
    { "workItemId": "tailored:{offerGuid}",  "outcome": "produced",   "attempt": 0, "state": "produced" },
    { "workItemId": "tailored:{offerGuid2}", "outcome": "failed",     "attempt": 1, "state": "pending"  }
] }
// outcome ∈ "produced" | "failed" | "superseded" | "renderFailed"
// "superseded": generationVersion ≠ current (a newer regenerate happened) — nothing written
// "renderFailed": HTML received but Playwright render threw — counted as a failure attempt
```

---

## View DTO

```jsonc
// TailoredCvView
{
  "offerId": "…", "offerTitle": "…", "company": "…",
  "sourceCvId": "…",
  "state": "pending" | "produced" | "failed",
  "generationVersion": 3,
  "emphasisedSkills": ["…"],
  "prompt": "…the exact prompt used…",
  "hasPdf": true,                 // state==produced && pdf file present → download enabled
  "generatedAt": "2026-06-30T12:00:00Z" | null,
  "lastError": null | "…"
}
```

---

## Privacy & guards (Principle IV / FR-005 / SC-007)

- The **whole group is loopback-only** (`LoopbackOnlyFilter`). The **source CV binary** is delivered to
  the worker as a **filesystem path** (never over HTTP), exactly as 002. Prompt + emphasised skills + the
  source-CV path/fallback text traverse only this loopback channel. The generated **PDF** is streamed
  only to the local user over loopback. The backend makes **no** external AI call and references **no**
  AI SDK (the no-AI-package guard test covers the new code). **0** records transmitted externally.
- Generated files live in gitignored `cv-data`; they are included in 003 backup/restore (FR-016/FR-017).
