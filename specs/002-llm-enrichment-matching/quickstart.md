# Quickstart & Validation: LLM Enrichment & Matching

**Feature**: `002-llm-enrichment-matching` | **Date**: 2026-06-29 | **Plan**: [plan.md](./plan.md)

A runnable validation guide that proves the feature end-to-end. Implementation details live in
[data-model.md](./data-model.md), [contracts/](./contracts/), and the eventual `tasks.md`. Endpoint
shapes are in [contracts/enrichment-api.md](./contracts/enrichment-api.md); the worker behavior in
[contracts/worker-protocol.md](./contracts/worker-protocol.md).

## Prerequisites

- Feature 001 running: `./start.ps1` (docker-compose PostgreSQL + `dotnet run --project backend/src/Web`).
- At least one completed scan so the feed has offers (`POST /api/scans/run`, or use existing data).
- The `LlmEnrichment` migration applied at startup (`MigrateAsync`).
- A CV uploaded for US3/US4 (`POST /api/cv/` multipart `file`), stored under the gitignored `cv-data/`.

## Run the worker

The worker is **Claude Code under your Max plan** — open Claude Code in this repo and run the slash
command:

```
/enrich
```

It loops `GET /api/enrichment/pending` → produces outputs in-session (reading the CV PDF directly for
the profile) → `POST /api/enrichment/results`, until `pendingTotal == 0`. It is safe to re-run any time.

---

## Validation scenarios

### US1 — Publish date & "Recently published" sort ✅ (already delivered)
1. `GET /api/offers/` → each item has `publishedAt`.
2. `GET /api/offers/?sort=published` → offers ordered newest-first, date-less last.
   *Expected*: matches the delivered behavior; this feature does not change it.

### US2 — Offer summary + key skills
1. After a scan, `GET /api/offers/` → new offers show `enrichmentState: "pending"`, `summary: null`
   (never a non-AI placeholder).
2. Run `/enrich`. Re-fetch → each available offer shows `enrichmentState: "produced"`, a `summary`
   (≤ configured words), and `keySkills` (≤ configured count). *(SC-001: 100% of non-failed available
   offers.)*
3. Change an offer's description (re-scan a changed posting) → that offer flips to `pending`; next
   `/enrich` regenerates only its summary/skills. *(US2-AC3 / FR-006.)*

### US3 — CV profile (recruiter-style understanding)
1. Upload a readable CV → `GET /api/cv/` shows `state: "pending"`.
2. Run `/enrich`. Re-fetch → `state: "produced"` with `skills`, `seniority`, and a short `summary`
   drawn from the CV. *(SC-002.)*
3. Upload an image-only/garbled CV → after `/enrich` it is `state: "unreadable"` (no crash, no
   profile, counted separately from failed). *(US3-AC3 / SC-002.)*

### US4 — AI fit score with matched/missing
1. With a produced CV profile, `GET /api/offers/` → fits show `{ "state": "pending" }` until the
   worker runs; **no** numeric score appears yet (FR-005).
2. Run `/enrich`. Re-fetch → offers show `fit: { state: "produced", score, matched, missing,
   rationale }`. *(SC-003: ≥95% of non-failed available offers; 0% non-AI fallback.)*
3. Raise the **Skills** weight (`PUT /api/settings/weights`) → all fits flip to `pending`; after
   `/enrich`, scores/rationales reflect the heavier skill weighting. *(US4-AC2 / FR-007.)*
4. With **no** CV uploaded, fits are `null` (absent) — not a fallback score. *(Edge case "No CV".)*

### US5 — Freshness & on-demand re-run
1. Replace the CV (or change preferences) → `GET /api/enrichment/status` shows `pendingFits` jump to
   the full offer count (100% of fits invalidated). *(SC-004 / FR-007.)*
2. `POST /api/enrichment/rerun {scope:"failed"}` re-arms any failed items; run `/enrich` → pending
   count drops to **0**. *(SC-007 / FR-009.)*
3. The feed renders a clean mix of `produced` / `pending` / `failed` cards during a partial pass.
   *(Edge case "Partial worker pass".)*
4. First-time enablement: with offers + a CV already present, the startup `DatabaseInitializer`
   backfill creates `pending` satellite rows so `GET /api/enrichment/status` shows the correct pending
   count immediately (not 0); the first `/enrich` then processes them all. *(FR-014 / FR-010 / US5-AC3.)*

### Failure & resilience
- Force a failure (worker returns `status:"failed"` 3×) → the offer shows `enrichmentState:"failed"`
  with an error badge, counted in `failedTotal`, **not** in `pendingTotal`, and excluded from the
  100%/95% targets. *(FR-015/FR-016.)*
- Stop the worker mid-pass → the rest of the app (collection, feed, statuses, export) works normally;
  unprocessed items stay `pending`. *(Edge case "Worker never runs".)*

### Privacy & isolation (FR-012 / SC-005)
- `GET /api/enrichment/pending` from a non-loopback address → **403**; a request with a null/unknown
  remote IP also → **403** (fail-closed).
- The no-AI-package guard test passes (asserts `Directory.Packages.props`/csprojs reference no AI
  package); there is no outbound AI call in the backend. The CV **binary** is delivered as a **path**,
  read locally by the worker (host-process mode, ADR-4); CV/offer text crosses only the loopback
  channel. *(0 records transmitted externally.)*

### Regression (FR-013 — feature 001 unchanged)
- The full feature-001 test suite stays green; collection, dedup, availability/reconciliation,
  role-grouping, salary normalization, and `GET /api/export` behave exactly as before.

## Done check
- `dotnet test` green (Domain/Application/Infrastructure incl. real-Postgres integration).
- The running UI shows produced summaries/skills/fit, plus pending/failed/unreadable states, verified
  by eye (Principle VII).
