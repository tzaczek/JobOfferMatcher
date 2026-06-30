# Feature Specification: LLM Enrichment & Matching (Claude-as-Worker)

**Feature Branch**: `002-llm-enrichment-matching` (spec directory; no git branch created — repo has no `before_specify` hook)

**Created**: 2026-06-29

**Status**: Draft — User Story 1 (Published date & sorting) already implemented and deployed; remaining stories not yet built.

**Input**: User description: "LLM-powered offer enrichment, CV understanding, and matching — using Claude Code (the user's Claude Max plan) as the enrichment worker, NOT the paid Anthropic API. The app stays fully local. The app exposes an internal enrichment queue that Claude Code drains, generating the LLM outputs with the Max plan and writing them back into the local DB. Four capabilities: (1) published date + sorting [done], (2) per-offer summary + skills via Opus, (3) CV skills/info via Opus reading the PDF, (4) CV-to-offer matching via Opus (fit score + matched/missing + reasoning, user weights kept as guidance)."

## Clarifications

### Session 2026-06-29

- Q: How should the system treat an offer summary/skills or a fit that the worker attempts but cannot produce a valid result for (e.g. malformed/empty model output, or content too thin to summarise)? → A: After a bounded number of attempts (default 3), mark the item **"failed"** — a distinct terminal state, shown as an error badge, counted separately from "pending", and excluded from the 100%/95% success targets; a failed item is only re-attempted when its inputs change or the user triggers a manual re-run.
- Q: Should the worker produce/refresh summary+skills and fit for offers that are currently unavailable (delisted)? → A: Yes — the worker processes **all** offers regardless of availability; availability does not gate enrichment or fit, and an offer going unavailable does not invalidate its existing results.
- Q: When an output is invalidated (inputs changed) but not yet recomputed, what does the card display? → A: Show **"pending"** — the prior, now-superseded value is not displayed as a current result; it may be retained internally only for freshness comparison.
- Q: Should the spec set explicit bounds for generated output sizes (summary, key-skills count, rationale)? → A: Yes — soft caps as worker guidance + loose validation, but **configurable** (not hard-coded). Defaults: offer summary ≤ ~3 sentences (~60 words), CV summary ≤ ~3 sentences, key skills ≤ 10, fit rationale ≤ 1 sentence (~30 words).
- Q: How should the pending-work set be ordered? → A: Prioritised — a CV profile is produced before any fit that depends on it; then available offers before unavailable; newest publish date first.
- Q: Do the user's matching preferences (salary target, preferred work modes/employment) feed the AI fit and therefore invalidate it, alongside CV and weights? → A: Yes — fit is computed from the offer + CV profile + importance weights **and** matching preferences; changing any of CV, weights, or preferences invalidates all fits (FR-004/FR-006/FR-007/SC-004). Preferences do **not** affect offer summaries/skills.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See and sort by when an offer was published (Priority: P1) — ✅ DELIVERED

As a job seeker, I want each offer to show when it was published and to sort my feed by publish date, so I can prioritise the freshest postings.

**Why this priority**: Smallest, highest-certainty slice; independent of any AI work; already shipped to validate the delivery loop.

**Independent Test**: Open the offers feed, choose the "Recently published" sort, and confirm offers appear newest-first with a visible "Published \<date\>" on each card.

**Acceptance Scenarios**:

1. **Given** offers whose source reports a publish date, **When** I view the feed, **Then** each offer card shows that date.
2. **Given** the feed, **When** I select sort "Recently published", **Then** offers are ordered by publish date, newest first, with offers lacking a date sorted last.

---

### User Story 2 - Read a quick summary and key skills for each offer (Priority: P1)

As a job seeker scanning many postings, I want a short, plain-language summary and a clean list of key required skills for each offer, so I can triage without reading the full description.

**Why this priority**: Delivers standalone value even before a CV is uploaded; turns raw, inconsistent source descriptions into a uniform, skimmable card.

**Independent Test**: After the enrichment worker runs, confirm each available offer card shows a generated summary and a key-skills list; before it runs, confirm the offer shows a "pending" state rather than a blank or a non-AI placeholder.

**Acceptance Scenarios**:

1. **Given** an offer that has been processed, **When** I view its card, **Then** I see a concise summary and a list of key skills derived from the posting.
2. **Given** a newly collected or changed offer not yet processed, **When** I view it, **Then** its summary and skills show as "pending".
3. **Given** an offer whose content later changes, **When** the next worker pass runs, **Then** its summary and skills are regenerated.

---

### User Story 3 - Understand my CV the way a recruiter would (Priority: P2)

As a job seeker, I want the app to read my uploaded CV and derive my skills, seniority, and a short professional summary, so my profile reflects what I actually offer (not just keyword hits).

**Why this priority**: Produces the profile that powers matching (US4) and is independently useful on the CV page; depends only on an uploaded CV.

**Independent Test**: Upload a CV, run the worker, and confirm the CV/profile page shows extracted skills, a seniority level, and a short summary; confirm an image-only/garbled CV is handled without crashing and is flagged appropriately.

**Acceptance Scenarios**:

1. **Given** a readable CV, **When** the worker processes it, **Then** the profile shows skills, seniority, and a brief summary drawn from the CV.
2. **Given** a CV just uploaded, **When** I view the profile before processing, **Then** the profile shows as "pending".
3. **Given** a CV the document reader cannot interpret, **When** processing runs, **Then** the system records it as unreadable without error and shows a clear state.

---

### User Story 4 - Get an AI fit score with matched and missing skills (Priority: P2)

As a job seeker, I want each offer scored 0–100 for fit against my profile, with a matched-vs-missing breakdown and a one-line rationale, weighted by what I care about, so I can focus on the best matches.

**Why this priority**: The headline "matcher" value; depends on the CV profile (US3) and the user's importance weights and matching preferences.

**Independent Test**: With a processed CV present, run the worker and confirm offers show a fit score, matched skills, missing skills, and a short rationale; confirm no offer shows a fit produced by the old non-AI scorer.

**Acceptance Scenarios**:

1. **Given** a processed profile and a processed offer, **When** the worker matches them, **Then** the offer shows a 0–100 fit score, matched skills, missing skills, and a brief rationale.
2. **Given** my importance weights, **When** matching runs, **Then** the score and rationale reflect those weights (e.g. raising the Skills weight makes skill gaps weigh more heavily).
3. **Given** an offer not yet matched, **When** I view it, **Then** its fit shows as "pending" — never a fallback numeric score.

---

### User Story 5 - Keep results fresh and re-run on demand (Priority: P3)

As a job seeker, I want results to update when the underlying data changes and to re-run processing myself, so I'm never acting on stale matches and I can refresh after editing my CV, weights, or matching preferences.

**Why this priority**: Operational correctness and control; ties the previous stories together over time. Includes the one-time backfill of existing offers and CV.

**Independent Test**: Change the CV (or the weights or matching preferences), confirm all fit results flip to "pending" and are refreshed on the next worker run; trigger a manual re-run and confirm the pending count drops to zero; confirm a count of pending items is visible.

**Acceptance Scenarios**:

1. **Given** existing matched offers, **When** I change my CV, my weights, or my matching preferences, **Then** all fit results become "pending" until re-processed.
2. **Given** pending items, **When** I trigger a re-run, **Then** the worker processes them and the pending count decreases to zero.
3. **Given** the feature is newly enabled, **When** the first worker pass runs, **Then** all previously collected offers and the existing CV are processed (backfill).
4. **Given** the feed, **When** items are pending, **Then** the UI shows how many items are pending enrichment.

### Edge Cases

- **No CV uploaded**: offers still get summaries/skills (US2); fit (US4) is simply absent, not a fallback score.
- **Worker never runs / unavailable**: all AI-derived fields stay "pending" indefinitely; the rest of the app (collection, feed, statuses, export) works normally.
- **Partial worker pass**: some items processed, others still pending — the feed must render a mix cleanly.
- **Offer content changes after processing**: its summary/skills/fit go stale and are re-derived; the user's status/disposition on the offer is unaffected.
- **CV, weights, or matching-preferences change**: invalidates fit for all offers (not summaries/skills).
- **Unreadable CV**: recorded as unreadable; no profile produced; no crash.
- **Offer with no description**: summary/skills derived from the available fields (title, company, skills, salary) or marked thin.
- **Unprocessable offer / failed match**: when the worker attempts an offer's summary/skills or a fit and cannot produce a valid result (malformed/empty output, or content too thin), the item is retried up to the bounded limit (default 3) and then marked **"failed"** — shown as an error state, never left permanently "pending" and never given a non-AI fallback.
- **Unavailable / delisted offer**: enrichment and fit are still produced and refreshed for it (availability does not gate the worker); an offer going unavailable does not invalidate its existing summary/skills/fit.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST display each offer's source-reported publish date and let users sort the feed by it, newest first, with date-less offers sorted last. *(delivered)*
- **FR-002**: System MUST generate, for each offer, a concise plain-language summary and a list of key required skills, derived from the offer's content, using Claude.
- **FR-003**: System MUST derive, from each uploaded CV, the user's skills, a seniority level, and a short professional summary, using Claude reading the original CV document (with the document's extracted text used as input only when the document itself cannot be read directly).
- **FR-004**: System MUST produce, for each offer relative to the user's profile, a 0–100 fit score, a matched-vs-missing skills breakdown, and a brief rationale, using Claude, applying the user's importance weights **and matching preferences (salary target, preferred work modes, preferred employment)** as guidance.
- **FR-005**: System MUST treat Claude as the sole source for summaries, skills, profile, and fit; when an item has not yet been produced it MUST be shown as "pending" — the system MUST NOT substitute a non-AI computation as a fallback.
- **FR-006**: System MUST persist all Claude-derived outputs and recompute a given output only when its inputs change: an offer's content (including its description) for that offer's summary/skills; the offer plus the user's profile plus the user's weights and matching preferences for that offer's fit.
- **FR-007**: System MUST invalidate (mark "pending") every offer's fit when the user's CV, weights, or matching preferences change, and invalidate an individual offer's summary/skills/fit when that offer's content changes. Once invalidated, an output MUST be displayed as "pending" and its prior, now-superseded value MUST NOT be shown to the user as a current result (the prior value MAY be retained internally only for freshness comparison).
- **FR-008**: System MUST expose a local-only mechanism for the enrichment worker to (a) retrieve the set of pending work with the inputs needed to produce each result, and (b) write results back into storage.
- **FR-009**: Users MUST be able to trigger processing of pending items on demand.
- **FR-010**: System MUST surface the number of items currently pending enrichment.
- **FR-011**: Users MUST be able to edit their importance weights (Skills, Seniority, Work mode, Employment, Salary); the system MUST pass those weights to Claude as guidance for matching.
- **FR-012**: System MUST NOT call any paid external AI API, and MUST NOT transmit offer or CV data to an external AI service, from the application backend; all AI generation is performed by the local Claude Code worker under the user's own plan.
- **FR-013**: System MUST preserve existing offer collection, deduplication, availability/reconciliation, role-grouping, salary normalisation, and export behaviour unchanged.
- **FR-014**: System MUST perform a one-time backfill that processes all previously collected offers and the existing CV when the feature is first enabled.
- **FR-015**: System MUST cap automatic attempts to produce any Claude-derived output (offer summary/skills, CV profile, offer fit) at a bounded retry limit (configurable, default 3); once the limit is reached without a valid result, the item MUST be marked **"failed"** — a terminal state distinct from "pending" — and MUST NOT be retried automatically until its inputs change or the user triggers a manual re-run.
- **FR-016**: System MUST surface "failed" items distinctly from "pending" items (e.g. an error indicator on the affected card) and MUST count failed items separately from pending items when reporting outstanding work.
- **FR-017**: Offer availability MUST NOT gate enrichment or fit — the worker's pending-work set includes offers regardless of their availability/reconciliation state, and an offer becoming unavailable MUST NOT, by itself, invalidate or remove its enrichment or fit.
- **FR-018**: System MUST bound the size of generated outputs via **configurable** limits (not hard-coded), passed to the worker as guidance and applied as loose validation. Default limits: offer summary ≤ ~3 sentences (~60 words); CV professional summary ≤ ~3 sentences; key-skills list ≤ 10 items; fit rationale ≤ 1 sentence (~30 words).
- **FR-019**: The pending-work set (FR-008) MUST be ordered so that a CV profile is produced before any fit that depends on it; remaining work MUST be prioritised — available offers before unavailable, then newest publish date first.

### Key Entities *(include if feature involves data)*

- **Offer enrichment**: per offer — a generated summary, a key-skills list, a freshness marker tying the result to the offer-content version it was produced from, a produced-at timestamp, plus a processing state (pending / produced / failed) and an attempt counter that drives the bounded-retry-then-failed behaviour.
- **CV profile (extended)**: per CV/user — skills, seniority, and a short professional summary, plus a processing state (pending / produced / unreadable / failed) and an attempt counter. Replaces the keyword-derived profile.
- **Offer fit/match**: per offer — a 0–100 score, matched skills, missing skills, a brief rationale, a freshness marker tying it to the (offer + profile + weights + preferences) version it was produced from, a produced-at timestamp, plus a processing state (pending / produced / failed) and an attempt counter that drives the bounded-retry-then-failed behaviour.
- **Importance weights (existing)**: relative weights for Skills/Seniority/Work mode/Employment/Salary, edited by the user, passed to Claude as guidance.
- **Matching preferences (existing)**: the user's salary floor/target and preferred work modes/employment; like weights, they are passed to Claude as fit guidance, and changing them invalidates all fits.
- **Enrichment work item**: a pending unit of work (an offer to summarise, a CV to profile, or an offer to match) with the inputs the worker needs to produce it.
- **Enrichment configuration (settings)**: configurable output-size limits (offer-summary length, CV-summary length, max key skills, rationale length) and the worker retry limit, each with a sensible default; editable without code changes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After one worker pass following collection, 100% of available offers that are not in the "failed" state display a summary and a key-skills list; any failed offers are reported separately.
- **SC-002**: Every uploaded readable CV yields a skills + seniority + summary profile after one worker pass; unreadable CVs are flagged without error.
- **SC-003**: With a processed CV present, at least 95% of available offers that are not in the "failed" state display an AI fit score with matched/missing and a rationale after one worker pass, and 0% display a non-AI fallback score.
- **SC-004**: Changing the CV, weights, or matching preferences causes 100% of fit results to refresh (no result shown as current while based on superseded inputs) by the end of the next worker pass.
- **SC-005**: 0 offer/CV records are transmitted from the application backend to any external AI service (all AI work runs under the local worker).
- **SC-006**: Users can see each offer's publish date and sort the feed by it. *(delivered)*
- **SC-007**: At any time the user can see the count of pending items and reduce it to zero via a single manual re-run (given the worker is run).

## Assumptions

- **Engine**: This is a single-user, local-first app. The enrichment "worker" is **Claude Code running under the user's Claude Max plan** — there is no Anthropic API key, no per-token billing, and no AI SDK in the backend. The application owns the work queue and staleness; Claude Code performs the generation and writes results back.
- **Cadence**: Processing is **on-demand first** (the user/worker triggers it). A scheduled worker (e.g. after each scan) is an optional future enhancement, not required for this spec. Enrichment is therefore not guaranteed to be instant within a page load; un-processed items are shown as "pending".
- **CV reading**: Claude reads the **original CV document**; the local text extraction is used only as an input fallback when the document cannot be read directly.
- **Persistence model change**: Storing AI outputs and recomputing on input change **supersedes the prior "fit derived on read, never stored" behaviour** and the constitution v1.1.0 load-bearing decision "CV matching fully local". This accepted change will be reconciled **in the plan via an ADR** (rather than a formal constitution amendment), recording the rationale and the superseded decision.
- **Delivered**: User Story 1 (publish date + "Recently published" sort + card date) is already implemented and deployed.
- **Stack retained**: React (Vite, TypeScript) front end + .NET 10 (ASP.NET Core) back end + PostgreSQL (EF Core, append-only migrations), layered `Domain → Application → Infrastructure → Web`.
- **Implementation decisions already locked (hand-off to `/speckit-plan`)**: an app-owned enrichment queue with a "get pending work" read endpoint and a "write results" endpoint, both local-only; new persisted offer fields for summary + key-skills + an enrichment input hash + timestamp; a new per-offer fit/match record (score, matched, missing, rationale, inputs hash, timestamp); an extended CV profile (skills, seniority, summary) with a pending state; recompute keyed by input hashes (offer content+description for enrichment; offer + profile + weights for fit); un-processed items rendered as "pending"; weight sliders retained and relabelled as "guidance to Claude"; a re-runnable Claude Code worker command/skill plus the one-time backfill; the local document text extractor retained only to gauge CV readability and as an input fallback.
- **NON-GOALS**: calling the paid Anthropic API from the backend; wiring the Claude subscription's OAuth/login token into the app as a backend credential (out of scope, not the subscription's intended use).
