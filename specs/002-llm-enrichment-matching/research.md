# Phase 0 Research: LLM Enrichment & Matching (Claude-as-Worker)

**Feature**: `002-llm-enrichment-matching` | **Date**: 2026-06-29 | **Plan**: [plan.md](./plan.md)

All decisions are grounded in the existing feature-001 codebase (read 2026-06-29). Format per
topic: **Decision / Rationale / Alternatives considered**. The load-bearing constraint behind
every decision: the LLM engine is **Claude Code under the user's Max plan**, never the paid
Anthropic API — the backend imports no AI SDK and makes no outbound AI call (FR-012 / SC-005).

---

## R1 — Claude-as-worker enrichment queue (pull-based, loopback-only)

**Decision**: Add an app-owned, **loopback-only** enrichment protocol under `/api/enrichment`:

- `GET /api/enrichment/pending?limit=N` — returns an **ordered, self-contained** batch of work
  items (`kind` ∈ `cvProfile | offerSummary | offerFit`), each carrying *all* inputs needed to
  produce it, an `inputsHash`, the soft-cap `guidance`, and the current `attempt`, plus a `meta`
  block of counts.
- `POST /api/enrichment/results` — writes a **batch** of results back; each result echoes its
  `inputsHash`, `kind`, `status` (`produced | unreadable | failed`) and a per-kind payload.
- `GET /api/enrichment/status` — pending/failed counts (FR-010/FR-016/SC-007).
- `POST /api/enrichment/rerun` — in-app trigger (FR-009): re-arms `failed` rows / forces a full
  re-run; **does not call AI** — it only resets app state so the next worker pass picks the work up.

The **worker is a re-runnable Claude Code slash command** in this repo (`.claude/commands/enrich.md`,
`/enrich`) that loops: GET pending → produce each output *in-session under the Max plan* (for
`cvProfile` it reads the local PDF directly) → POST results → repeat until `pendingTotal == 0`.

**Rationale**: A pull-based queue the worker drains is the only model compatible with FR-012/SC-005:
the backend is a passive localhost data store + queue; the sole AI engine is the user's own Claude
Code session (the sanctioned worker per spec Assumptions/NON-GOALS — explicitly *not* "the
application backend"). No `@anthropic-ai`/Anthropic package is added; no outbound AI HTTP call
exists ⇒ the backend transmits **0** offer/CV records externally (SC-005). Self-contained work items
let the worker run as a trivial loop with no extra round-trips. The design reuses existing patterns
verbatim: endpoint-group + `Result→HTTP` mapping (`ScanEndpoints`/`ResultExtensions`) and the
`AppSettings` jsonb singleton for guidance/retry config. The effective-profile input is **reworked**
(not reused): `ProfileService.BuildEffectiveProfileAsync` today composes a keyword `CandidateProfile`
via `CandidateProfileMerger`/`DerivedProfile` — both removed (ADR-2) — so it is rewritten to compose
the effective profile from the new AI `CvProfile` + preferences (see R5), keeping Domain
framework-free.

**Alternatives considered**:
- *Backend calls the Anthropic API behind an `ILlmClient` port* — **rejected**: violates FR-012/SC-005
  and the spec's locked engine.
- *Leased / visibility-timeout work queue (claim/ack, dead-letter)* — **rejected**: over-engineered
  for one user + one worker; the `inputsHash` optimistic-concurrency token already makes
  duplicate/concurrent writes safe (Principle X).
- *Backend spawns Claude Code as a subprocess* — **rejected**: the backend must not drive the user's
  interactive Max session; the in-app trigger only resets state and surfaces the pending count.
- *WebSocket / file-drop queue* — **rejected**: plain loopback HTTP matches the existing typed API
  client and contract conventions.
- *Per-resource write-back endpoints (`POST /api/enrichment/cv/{id}/result`, …)* — **rejected** in
  favor of one batched `POST /results` so the worker does one round-trip per batch.

---

## R2 — Recompute / staleness keying via input hashes (FR-006/FR-007)

**Decision**: Add a framework-free `Domain/Enrichment` namespace with **input-hash composers** that
reuse the existing `ContentFingerprint` SHA-256-over-canonical-JSON style, and store the resulting
`inputs_hash` next to every persisted Claude output. Each output's hash:

| Output | Hash inputs |
|---|---|
| Offer summary + key skills | offer content **including description** (Major-tier `ContentFingerprint.Hash` + description + company + location) |
| CV profile | the CV **document bytes** (SHA-256 of the file) |
| Effective-profile version | canonical produced profile (skills + seniority + summary) + preferences |
| Offer fit | `offerEnrichmentInputsHash` + `effectiveProfileVersion` + `weightsHash` |

> **Preferences feed the fit (intentional extension of FR-007).** The `effectiveProfileVersion`
> includes `ProfilePreferences` (salary floor/target, preferred work modes/employment) because the
> worker uses them as part of the fit (work-mode/salary alignment, US4). So a **preferences change
> invalidates all fits** alongside the spec-named "CV or weights" (FR-007/SC-004). This is a
> deliberate widening of the spec's fit inputs, recorded here.
>
> **Eager hooks dominate over the version key.** The eager `UPDATE all fits → Pending` fires whenever
> a profile is (re)produced or a CV is replaced (a re-upload mints a new `CvId`; the LLM is
> non-deterministic), so the produced-version-vs-bytes choice mainly governs the *write guard*, not
> whether fits rerun. The "profile produced" invalidation is a **no-op when the new
> `effectiveProfileVersion` equals the old** (identical re-produce), avoiding a needless ~180-fit rerun.

**Staleness uses a hybrid model — eager `state` writes + `inputs_hash` guard**:

1. **Eager**: known input changes write `state = Pending` immediately — offer content change (scan
   path), weights change, CV upload/profile change, preferences change. This makes FR-007 literal
   ("MUST invalidate (mark 'pending')") and the pending **count** (FR-010) a trivial indexed
   `COUNT(state='Pending')`.
2. **Guard**: on write-back the server **recomputes the current hash from live inputs** (offer
   content / produced-profile version / current weights) and rejects any result whose echoed hash ≠
   that freshly-recomputed value (`stale`, never stored). It does **not** compare against the stored
   column — eager invalidation leaves the stored hash at its last-produced value, so a stored-vs-echoed
   comparison would wrongly accept a result computed against superseded inputs (SC-004 violation). The
   stored `inputs_hash` serves only the read-path drift backstop: a `Produced` row whose stored hash ≠
   the recomputed current hash is rendered as pending, so a value based on superseded inputs is never
   shown even if an eager hook is ever missed.

The prior produced value stays in its column (with its old hash) purely as the freshness-comparison
value FR-007 permits, and is **never displayed** while the row is not `Produced`-with-matching-hash.

**Rationale**: FR-006/FR-007 demand per-output recompute keyed to each output's own inputs. The
existing `ContentFingerprint` deliberately **excludes** the description (Minor tier), so it cannot be
the summary hash alone — composing it with the description yields the description-inclusive hash
FR-006 requires while guaranteeing any Major-tier change still propagates. The hybrid gives the best
of both research directions: literal "mark pending" + cheap counts (eager) *and* drift-proof
correctness (hash guard). On a CV/weights change all fits flip to pending **immediately** (a single
`UPDATE`), satisfying SC-004 "100% refresh" with no stale fit ever shown.

**Alternatives considered**:
- *Reuse `ContentFingerprint` directly for the summary hash* — **rejected**: excludes the
  description ⇒ a description-only change would never re-summarise (violates FR-006).
- *Purely-derived staleness (no `state` column; compute pending at read time)* — **rejected**: makes
  the pending **count** (FR-010) a per-offer computation and reads "MUST invalidate (mark pending)"
  too loosely. (Kept its insight as the `inputs_hash` read-guard.)
- *Key the fit's profile component on the CV **document bytes*** — **rejected** as the *write guard*
  (a byte-different-but-identical re-read would needlessly rerun ~180 fits); the **produced
  effective-profile version** is FR-006-optimal. Immediate invalidation on CV change is still
  achieved by the eager `UPDATE` above, not by the hash.
- *Drive staleness off `OfferVersion` rows* — **rejected**: versions are Major-tier-only history and
  miss Minor description changes and CV/weights inputs.

**Required minimal scan-flow touch**: today the scan's *Unchanged* branch (`ScanOrchestrator`) calls
`RegisterSighting` and never persists a description-only change, so `OfferEnrichmentInputs` would
never see a new description. Add a **silent Minor-content refresh** (`Offer.RefreshMinorContent`)
that updates the denormalized description/company/location **without** touching `HasUnseenUpdate`,
the `Updated` tally, or appending an `OfferVersion`/event — preserving every user-visible feature-001
semantic (FR-013) while letting enrichment see the new description (FR-006).

---

## R3 — Persisting structured LLM outputs (tables, state machine, append-only migration)

**Decision**: Two new **1:1 satellite tables** keyed by `OfferId` (PK == FK → `offers`, cascade),
mirroring the existing `offer_version`/`offer_observation`/`offer_event` pattern; **extend**
`candidate_cv` with AI-profile columns; **extend** `app_settings` with one `enrichment` jsonb column.
All lists (`key_skills`, `matched`, `missing`, `profile_skills`) stored as **jsonb string arrays** via
the existing `HasJsonbListConversion<string>()`. **No new wrapped IDs.** Deliver in **one** new
append-only migration `LlmEnrichment` (never edit prior migrations — Principle IX).

- `offer_enrichment`: `state` (`Pending|Produced|Failed`), `attempts`, `summary`, `key_skills` jsonb,
  `inputs_hash`, `produced_at`, `last_error`. Index on `state`.
- `offer_fit`: `state`, `attempts`, `score` (0..100), `matched` jsonb, `missing` jsonb, `rationale`,
  `inputs_hash`, `produced_at`, `last_error`. Index on `state`. **Supersedes** read-time scoring.
- `candidate_cv` (+columns): `profile_state` (`Pending|Produced|Unreadable|Failed`),
  `profile_attempts`, `profile_skills` jsonb, `profile_seniority`, `profile_summary`,
  `enrichment_input_hash`, `profile_produced_at`. **Retain** `is_readable`/`extracted_at` as the
  PdfPig readability gauge. The legacy `derived_profile` column + its `CandidateProfile` mapping are
  **dropped** in the same migration (ADR-2; a recomputable keyword cache — no user data).
- `app_settings` (+column): `enrichment` jsonb (`EnrichmentSettings`), default seeded.

**Rationale**: Separate satellites keep feature-001's collection **behavior** unchanged (FR-013) —
the worker writes only the satellites; the scan path is additively extended to **create/invalidate**
satellite rows + a silent Minor refresh (ADR-3), but a satellite write never rolls back the `offers`
upsert, so the collection result is unaffected. Each satellite has a *distinct* invalidation trigger (enrichment ← offer
content; fit ← offer ∨ profile ∨ weights), which maps naturally to two rows. jsonb arrays match
every existing collection (`offers.required_skills`, `role_group.member_offer_ids`,
`candidate_cv.derived_profile`); at single-user scale relational skill rows buy nothing (Principle X).
A virtual queue (`WHERE state='Pending'`) is a single source of truth for pending work (no second
table to drift). Reusing `OfferId`/`CvId` avoids id-struct + converter + `ConfigureConventions`
boilerplate (Principle II/X). Persisting fit is exactly the spec's ADR-bound accepted change
(supersedes "fit derived on read, never stored").

**Alternatives considered**:
- *Enrichment columns on the `offers` row (owned VO)* — **rejected**: mixes the worker write-path
  into the scan-owned aggregate (FR-013 risk) and bloats the root with ~14 nullable AI columns; fit's
  inputs (profile + weights) are not offer concerns.
- *A dedicated `enrichment_work_queue` table* — **rejected**: redundant with the `state` columns;
  two stores of "what's pending" drift. The state machine **is** the queue.
- *Child rows for skills* — **rejected**: no existing code stores skills relationally.
- *New `OfferEnrichmentId`/`OfferFitId`/`CvProfileId` wrapped IDs* — **rejected**: these are 1:1
  mutable caches, not independently-referenced aggregates.
- *Append-only fit history (like `offer_version`)* — **rejected**: the spec keeps only the current
  result and discards superseded values from display; a 1:1 mutable cache is correct.

**State machines**:

```
offer_enrichment / offer_fit  {Pending, Produced, Failed}
  create ............ → Pending(attempts 0)        (offer created; FR-014 backfill)
  Pending → Produced  (valid output: store payload + inputs_hash + produced_at; attempts→0)
  Pending → Pending   (invalid/empty/error: attempts++ while < RetryLimit)
  Pending → Failed    (attempts ≥ RetryLimit; terminal; last_error set)   [FR-015]
  Produced → Pending  (Invalidate: inputs hash changed; attempts→0; old payload kept, not shown) [FR-007]
  Failed → Pending    (Rearm: ONLY inputs change OR manual re-run)        [FR-009/FR-015]

candidate_cv  {Pending, Produced, Unreadable, Failed}
  upload → Pending    (PdfPig still runs to set is_readable as a worker hint)
  Pending → Produced  (skills + seniority + summary)
  Pending → Unreadable(worker verdict: document uninterpretable; distinct terminal; no retries) [SC-002/US3-AC3]
  Pending → Pending/Failed (bounded retry as above)
  Failed/Unreadable → Pending (manual re-run; replacing a CV = new CvId = fresh Pending row)
```

---

## R4 — CV delivery to the worker, readability gauge vs. "unreadable", configurable limits

**Decision**: The `cvProfile` work item carries the **absolute local file path** to the stored PDF
(primary input — Claude reads the original on disk), plus the retained PdfPig `documentReadable`
gauge and an **inline `fallbackText`** produced on demand by `ICvTextExtractor` (used only if the
worker cannot read the original directly — FR-003). The `fallbackText` is **not persisted** (no new
PII-at-rest column) — it is regenerated at fetch time. **"Unreadable"** is an *authoritative worker
verdict* recorded as a distinct terminal `CvProcessingState` (no retries, counted as neither pending
nor failed). The configurable output caps + retry limit live in a new `EnrichmentSettings` VO on the
`app_settings` jsonb singleton, surfaced via `GET/PUT /api/settings/enrichment`, mirroring the
existing weights/normalization pattern.

**Rationale**: Delivering a **path** (a reference, not bytes) is the most privacy-preserving option
and honors FR-003 verbatim — the bytes never leave disk, the only transport is localhost, and the
local worker opens the file itself (FR-012/SC-005, Principle IV). Keeping PdfPig strictly as a
readability gauge + fallback-text producer matches the spec Assumption and FR-005 (the keyword
profiler is retired). Modeling "unreadable" as a *content* verdict (decided on first inspection, no
retries) distinct from "failed" (a *process* verdict after the bounded retry limit) satisfies
FR-016/SC-002 and the Key-Entities state list. Storing limits on `AppSettings` keeps FR-018 "editable
without code changes" true via the established settings home (Principle X).

**Alternatives considered**:
- *Deliver extracted text only* — **rejected**: violates FR-003; loses layout/visual signal; image-
  only CVs become undeliverable.
- *Deliver CV bytes inline over HTTP* — **rejected**: pointless on one machine, larger, less private.
- *Gate purely on PdfPig (mark unreadable at upload, never enqueue)* — **rejected**: Claude vision may
  read scanned/image-only PDFs PdfPig cannot, so an upfront gate would wrongly deny enrichment; PdfPig
  stays advisory.
- *Persist the extracted text in a DB column* — **rejected** to minimise PII at rest; regenerate at
  fetch (single-user, on-demand cadence makes this cheap).
- *Store limits in `appsettings.json` + `IOptions<T>`* — **rejected**: that pattern is static infra
  config, not runtime-editable; FR-018 needs user-editable-without-code (DB-backed `AppSettings`).

**`EnrichmentSettings`** (defaults, FR-018): `OfferSummaryMaxWords=60`, `CvSummaryMaxWords=60`,
`MaxKeySkills=10`, `FitRationaleMaxWords=30`, `RetryLimit=3`. Applied as **loose** validation on
write-back — only hard violations are rejected (empty summary, missing skills array, score outside
0..100); soft word/skill-count overage is accepted (optionally trimmed).

---

## R5 — Constitution reconciliation ADR; fate of the non-AI scorer; preserving feature-001

**Decision**: Reconcile in *this plan* with a single **accepted ADR** — no constitution amendment,
no semver bump. Persisting Claude-derived outputs and running generation in the local Claude Code
worker **supersedes — within feature 002 only** — the CLAUDE.md load-bearing decision *"CV matching
fully local"* and the feature-001 rule *"fit derived on read, never stored (FR-035)"*, while fully
honoring the NON-NEGOTIABLE **Principle IV** (the backend calls no external AI API and transmits no
offer/CV data off-machine). See [plan.md → ADR-1](./plan.md).

**Remove** (per FR-005 — no non-AI fallback): `Domain/Matching/Scorer.Score`, `FitScore`,
`FitBreakdown`, `ScoringInput`; `CvProfileBuilder`, `ICvProfileBuilder`, `SkillCatalog`,
`SkillCatalogLoader`, `skill-catalog.json`, `SkillRef`, **`CandidateProfile`**, `CandidateProfileMerger`
— which also removes the **only FuzzySharp consumer**, so the **FuzzySharp** package (+ version + the
`skill-catalog.json` copy directive) is dropped, along with the `SkillCatalog`/`ICvProfileBuilder` DI
registrations and the `derived_profile` column/mapping. **Rework consumers**: `ProfileService`
(`BuildEffectiveProfileAsync`/`GetAsync`/`ProfileView`) and `CvEndpoints.ToCvDto` must source from the
new AI `CvProfile`; delete `ScorerTests.cs`; rewrite `CvExtractionTests.cs`.
**Retain**: **PdfPig** (`PdfPigCvTextExtractor`/`ICvTextExtractor`) as the readability gauge + input
fallback; the pure ranking helpers `Scorer.CombinedRank`/`DegradedRank` (they only **order** the
feed and never produce a *displayed* fit). The new **`CvProfile`** VO (`Skills`, `Seniority`,
`Summary`) — **not** the removed `CandidateProfile` — is the worker's profile write-back target.

**Rationale**: The constitution's own External-services clause already anticipates "any LLM
integration … must satisfy Principle IV before it ships", so an ADR demonstrating Principle-IV
compliance is the correct reconciliation (Principle XI: superseded by a new note, not amended). The
two superseded statements are a CLAUDE.md *decision* and a feature-001 *FR/code rule* — neither a
numbered Principle — so an amendment would overstate the change. FR-005 makes Claude the **sole**
source with unproduced items "pending" and forbids a non-AI fallback, so keeping `Scorer.Score`/
`CvProfileBuilder` alive would be exactly the forbidden fallback. FR-013 + Principle VI make the
untouched feature-001 test suite the **regression contract** proving collection/dedup/availability/
role-grouping/salary/export are unchanged.

**Feed ordering note**: the default feed rank previously combined salary + the on-read fit. Now the
fit comes from `offer_fit.score` (AI), and the pending mix is the *normal* state until `/enrich` runs,
so a single `OrderBy(rank)` mixing `CombinedRank` (≈0.7·fit + 0.3·salary) with `DegradedRank`
(≈0.6·salary + 0.4·recency) is incoherent (a high-salary unscored offer would outrank a good produced
match). The default sort is therefore **two-tier**: **(1)** offers with a current *produced* fit, by
`CombinedRank` (AI score); then **(2)** pending/absent-fit offers, by `DegradedRank`. `CombinedRank`/
`DegradedRank` are retained for **ordering only** — no fabricated fit is ever shown (FR-005). Other
sorts (`published`, `salary`, `fit`) are unchanged.

**Alternatives considered**:
- *Amend the constitution (drop "CV matching fully local")* — **rejected**: the spec directs an ADR;
  Principle IV is not weakened, so an amendment overstates the change.
- *Keep `Scorer.Score`/`CvProfileBuilder` as a fallback until the worker runs* — **rejected**:
  violates FR-005/SC-003 ("0% display a non-AI fallback score"); the spec mandates "pending".
- *Call the paid API / wire the Max-plan OAuth token into the backend* — **rejected**: violates
  FR-012/SC-005, the spec NON-GOALS, and Principle IV.
- *Delete all of `Domain/Matching` (incl. `CombinedRank`/`DegradedRank`, `CandidateProfile`)* —
  **rejected**: the ranking helpers only order the feed and the profile shape is the write-back
  target; deleting them needlessly breaks feed ordering and the no-CV degraded path.

---

## Resolved unknowns

All Technical-Context unknowns are resolved; **no `NEEDS CLARIFICATION` remain**:

- Worker integration mechanism → R1 (loopback pull queue + `/enrich` slash command).
- Recompute/staleness keying → R2 (input-hash composers + eager-state/hash-guard hybrid).
- Structured-output persistence + state machine → R3 (two satellites + CV/settings columns, one
  append-only migration).
- CV reading + configurable limits → R4 (path delivery, distinct "unreadable", `EnrichmentSettings`).
- Constitution reconciliation + non-AI code fate → R5 (accepted ADR; remove scorer/keyword profiler,
  drop FuzzySharp, retain PdfPig + ranking helpers).
