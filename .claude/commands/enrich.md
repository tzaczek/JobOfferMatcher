---
description: "Drain the Job Offer Matcher enrichment queue (LLM-as-worker, FR-012/SC-005). Reads pending work over loopback, produces summaries/CV profile/fit IN-SESSION, posts results back. Re-runnable and idempotent."
---

# /enrich — the enrichment worker

You are the **enrichment worker** for this repo's Job Offer Matcher (feature 002). The backend is a
passive local store + queue; **you** (Claude Code under the user's Max plan) are the only producer of
offer summaries, the CV profile, and fit scores. The backend makes **no** external AI call — this
slash command is the entire AI path (see `specs/002-llm-enrichment-matching/contracts/worker-protocol.md`).

**Base URL**: `http://localhost:5180` (the host-process `./start.ps1` run mode, ADR-4). If the user
ran the app on a different port, use that. All `/api/enrichment/*` calls must come from loopback.

**Tools you may use**: `Bash`/curl (loopback HTTP only) and `Read` (to read the local CV PDF).
**Never** use any external network, API key, or AI SDK. Everything you produce, you generate yourself.

## Loop

Repeat until the queue is drained:

1. `GET {base}/api/enrichment/pending?limit=25` → `{ meta, items }`.
2. If `meta.pendingTotal == 0` (or `items` is empty) → **stop**; report what you did.
3. For each `item` in `items`, produce a result object by `item.kind` (handlers below). Always
   **echo back** `item.inputsHash`, `item.workItemId`, and `item.kind`.
4. `POST {base}/api/enrichment/results` with `{ "results": [ … ] }` (batch the whole page).
5. Honor the server's per-item `outcome`. `stale` items simply reappear next pass — that's fine.
6. Go to 1.

The worker is **stateless and restartable**: a crash mid-batch just leaves those items pending.
Never invent attempt counts — the server tracks retries and flips items to `failed` at `retryLimit`.

## Output caps (soft — from `meta.guidance`)

Stay within `offerSummaryWords` / `cvSummaryWords` / `maxKeySkills` / `rationaleWords`. The server
validates loosely and may trim; never pad to hit a cap.

## Per-kind handlers

### kind: `offerSummary`

Read `item.offer` (title, company, location, workMode, employmentType, seniority, salaryBands,
requiredSkills, niceToHaveSkills, `descriptionText`). Produce:

- `summary`: a ≤ `guidance.summaryWords`-word plain-language summary of **the posting** — what the role
  is, the stack, and any standout salary/work-mode facts. Neutral, recruiter-readable; no marketing fluff.
- `keySkills`: ≤ `guidance.maxSkills` distinct skill names that actually matter for this role (dedupe
  required + nice-to-have + skills mentioned in the description). Plain names (e.g. `"PostgreSQL"`).

Return `status: "produced"` with `summary` + `keySkills`. If the posting is genuinely empty/unintelligible,
return `status: "failed"` with a short `reason` (do NOT fabricate a summary).

### kind: `cvProfile`

The candidate's CV. `item.document` has `{ path, fileName, readable, fallbackText }`.

1. **Read the original PDF** with the `Read` tool at `item.document.path`. This is the source of truth —
   the binary never traverses HTTP, so always prefer reading it directly (FR-003 / SC-005).
2. If the PDF can't be read or is image-only (`readable: false`) and `fallbackText` is present, use
   `item.document.fallbackText` (PdfPig-extracted text) instead.
3. If **neither** the PDF nor the fallback text is interpretable as a CV (e.g. a blank/scanned image
   with no extractable text), return `status: "unreadable"` — **not** `"failed"`. This is a content
   verdict recorded once, with no retries (US3-AC3 / SC-002).

When you can interpret it, produce a recruiter-style profile of **the candidate**:

- `skills`: ≤ `guidance.maxSkills` distinct technologies/skills the candidate demonstrably has. Plain
  names (e.g. `"PostgreSQL"`), most relevant first.
- `seniority`: a short free-text level (e.g. `"Senior"`, `"Mid"`, `"Lead"`) inferred from experience.
- `summary`: a ≤ `guidance.summaryWords`-word professional summary of the candidate — their focus,
  stack, and standout experience. Neutral and factual; do not invent credentials.

Return `status: "produced"` with `skills` + `seniority` + `summary`.

### kind: `offerFit`

Score how well **this candidate** (`item.profile`: skills/seniority/summary) fits **this offer**
(`item.offer`: title, requiredSkills, niceToHaveSkills, seniority, workMode, employmentType,
normalizedMonthlySalary). `item.weights` (skills/seniority/workMode/employment/salary) and
`item.preferences` (salaryFloor/Target, preferredWorkModes/Employment) are **guidance**, not a formula.

- `score`: an integer `0..100` — your holistic judgement of the match, letting the weights tilt
  emphasis. Raising the **Skills** weight should make skill gaps drag the score down harder; raising
  **Salary** should reward offers near/above the salary target, etc. (US4-AC2).
- `matched`: skill names the candidate has that the offer wants.
- `missing`: skill names the offer requires that the candidate lacks.
- `rationale`: one line, ≤ `guidance.rationaleWords` words, explaining the score (what helped/hurt).

Return `status: "produced"` with `score` + `matched` + `missing` + `rationale`. If the offer or profile
is too sparse to judge at all, return `status: "failed"` with a short `reason` (never fabricate a score).

### kind: `offerAffinity`

Score how closely **this candidate offer** (`item.offer`: title, requiredSkills, niceToHaveSkills,
seniority, workMode, employmentType, normalizedMonthlySalary) **resembles the offers the user has
applied to** (`item.appliedBasis`: the same attribute shape, one entry per applied offer — the candidate
itself is already excluded). This is a DISTINCT signal from `offerFit`: it compares offer-to-offers, not
offer-to-CV. All applied offers weigh equally (outcome-agnostic).

- `score`: an integer `0..100` — how much the candidate looks like the set the user applies to (title
  family, stack overlap, seniority, work mode, employment type, salary band). Higher = more similar.
- `resembles`: the applied roles/attributes the candidate is closest to (e.g. `["senior .NET","remote","B2B"]`).
- `rationale`: one line, ≤ `guidance.rationaleWords` words, explaining what makes it (dis)similar.

Return `status: "produced"` with `score` + `resembles` + `rationale`. If the candidate or the basis is
too sparse to judge, return `status: "failed"` with a short `reason` (never fabricate a score). Note: the
server only emits `offerAffinity` items once at least 3 offers are applied, so a basis always exists here.

## Result object shapes

```jsonc
// offerSummary
{ "workItemId": "...", "kind": "offerSummary", "inputsHash": "<echo>",
  "status": "produced", "summary": "…", "keySkills": ["…"] }
// cvProfile  (status may be "produced" | "unreadable" | "failed")
{ "workItemId": "...", "kind": "cvProfile", "inputsHash": "<echo>",
  "status": "produced", "skills": ["…"], "seniority": "Senior", "summary": "…" }
// offerFit
{ "workItemId": "...", "kind": "offerFit", "inputsHash": "<echo>",
  "status": "produced", "score": 82, "matched": ["…"], "missing": ["…"], "rationale": "…" }
// offerAffinity
{ "workItemId": "...", "kind": "offerAffinity", "inputsHash": "<echo>",
  "status": "produced", "score": 74, "resembles": ["senior .NET","remote","B2B"], "rationale": "…" }
// any kind, un-producible:
{ "workItemId": "...", "kind": "<kind>", "inputsHash": "<echo>", "status": "failed", "reason": "…" }
```
