# Feature Specification: Application Affinity Metric & Offer Detail Body

**Feature Branch**: `006-application-affinity-metric` (spec directory; no git branch created — repo has no `before_specify` hook)

**Created**: 2026-07-01

**Status**: Draft

**Input**: User description: "I want to have another metric not only \"fit\". It should analyze my previous applications and match other offers against the ones I applied. Job offers should also have body - description, requirements which I might display after clicking on job offer. Make sure none of the current data is lost."

## Clarifications

### Session 2026-07-01

- Q: How should the affinity metric be computed? → A: **Local AI worker** — produced by the existing feature-002 Claude-Code worker (the same pattern as "fit"), stored as a per-offer signal with pending/produced/failed states and a rationale; the backend makes no external AI call (Principle IV). There is **no** non-AI/deterministic fallback (consistent with the 002 decision).
- Q: Which applied offers form the affinity basis, and how are they weighted? → A: **All applied offers, weighted equally** — every offer marked applied is an equal positive exemplar; feature-005 outcome/stage does not weight the basis. The act of applying is the targeting signal.
- Q: When is each offer's description/requirements body fetched and stored? → A: **Eagerly during a scan** — the full body is fetched and stored as each offer is collected, so it is available in-app immediately (this changes the prior "detail fetched only on demand" behaviour). It stays Minor-tier display content (never affects new-vs-seen, the content fingerprint, or the "updated" flag), and a per-offer fetch failure/block falls back to the "description not available" state without failing the scan.
- Q: Below how many applied offers should affinity show "not enough history"? → A: **Fewer than 3** — affinity requires at least 3 applied offers; below that it shows the insufficient-history state instead of a score.

## User Scenarios & Testing *(mandatory)*

<!--
  Prioritised as independently testable slices. US1 and US2 are each a standalone MVP: US1 adds the
  new "affinity" match signal beside the existing "fit"; US2 lets the user read a job's full body inside
  the app. Neither depends on the other. US3 layers interpretability + freshness onto US1. Throughout,
  the request's explicit "no data lost" constraint is a hard, cross-cutting requirement, not an afterthought.
-->

### User Story 1 - A second match signal: how much an offer resembles the roles I've applied to (Priority: P1)

As a job seeker, I already see a **fit** score (how well an offer matches my CV). I also want a **second, distinct metric** — call it **affinity** — that looks at the offers I have **already applied to** and tells me how closely a given offer resembles them, so I can spot roles "like the ones I've been going for" even when a raw CV-fit number doesn't capture my actual targeting.

**Why this priority**: This is the headline of the request ("another metric, not only fit … analyze my previous applications and match other offers against the ones I applied"). It is the smallest slice that delivers the core new value: a per-offer signal learned from my own application behaviour, shown alongside — never replacing — fit.

**Independent Test**: Apply to several offers of a recognisable kind (e.g. senior back-end .NET remote roles), trigger affinity computation, then confirm that other offers of that same kind receive a high affinity value while clearly unrelated offers receive a low one — and that the existing fit score is still shown separately and unchanged.

**Acceptance Scenarios**:

1. **Given** I have applied to several offers, **When** I view the feed, **Then** each offer shows an **affinity** value that is visually distinct from its fit score, and the two are never conflated into one number.
2. **Given** a group of offers similar to ones I have applied to and a group that is unrelated, **When** I compare their affinity, **Then** the similar offers score higher than the unrelated ones (the metric reflects what I have actually been applying to).
3. **Given** offers with affinity values, **When** I want to focus on "roles like the ones I've pursued", **Then** I can prioritise/sort the feed by affinity in addition to the existing ordering.
4. **Given** the affinity metric exists, **When** I look at any offer, **Then** its fit score, triage disposition, applied flag, and application pipeline stage are all unchanged — affinity is an added signal layered on top, not a replacement for any of them.

---

### User Story 2 - Read the full job description and requirements inside the app (Priority: P1)

As a job seeker triaging many offers, I want to **click an offer and read its full body — the description and requirements — inside the app**, so I can decide whether to pursue it without opening the external job-board page for every single offer.

**Why this priority**: This is the second explicit ask ("job offers should also have body — description, requirements which I might display after clicking on job offer"). It is independently valuable and independently testable, and it is the natural place a richer offer view lives. Today the app only links out to the external posting; the full body is not shown in-app.

**Independent Test**: Open an offer within the app and confirm its full description and requirements are shown in a readable form (alongside the facts already displayed and a link to the original posting); open an offer whose body was not captured and confirm a clear "description not available" state plus the external link, rather than a blank or broken view.

**Acceptance Scenarios**:

1. **Given** an offer whose body has been captured, **When** I open it in the app, **Then** I see its full description and requirements rendered readably, together with the offer's existing facts (title, company, location, salary, work mode, seniority, skills, summary, fit, affinity) and a link to the original external posting.
2. **Given** an offer whose body could not be captured (e.g. it was delisted before it was fetched), **When** I open it, **Then** I see a clear "description not available" message and can still open the original posting externally — never a blank or broken screen.
3. **Given** an offer body that originates from an external site, **When** it is displayed, **Then** it is shown safely (untrusted markup cannot execute) and remains legible.
4. **Given** the in-app detail view exists, **When** offers are collected, deduplicated, versioned, and flagged new/updated, **Then** that behaviour is unchanged — the stored body is display-only content and does not affect new-vs-seen, fingerprinting, or the "updated" badge.

---

### User Story 3 - Affinity that explains itself and stays current (Priority: P2)

As a job seeker, I want the affinity metric to be **interpretable and up to date** — a short reason for the score, a sensible state when I have too few applications to compare against, and automatic refresh as my application history grows — so I can trust it rather than see an opaque, stale number.

**Why this priority**: A bare score is useful; a score I understand and can rely on is far more useful. This depends on US1 existing but adds the interpretability, cold-start handling, and freshness that make the metric trustworthy. It is P2 because US1 already delivers the core value.

**Independent Test**: With zero applications, confirm affinity shows a "not enough application history yet" state rather than a number; after applying to several offers and recomputing, confirm scores appear with a short rationale; then change which offers I have applied to, recompute, and confirm the scores update accordingly with no duplicate or stale values.

**Acceptance Scenarios**:

1. **Given** I have not applied to anything yet (or too few offers to compare), **When** I look at affinity, **Then** it clearly communicates "not enough application history yet" rather than showing a misleading score.
2. **Given** an offer with an affinity value, **When** I look at it, **Then** a short rationale explains what it resembles (e.g. which applied roles / attributes it is close to), so the number is interpretable.
3. **Given** I apply to a new offer (or un-apply one), **When** affinity is recomputed, **Then** the scores reflect my updated application history, and recomputation does not produce duplicate or conflicting values for an offer.
4. **Given** affinity cannot be produced for a particular offer, **When** I view that offer, **Then** it shows a pending/unavailable affinity state (never a fabricated fallback), and the rest of the offer is unaffected — exactly as fit already behaves.

### Edge Cases

- **No / very few prior applications (cold start)**: with **fewer than 3** applied offers, affinity shows an explicit "insufficient application history" state, never a fabricated or misleadingly-low score.
- **Application history changes**: applying to a new offer or un-applying one causes affinity to recompute; stale scores are refreshed and no offer ends up with duplicate/conflicting affinity values.
- **An applied offer later becomes delisted**: it still counts as part of the affinity comparison basis (its captured attributes persist), mirroring the tailored-CV / application persistence rules for delisted offers.
- **Offer body unavailable**: a body that was never captured — a fetch failure, a source block/challenge during the scan, or an offer delisted before it was scanned — yields a clear "description not available" state plus the external link, and never fails the scan or shows a blank/broken view.
- **Untrusted markup in an offer body**: the body is sanitised before display so injected content cannot execute; the offer remains legible.
- **Very large offer body**: rendered readably without losing the requirements section and without degrading feed performance.
- **Existing offers from before this feature**: appear in the feed as before; they receive an affinity value once enough application history exists and it is computed, and their body is shown once captured — absence of either shows as pending/insufficient, never blocking the feed.
- **Affinity vs fit confusion**: the two metrics are always presented as distinct signals; a user is never shown a single blended number that hides which is which.
- **Backup / restore & export**: affinity data and stored offer bodies are captured by the existing on-demand backup and the data export, and are restored intact alongside offers, fit, enrichment, applications, tailored CVs, and CVs.
- **No data lost on upgrade**: every existing offer, fit score, enrichment result, application (feature 005), tailored CV (feature 004), CV, setting, and history entry survives the change intact; the migration is additive/append-only and never drops, overwrites, or rewrites existing records.

## Requirements *(mandatory)*

### Functional Requirements

#### The affinity metric (a second match signal)

- **FR-001**: The system MUST provide a **second per-offer match metric ("affinity")**, distinct from the existing CV **"fit"** metric, that reflects how closely an offer resembles the offers the user has applied to. Both metrics MUST coexist; the fit metric MUST remain unchanged.
- **FR-002**: The affinity signal MUST be derived from the user's **own application history** — the offers the user has marked applied — by comparing each candidate offer against that history (the request's "match other offers against the ones I applied").
- **FR-003**: Each offer MUST present its affinity value in a way that is **visually and conceptually distinct** from its fit value; the two MUST NOT be merged into a single number that hides which is which.
- **FR-004**: Users MUST be able to **prioritise/sort** offers by affinity, in addition to the existing feed ordering, so the roles most like the ones they have pursued can be surfaced first.
- **FR-005**: Each affinity value MUST be accompanied by a short **rationale** (e.g. which applied roles or attributes the offer resembles), so the metric is interpretable rather than an opaque number.
- **FR-006**: When the user has **too few prior applications** to form a meaningful comparison — specifically **fewer than 3 applied offers** — the system MUST show a clear "insufficient application history" state for affinity rather than a misleading score.
- **FR-007**: The affinity metric MUST **recompute/refresh** whenever any of its inputs change materially: **(a)** the user's application history changes — a new application or an un-apply (an application's outcome or stage change does NOT affect affinity, since the basis is outcome-agnostic per the clarified decision) — which refreshes affinity for **all** offers; and **(b)** a candidate offer's own content changes, which refreshes **that offer's** affinity (the candidate offer is itself part of the affinity input). Recomputation MUST be **idempotent by its inputs**: an offer never ends up with duplicate or conflicting affinity values.
- **FR-008**: Affinity MUST be **produced by the local Claude-Code worker** (the same worker pattern as fit, feature 002), stored as a per-offer signal with pending / produced / failed states; the backend MUST make **no external AI call** and no offer or application data is sent to any external service (Constitution Principle IV). There MUST be **no** non-AI/deterministic fallback score — an un-produced affinity shows a pending/failed state (see FR-009).
- **FR-009**: The affinity metric MUST **degrade gracefully** — if a value cannot be produced for an offer, that offer MUST show a pending/unavailable affinity state (never a fabricated fallback), exactly as the fit metric behaves today, and the rest of the offer MUST be unaffected.
- **FR-010**: Affinity MUST be **orthogonal** to, and MUST NOT alter, the existing fit metric, the triage disposition (new / viewed / interested / dismissed), the applied flag, or the application pipeline and outcomes (feature 005). It is an added signal only.

#### The offer body (description & requirements)

- **FR-011**: The system MUST capture and persist each offer's **full body** — its description and requirements text as published at the source — by **fetching it during collection** (for offers that need it — newly seen, changed, or not yet captured — so unchanged offers are not re-fetched every scan; offers present before this feature fill in their body on their next scan sighting), so the full posting is available inside the app. An available offer SHOULD carry its body once a fetch succeeds; a persistent fetch failure or source block leaves that offer in the FR-014 "description not available" state until a later scan succeeds.
- **FR-012**: Users MUST be able to **open an offer within the app** and read its full description and requirements in a readable format, without being forced out to the external site.
- **FR-013**: The in-app offer detail MUST also present the offer's existing structured facts (title, company, location, salary and normalised salary, work mode, seniority, required/nice-to-have skills, AI summary, fit, affinity) in one place, and MUST retain a link to the original external posting.
- **FR-014**: When an offer's body is **not available** (never captured, or the offer was delisted before it could be fetched), the detail view MUST show a clear "description not available" state and still offer the external link — never a blank or broken view.
- **FR-015**: The offer body MUST be **sanitised for safe display** before rendering (it originates from an untrusted external site), so displaying it cannot execute injected/active content.
- **FR-016**: Fetching and storing the offer body during collection MUST NOT change existing **new-vs-seen** semantics: deduplication, the "updated" flag, content fingerprinting, salary normalisation, tailored-CV generation, and application tracking MUST all continue unchanged. The body is display-only, Minor-tier content and MUST NOT enter the identity/"updated" fingerprint. **Enrichment, fit, and affinity behave exactly as today**: because the offer description is (by the existing rule) part of an offer's AI summary/fit/affinity input, newly capturing a body legitimately re-computes **that offer's own** summary, fit, and affinity (the same effect an edited description already has) — the mechanism is unchanged, though a **one-time recomputation** of offers that gain a body is expected on the first scan after this feature ships. A body capture never alters **another** offer's metrics (the affinity basis is keyed on Major-tier fingerprints, which exclude the Minor-tier description). Body fetching MUST be **resilient** — a per-offer fetch failure or a source block/challenge MUST NOT fail the scan or drop the offer; the affected offer falls back to the "description not available" state (FR-014).

#### Data preservation, portability, and privacy (cross-cutting)

- **FR-017**: The feature MUST preserve **all existing data with no loss**: every existing offer, fit score, enrichment result, application (feature 005), tailored CV (feature 004), CV, setting, and history entry MUST survive intact. Any schema change MUST be **additive/append-only** (Constitution Principle IX) — no existing record is dropped, overwritten, or rewritten — and any backfill MUST be idempotent and additive.
- **FR-018**: The **full** new affinity data and the stored offer bodies MUST be included in the existing on-demand **backup and restore** scope (feature 003), so they are fully recoverable alongside everything else. The human-readable **data export** (feature 001) MUST additionally carry the captured **offer body** (a captured fact) and the current **affinity score** (a portable summary of the metric); the affinity rationale/`resembles` — like the existing fit score's breakdown — are treated as recomputable derived detail and need not appear in the export (they remain fully recoverable via backup/restore). Backup/restore is the recoverability guarantee for the derived affinity cache; export is the portability path for captured facts plus the headline score.
- **FR-019**: All new data derived from the user's behaviour (affinity, which is learned from application history) and all stored offer bodies MUST stay **local** to the user's machine and MUST NOT be committed to source control (Constitution Principle IV).

### Key Entities *(include if feature involves data)*

- **Affinity signal (per offer)**: a derived, per-offer measure of how closely an offer resembles the user's applied-to offers — a value, a short rationale (what it resembles), a production state (e.g. pending / produced / failed / insufficient-history), and the timestamp/basis it was computed against. **Produced by the local worker** (like fit), one per offer, recomputed when its inputs change. Distinct from, and coexisting with, the CV **fit** signal.
- **Application-history basis**: the set of the user's applied-to offers used as the comparison basis for affinity (each contributing its captured attributes — title, skills, seniority, work mode, salary, etc.). This is the "ones I applied" side of the comparison.
- **Offer body / description**: the full free-text description and requirements of an offer as published at the source, captured for in-app display. It is Minor-tier, display-only content (not part of the identity/"updated" fingerprint) and is sanitised for safe rendering.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For any offer, the user can see **two distinct match signals** — CV fit and application-affinity — without confusing one for the other.
- **SC-002**: After applying to several similar roles and recomputing, offers resembling those roles rank **measurably higher** on affinity than clearly unrelated offers (the metric reflects the user's revealed targeting).
- **SC-003**: With **fewer than 3** prior applications, affinity clearly communicates **"not enough application history yet"** rather than showing a misleading number, in 100% of such cases.
- **SC-004**: For any offer whose body has been captured, the user can read its **full description and requirements inside the app** without leaving for the external site; for offers whose body is unavailable, a clear unavailable state plus the external link is always shown.
- **SC-005**: **100%** of existing data — offers, fit, enrichment, applications, tailored CVs, CVs, settings, and history — is intact after this feature is added; **zero** records are lost or altered (the explicit "no data lost" requirement).
- **SC-006**: The full affinity data and stored offer bodies survive a **backup → restore** cycle intact; and the data export carries each offer's captured body and current affinity score.
- **SC-007**: **Zero** offer or application data is transmitted to any external service, and **zero** personal signal data (affinity, offer bodies) is committed to source control (Constitution Principle IV).
- **SC-008**: Existing behaviour — feed, enrichment, fit, tailored CV, applications, export, and backup/restore — continues to work **unchanged** after this feature is added.
- **SC-009**: Affinity **refreshes** to reflect the user's current application history (e.g. after applying to a new offer and recomputing, the scores update) with no duplicate or stale values.

## Assumptions

- **Affinity sits beside fit, not instead of it**: fit measures CV ↔ offer; affinity measures applied-history ↔ offer. The two are separate signals shown together; the existing fit metric is untouched.
- **Positive basis = applied offers, weighted equally** *(clarified 2026-07-01)*: the comparison basis is the offers the user has marked applied, and **all** applied offers count as positive exemplars weighted equally — matching the literal request ("the ones I applied"). Feature-005 **outcome/stage progress does not weight the basis** (an outcome-agnostic signal); consequently an outcome or stage change does not trigger affinity recomputation (only apply/un-apply does — FR-007).
- **Dismissed offers are not used as a negative signal** in this version — the request is about matching to applied offers only (negative-signal learning is a non-goal).
- **Compute locality — local worker (Principle IV)** *(clarified 2026-07-01)*: affinity is **produced by the local Claude-Code worker**, the same pattern as fit (feature 002); the backend makes no external AI call and nothing is sent to an external service. There is **no** non-AI/deterministic fallback — an un-produced affinity shows a pending/failed state, exactly as fit does.
- **Affinity is presented like fit**: a bounded value with a short rationale and pending / failed / insufficient-history states, so the UI and the user's mental model mirror the existing fit metric (matched/missing-style interpretability).
- **Offer body is captured eagerly during a scan** *(clarified 2026-07-01)*: the full description/requirements body is fetched and stored as each offer is collected, so it is available in-app immediately. This is a **deliberate change** to the prior architecture note that offer detail "is fetched only on demand (not during scan)". The body remains Minor-tier display content — it does not affect new-vs-seen or the content fingerprint — and per-offer fetch failures/blocks are tolerated (the offer still collects; its body shows "not available"). This adds source requests per scan, so body fetching reuses the existing 403/challenge handling and must not escalate a per-offer failure into a scan failure.
- **Delisted offers**: an offer that is no longer available still contributes to the affinity basis via its previously-captured attributes; if its body was never captured, the detail view shows "description not available" plus the external link. A previously-captured body remains viewable.
- **No data loss / additive migration (Principle IX)**: schema changes are append-only; existing offers, fit, enrichment, applications (005), tailored CVs (004), CVs, and settings are preserved unchanged; new fields/tables are added alongside; any backfill is idempotent and additive, mirroring the feature-003/005 backfill pattern (run at startup and on older-restore).
- **Backup/restore & export reuse**: the new affinity data and stored bodies join the existing feature-003 backup/restore scope (added to the guarded table list) and the feature-001 export, rather than introducing parallel recovery mechanisms.
- **Stack retained**: local-first, single-user web app — React (Vite, TypeScript) front end + .NET 10 (ASP.NET Core) back end + PostgreSQL (EF Core, **append-only** migrations), layered `Domain → Application → Infrastructure → Web`. Exactly the changes needed for this feature; all existing features preserved unchanged.
- **NON-GOALS**: replacing or changing the fit metric; multi-user or sharing; sending any offer/application data to an external service; learning from dismissed/negative signals; automated recommendations, alerts, or ranking changes beyond exposing the affinity score and letting the user sort by it; matching against jobs that were never in the tracked feed; analytics/reporting dashboards over affinity.
