---
description: "Drain the Job Offer Matcher tailored-CV queue (LLM-as-worker, FR-005/SC-007). Reads pending requests over loopback, reads the source CV by path + the cv_versions layout, produces a per-offer tailored CV as HTML, posts it back. Re-runnable and idempotent."
---

# /tailor-cv — the tailored-CV worker

You are the **tailored-CV worker** for this repo's Job Offer Matcher (feature 004). The backend is a
passive local store + queue; **you** (Claude Code under the user's Max plan) are the only producer of
tailored CVs. The backend makes **no** external AI call and renders the **PDF itself** from the HTML you
return — this slash command is the entire AI path (see
`specs/004-tailored-cv-generation/contracts/worker-protocol.md`). It extends the 002 `/enrich` worker to
a fourth output kind: a **tailored CV (HTML)**.

**Base URL**: `http://localhost:5180` (the host-process `./start.ps1` run mode, ADR-4). If the user ran
the app on a different port, use that. All `/api/tailored-cv/*` calls must come from loopback.

**Tools you may use**: `Bash`/curl (loopback HTTP only) and `Read` (the local CV PDF **and** the
`cv_versions/` layout files in the repo). **Never** use any external network, API key, or AI SDK. The
**source-CV binary is read from disk by path** — it never traverses HTTP (FR-005 / Principle IV).

## Loop

Repeat until the queue is drained:

1. `GET {base}/api/tailored-cv/pending?limit=10` → `{ meta, items }`.
2. If `meta.pendingTotal == 0` (or `items` is empty) → **stop**; report what you did.
3. For each `item` in `items`, produce ONE result object (steps below). Always **echo back**
   `item.workItemId` and `item.generationVersion`.
4. `POST {base}/api/tailored-cv/results` with `{ "results": [ … ] }` (batch the whole page).
5. Honor the server's per-item `outcome`. A `superseded` item was regenerated meanwhile — skip it, it
   reappears next pass if still pending. A `renderFailed` item means your HTML didn't render; tighten it.
6. Go to 1.

The worker is **stateless and restartable**: a crash mid-batch just leaves those items pending. Never
invent attempt counts — the server tracks retries and flips items to `failed` at `meta.retryLimit`.

## Producing a tailored CV

For each `item`:

1. **Read the SOURCE CV** at `item.sourceCv.path` (the original PDF) with the `Read` tool. This is the
   only content source. If it is unreadable (`item.sourceCv.readable == false`) and
   `item.sourceCv.fallbackText` is present, use that text instead. If **neither** is usable, return
   `status: "failed"` with a short `reason` (do **not** fabricate a CV).
2. **Read the LAYOUT recipe** from the repo: `cv_versions/v2_two_column.html` (the two-column A4 template)
   and `cv_versions/NOTES.md` (the print/pagination rules — two `.page` grids, navy sidebar, A4 height).
   Mirror that structure and CSS.
3. **Follow `item.prompt`** — the user's exact, possibly-edited instructions — and emphasise
   `item.emphasisedSkills`, using `item.offer` (title / company / seniority / requiredSkills /
   niceToHaveSkills) as the target role.
4. **Produce ONE self-contained HTML document** (inline CSS, like `v2_two_column.html`) — a CV tailored
   to this offer. Use **ONLY** information present in the source CV: re-emphasise, reorder, and rephrase
   real experience toward the role. **NEVER** invent an employer, date, role, qualification, certification,
   or skill the CV does not contain (FR-006 / Principle III). If the offer wants a skill the candidate
   lacks, foreground the closest real strength — do not claim it.

The backend renders your HTML to the downloadable **A4 PDF** (Playwright/Chromium) — you never produce a
PDF or any binary.

## Result object shapes

```jsonc
// produced — the tailored CV as a complete, self-contained HTML document
{ "workItemId": "tailored:{offerGuid}", "generationVersion": 3, "status": "produced",
  "html": "<!doctype html><html>…full self-contained CV…</html>" }

// un-producible (source CV unreadable AND no usable fallback text, or content too thin)
{ "workItemId": "tailored:{offerGuid}", "generationVersion": 3, "status": "failed",
  "reason": "source CV unreadable and no fallback text" }
```

## Privacy (FR-005 / SC-007 / Principle IV)

The only egress of CV/offer content is **your own** Max-plan Claude Code session — never the application
backend, never an external service. The source-CV binary is read locally by path; the prompt, emphasised
skills, `fallbackText`, and the HTML you return traverse only the **loopback-restricted**
`/api/tailored-cv/*` channel. The backend adds **no** AI dependency and makes **no** outbound AI call ⇒
**0** records transmitted externally.
