# Quickstart & Validation: Application Affinity Metric & Offer Detail Body

**Feature**: `006-application-affinity-metric` | **Date**: 2026-07-01 | **Plan**: [plan.md](./plan.md)

A run + per-user-story validation guide. Details live in [data-model.md](./data-model.md) and
[contracts/affinity-and-offer-body.md](./contracts/affinity-and-offer-body.md); implementation belongs in
`tasks.md`.

## Prerequisites

- .NET 10 SDK, Node 20+, Docker (for Postgres + real-DB tests).
- Run **host mode** so the loopback worker reaches the app (per the run notes): Postgres in Docker, the app
  via `dotnet run` on `http://localhost:5180`, scheduler off, then drive scans/enrichment manually.
  ```powershell
  ./start.ps1              # or: docker compose up -d db ; dotnet run --project backend/src/Web
  ```
- The affinity "worker" is the local **`/enrich`** Claude-Code command (loopback), same as for fit.

## Build, migrate, test

```powershell
dotnet build JobOfferMatcher.sln
# One new migration applies at startup (MigrateAsync): adds the offer_affinity table only.
dotnet test                                   # full suite (real-Postgres integration via Testcontainers)
cd frontend ; npm install ; npm run test ; npm run build
```

Expected: the new migration is the only schema change; the extended
`BackupTablesCompletenessTests`, `SchemaInvariantTests`, and the **no-AI-package guard** all pass; the
untouched 001–005 suites (the FR-016 regression contract, incl. all fit tests) stay green.

---

## US1 — A second match signal (affinity) beside fit (P1)

1. With the app running and a few offers collected, mark **≥ 3 offers of a recognisable kind** applied
   (e.g. senior .NET remote): the feed's Apply action, or `PUT /api/offers/{id}/application`.
2. Drain the queue: run **`/enrich`** (it now also produces `offerAffinity` items). Confirm
   `GET /api/enrichment/status` shows `pendingAffinity` dropping to 0.
3. Open the feed. **Expected**:
   - Each offer shows an **affinity** block *beside* the fit block — a distinct 0–100 number with a short
     rationale + `resembles` chips; fit is still shown separately and unchanged (FR-003, SC-001).
   - Offers similar to the applied set score **higher** on affinity than clearly unrelated offers
     (SC-002). Verify via `GET /api/offers` `affinity.score`.
   - `GET /api/offers?sort=affinity` orders the feed by produced affinity descending (FR-004).
   - The offer's fit, triage disposition, applied flag, and application stage (005) are all unchanged
     (FR-010).

## US2 — Read the full offer body inside the app (P1)

1. Run a scan (`POST /api/scans` / the Scan button). **Expected**: for new/updated/body-missing offers the
   scan fetches the detail body (paced); a per-offer fetch failure/block does **not** fail the scan and
   leaves that offer collected with a null body.
2. Click an offer (title / "Details"). **Expected**: an in-app **offer-detail drawer** opens showing the
   full **description & requirements** (rendered from the server-**sanitised** `descriptionHtml`), all facts
   (salary, skills, work mode, seniority, summary, fit, affinity), versions/events, and a link to the
   original external posting (FR-011/FR-013/SC-004).
3. Open an offer whose body was never captured (e.g. a delisted one). **Expected**: a clear "description not
   available" state + the external link — never blank/broken (FR-014).
4. Sanity: `curl -s localhost:5180/api/offers/<id> | jq .descriptionHtml` is populated for a scanned offer
   and contains no `<script>` (sanitised, FR-015).

## US3 — Affinity that explains itself and stays current (P2)

1. With **fewer than 3** applied offers, view the feed. **Expected**: affinity shows **"not enough
   application history yet"** (`state: "insufficient"`), not a number (FR-006, SC-003); `meta.hasAffinityBasis`
   is false.
2. Apply to a new offer (crossing to ≥ 3), run `/enrich`. **Expected**: affinity values appear with a
   rationale.
3. Un-apply one offer, then run `/enrich` again. **Expected**: `GET /api/enrichment/status` shows all
   affinity rows re-pending immediately after the un-apply (basis changed), and after draining the scores
   update — with **no** duplicate/stale values (SC-009). Changing an application's *outcome/stage* (005)
   does **not** re-pend affinity (basis is outcome-agnostic).
4. Failure path: force a bad affinity result; confirm it retries to terminal `failed` at `retryLimit` and
   the offer shows an affinity **failed** state (never a fabricated fallback — FR-009).

---

## Cross-cutting — no data lost, backup, privacy (FR-016..019, SC-005..008)

- **Upgrade preserves data**: start against a pre-006 database. **Expected**: all offers, fit, enrichment,
  applications (005), tailored CVs (004), CVs, settings, and history are intact; startup backfill creates a
  `Pending` `offer_affinity` row per offer (idempotent — re-running changes nothing). Bodies fill in on the
  next scan (SC-005).
- **Backup/restore**: create a backup, restore it. **Expected**: `offer_affinity` and offer bodies round-trip
  intact; restoring an **older** backup runs the affinity backfill (via `IEnrichmentBackfill`) so satellite
  rows are re-synthesised (SC-006). `BackupTablesCompletenessTests` proves `offer_affinity` is in the backup
  set.
- **Export**: `GET /api/export` (JSON/CSV) includes each offer's captured `description` and current produced
  `affinityScore` (SC-006).
- **Privacy**: with the no-AI-package guard green and the backend making no outbound AI call, **0** records
  are transmitted externally; offer/applied-offer text reaches only the loopback `/enrich` worker; nothing
  personal is committed (SC-007/SC-008, Principle IV).

## Definition of done (per Constitution)

Full local suite green (VI); the three UI surfaces — the affinity block (all four states), the offer-detail
drawer (sanitised body + unavailable state), and the affinity sort — run-and-looked-at (VII); no PII/secrets/
DB committed (IV); append-only migration, no existing data dropped/edited (IX); decision notes = plan
ADR-1..ADR-5 (XI).
