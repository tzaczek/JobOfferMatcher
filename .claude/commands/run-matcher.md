---
description: "Run the ENTIRE Job Offer Matcher pipeline end-to-end in one go: scan all sources (collect offers + capture bodies), drain the enrichment queue (CV profile + offer summaries + fit + affinity), then drain the tailored-CV queue. One command = scan → enrich → tailor. Loopback-only, re-runnable, idempotent."
---

# /run-matcher — the whole pipeline (scan → enrich → tailor CVs)

You are the **pipeline runner** for this repo's Job Offer Matcher. This one command chains the three
producer steps into a single run:

1. **Scan** — collect offers from all sources + capture each offer's body.
2. **Enrich** — the `/enrich` worker: CV profile, offer summaries, **fit**, and **affinity**.
3. **Tailor CVs** — the `/tailor-cv` worker: per-offer tailored CVs (opt-in).

The backend is a passive local store + queue; **you** (Claude Code under the user's Max plan) are the
only producer of every AI output. The backend makes **no** external AI call — this command is the entire
AI path, exactly as in `/enrich` and `/tailor-cv`.

**Base URL**: `http://localhost:5180` (the host-process run mode). If the user ran the app on another
port, use that. All `/api/enrichment/*` and `/api/tailored-cv/*` calls must come from **loopback**.

**Tools you may use**: `Bash`/curl (loopback HTTP only) and `Read` (the local CV PDF + the `cv_versions/`
layout files). **Never** use any external network, API key, or AI SDK.

## Options (interpret from the invocation)

Run all three phases by default. Honour simple natural-language overrides in the user's message:
- "skip scan" / "no scan" → start at enrich, use the offers already collected.
- "sources=<id,id>" → scan only those source ids (default: all enabled sources).
- "enrich only" / "no cv" → stop after phase 2.
- "cv only" → run only phase 3 (the tailored-CV drain).

## Pre-flight

`GET {base}/api/enrichment/status`. If it fails with connection-refused, the app isn't running in **host
mode** — tell the user to start it (`./start.ps1`, or a host `dotnet run --project backend/src/Web` bound
to `http://localhost:5180`) and stop. **Do not try to start the app yourself.** If it returns, note the
pending counts so you can confirm progress later.

## Phase 1 — SCAN  (collect offers + capture bodies) — *unless skipped*

1. `POST {base}/api/scans/run` with `{ "sourceIds": null }` (all enabled sources; or the requested ids).
   This call is **synchronous and can take several minutes** — it also fetches the detail **body** for
   each new / updated / body-missing justjoin.it offer, paced ~1 req/s. Prefer to run it in the
   **background** and poll `GET {base}/api/scans`: the in-progress run shows `finishedAt: null`, and its
   counts finalise only when it completes.
2. When it finishes, read the run summary. **A multi-source scan normally reports outcome `partial` with
   `LayoutChanged`** — that is the built-in <50% sanity guard (it compares each source against the prior
   run's total and skips disappearance reconciliation), **not a failure**. Report
   `collected / new / updated / unavailable / failed`.

The scan creates the invariant `Pending` satellites (enrichment + fit + affinity) for new offers and
captures bodies, so phase 2 has work to do. (Bodies only populate for justjoin.it today; other sources
tolerate a null body → "not available".)

## Phase 2 — ENRICH  (CV profile + summaries + fit + affinity)

Drain the enrichment queue exactly as the **`/enrich`** worker (full per-kind rules in
`.claude/commands/enrich.md` — read it if you need the detail). Repeat until drained:

1. `GET {base}/api/enrichment/pending?limit=100` → `{ meta, items }`.
2. If `meta.pendingTotal == 0` (or `items` is empty) → phase done.
3. Produce **one result per item** by `item.kind`, always echoing back `item.workItemId`, `item.kind`,
   and `item.inputsHash`. Stay within `meta.guidance` (soft caps):
   - **`cvProfile`** → `Read` the source PDF at `item.document.path` (fall back to
     `item.document.fallbackText` if `readable == false`; `status:"unreadable"` if neither is usable).
     Produce `skills` + `seniority` + `summary`. **Do profiles first — fit depends on a produced profile.**
   - **`offerSummary`** → a ≤`guidance.offerSummaryWords` plain summary of the posting + ≤`guidance.maxKeySkills` `keySkills`.
   - **`offerFit`** *(this is "calculate fit")* → a holistic `score` 0–100 + `matched` + `missing` +
     ≤`guidance.rationaleWords` `rationale`, judging `item.offer` against `item.profile` and letting
     `item.weights`/`item.preferences` tilt emphasis (heavier Skills weight → skill gaps hurt more, etc.).
   - **`offerAffinity`** → a `score` 0–100 + `resembles` + ≤`guidance.rationaleWords` `rationale`, judging
     how closely `item.offer` resembles `item.appliedBasis` (the offers the user applied to — self already
     excluded). Only emitted when ≥3 offers are applied.
   **Never fabricate.** An un-producible item returns `status:"failed"` with a short `reason` (the server
   tracks retries and flips it to `failed` at `meta.retryLimit`).
4. `POST {base}/api/enrichment/results` with `{ "results": [ … ] }` (batch the whole page). Honour each
   `outcome`; `stale` items simply reappear next pass. Go to 1.

Notes: below 3 applied offers, affinity is `insufficient` and **no** affinity items are emitted (fine);
fit is absent until a CV profile is produced. When done, `GET {base}/api/enrichment/status` should show
`pendingTotal: 0`.

## Phase 3 — TAILOR CVs  — *unless skipped*

Tailored CVs are **opt-in per offer** (the user requests them in the UI), so this queue is often empty —
that's normal. Drain it exactly as the **`/tailor-cv`** worker (full rules in
`.claude/commands/tailor-cv.md`). Repeat until drained:

1. `GET {base}/api/tailored-cv/pending?limit=10`.
2. If `meta.pendingTotal == 0` (or no items) → phase done.
3. For each item: `Read` the source CV at `item.sourceCv.path` (fall back to `item.sourceCv.fallbackText`),
   plus the layout recipe `cv_versions/v2_two_column.html` and `cv_versions/NOTES.md`. Follow `item.prompt`,
   emphasise `item.emphasisedSkills`, target `item.offer`, and produce **ONE self-contained tailored HTML
   document** using **ONLY** real content from the source CV — never invent an employer, date, role,
   qualification, or skill (FR-006). Echo `item.workItemId` + `item.generationVersion`
   (`status:"failed"` + `reason` if the source CV is unusable). The backend renders the PDF itself.
4. `POST {base}/api/tailored-cv/results` with `{ "results": [ … ] }`. Go to 1.

## Final report

Summarise the run: scan outcome + counts; enrichment produced per kind and the final
`GET /api/enrichment/status` (`pendingTotal` should be 0); tailored CVs produced. Call out anything left
pending or `failed`, and remind the user they can view results at `http://localhost:5180`.

## Privacy (Principle IV / FR-005 / FR-012)

All offer + applied-offer + CV text is produced **only** inside your own Max-plan Claude Code session over
the **loopback** API; the source-CV binary is read locally by path and never traverses HTTP. The backend
adds **no** AI dependency and makes **0** outbound calls. Every phase is **re-runnable and idempotent** —
a re-run only produces what is still pending; already-produced results are skipped as `stale`/`superseded`.
