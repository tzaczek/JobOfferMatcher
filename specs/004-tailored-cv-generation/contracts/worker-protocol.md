# Contract: Claude Code Tailored-CV Worker (`/tailor-cv`)

**Feature**: `004-tailored-cv-generation` | **Date**: 2026-06-30

The "worker" is **Claude Code running under the user's Claude Max plan** — not a backend service and not
the paid Anthropic API. It is packaged as a re-runnable repo slash command and drains the app's
tailored-CV queue over loopback. This is the **entire AI path** for the feature; the backend generates
nothing and calls no external AI (FR-005 / SC-007 / Principle IV). It extends the 002 `/enrich` worker
contract to a fourth output kind — a **tailored CV (HTML)**.

## Packaging

- A repo slash command **`.claude/commands/tailor-cv.md`** (invoked `/tailor-cv`), sibling to
  `.claude/commands/enrich.md`.
- **Run mode** (002 ADR-4): the worker and the app **share a host + filesystem**. Supported mode is the
  default `./start.ps1` (Postgres in docker, **app via `dotnet run`** on `http://localhost:5180`), where
  loopback is genuine and the source-CV path resolves on the worker's filesystem.
- Uses only: **Bash/curl** (loopback HTTP), the **Read** tool (the source CV PDF **and** the
  `cv_versions/` layout files in the repo). **No** external network, API key, or `@anthropic-ai` package.

## Loop

```
repeat:
  GET {base}/api/tailored-cv/pending?limit=10
  if meta.pendingTotal == 0: stop; report what you did
  for each item in items:
      1. Read the SOURCE CV at item.sourceCv.path (the original PDF) directly. If unreadable
         (item.sourceCv.readable == false) and item.sourceCv.fallbackText is present, use that text.
      2. Read the LAYOUT recipe from the repo: cv_versions/v2_two_column.html (the two-column A4
         template) and cv_versions/NOTES.md (the print/pagination rules). Mirror that structure/CSS.
      3. Follow item.prompt (the user's exact, possibly-edited instructions) and emphasise
         item.emphasisedSkills, using item.offer (title/company/seniority/skills) as the target.
      4. Produce ONE self-contained HTML document (inline CSS, like v2_two_column.html) — a CV tailored
         to this offer. Use ONLY information present in the source CV: re-emphasise, reorder, rephrase —
         NEVER invent employers, dates, roles, qualifications, or skills the CV does not contain (FR-006).
      collect a result, ECHOING item.workItemId and item.generationVersion
  POST {base}/api/tailored-cv/results   { "results": [ … ] }
  // honor per-item outcome; "superseded" items were regenerated meanwhile — skip, they reappear if still pending
```

The worker is **stateless and restartable**: it owns no queue state; a crash mid-batch leaves those
items pending. The backend renders the returned HTML to the downloadable **PDF** (Playwright) — the
worker never produces a PDF or any binary.

## Result object shapes

```jsonc
// produced — the tailored CV as a complete HTML document
{ "workItemId": "tailored:{offerGuid}", "generationVersion": 3, "status": "produced",
  "html": "<!doctype html><html>…full self-contained CV…</html>" }

// un-producible (e.g. source CV unreadable AND no usable fallback text, or content too thin)
{ "workItemId": "tailored:{offerGuid}", "generationVersion": 3, "status": "failed",
  "reason": "source CV unreadable and no fallback text" }
```

## Failure & supersede semantics

- A genuinely un-producible item → `status: "failed"` with a short `reason`. The **server** counts
  attempts and flips the item to terminal `failed` at `AppSettings.Enrichment.RetryLimit` (default 3).
  The worker does **not** track attempts.
- If the backend's Playwright render of returned HTML fails, the server records it as a failed attempt
  (`outcome: "renderFailed"`); the worker need do nothing special.
- **Supersede**: if the user regenerated while the worker was producing, the write-back's
  `generationVersion` won't match the current one → server returns `outcome: "superseded"` and writes
  nothing (the newer request is already `Pending` and will be re-served). Always echo the
  `generationVersion` you were given.

## No fabrication (FR-006 / Principle III)

The source CV is the **only** content source. The tailored CV re-emphasises and reorders real
experience toward the offer; it must never add an employer, date, role, certification, or skill that is
not in the source CV. If the offer wants a skill the candidate lacks, **do not** claim it — the
honest tailoring is to foreground adjacent real strengths.

## Privacy guarantees (FR-005 / SC-007 / Principle IV)

- The only egress of CV/offer content is the user's **own** Max-plan Claude Code session (the sanctioned
  worker), explicitly not "the application backend." Nothing reaches an external service.
- The **source CV binary** is delivered as a **filesystem path** and read locally; the prompt,
  emphasised skills, source-CV `fallbackText`, and the produced HTML traverse only the
  **loopback-restricted** `/api/tailored-cv/*` channel. The loopback guard is the load-bearing PII control.
- The backend adds **no** AI dependency (no-AI-package guard test) and makes **no** outbound AI call ⇒
  **0** records transmitted externally (SC-007).
