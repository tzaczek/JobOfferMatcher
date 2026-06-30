# Contract: Claude Code Enrichment Worker

**Feature**: `002-llm-enrichment-matching` | **Date**: 2026-06-29

The "worker" is **Claude Code running under the user's Claude Max plan** — not a backend service and
not the paid Anthropic API. It is packaged as a re-runnable slash command in this repo and drains the
app's enrichment queue over loopback. This contract defines its behavior so the backend and the
command stay in lockstep.

## Packaging

- A repo slash command **`.claude/commands/enrich.md`** (invoked `/enrich`), sibling to the existing
  `.claude/skills/` and `.claude/workflows/`.
- **Run mode (ADR-4)**: the worker and the app **share a host + filesystem**. The supported mode is
  the default `./start.ps1` (Postgres in docker, **app via `dotnet run`** on `http://localhost:5180`),
  where loopback is genuine and the CV path resolves on the worker's filesystem. The full-container
  `docker-compose` packaging is **not** supported for enrichment as-is (the host worker can't reach the
  container loopback or read the container-internal CV path) — to use it, bind the published port to
  `127.0.0.1` and mount `cv-data` to a host path, or run the app as a host process for enrichment.
- Uses only: **Bash/curl** (loopback HTTP) and the **Read** tool (to read the local CV PDF). No
  external network, no API key, no `@anthropic-ai` package anywhere.

## Loop

```
repeat:
  GET {base}/api/enrichment/pending?limit=25
  if meta.pendingTotal == 0: stop
  for each item in items:
      produce the output IN-SESSION (Claude itself generates it):
        - cvProfile  : Read item.document.path (the original PDF) directly; if unreadable,
                       fall back to item.document.fallbackText; if neither is interpretable,
                       return status "unreadable" (NOT "failed")
        - offerSummary: a ≤ guidance.summaryWords plain-language summary + ≤ guidance.maxSkills key skills
        - offerFit    : a 0–100 score + matched/missing skills + a ≤ guidance.rationaleWords rationale,
                        applying weights as guidance
      collect a result object, ECHOING item.inputsHash and item.kind
  POST {base}/api/enrichment/results   { "results": [ … ] }
  // honor the server's per-item outcome (stale items simply reappear next pass)
```

The worker is **stateless and restartable**: it owns no queue state. Re-running it is always safe
(idempotent write-back keyed by `inputsHash`); a crash mid-batch just leaves those items pending.

## Output rules (must match `EnrichmentSettings` guidance)

- Caps are **soft**: prefer to stay within `summaryWords` / `maxSkills` / `rationaleWords`; the server
  validates loosely and may trim. Never pad to hit a cap.
- `cvProfile.summary` is a short professional summary of the *candidate*; `offerSummary.summary` is a
  short summary of the *posting*.
- `offerFit.score` is `0..100`; `matched`/`missing` are skill-name arrays; `rationale` is one line.
- Weights are **guidance**, not a formula — raising the Skills weight should make skill gaps weigh
  more heavily in the score/rationale (US4-AC2).

## Failure semantics (FR-015/FR-016)

- A genuinely un-producible item → return `status: "failed"` with a short `reason`. The **server**
  counts attempts and flips the item to terminal `failed` at `retryLimit` (default 3). The worker
  does **not** track attempts.
- `unreadable` (cvProfile) is distinct from `failed`: it is a content verdict (the document can't be
  interpreted at all), recorded once with no retries (SC-002 / US3-AC3).

## Privacy guarantees (FR-012 / SC-005 / Principle IV)

- The only egress of offer/CV content is **the user's own Max-plan Claude Code session** — the
  sanctioned worker, explicitly not "the application backend." Nothing reaches an external service.
- The CV **binary** is delivered as a **filesystem path** and never traverses HTTP; the worker reads
  the PDF locally from the gitignored `cv-data/`. CV/offer **text** (the path, the PdfPig
  `fallbackText` when the original is unreadable, the profile, and offer descriptions) *is* serialized
  to the worker, but only over the **loopback-restricted** enrichment channel — never to an external
  service. The loopback guard is therefore the load-bearing PII control (ADR-4).
- The backend adds **no** AI dependency (enforced by a no-AI-package guard test) and makes **no**
  outbound AI call ⇒ it transmits **0** records externally (SC-005).
