# Data Model: LLM Enrichment & Matching (Claude-as-Worker)

**Feature**: `002-llm-enrichment-matching` | **Date**: 2026-06-29 | **Plan**: [plan.md](./plan.md)

This feature **adds** AI-derived outputs (offer summary/key-skills, offer fit, CV profile) and the
worker-queue state that governs them. It does **not** alter feature-001's collection/dedup/
availability/role-grouping/salary entities (FR-013); those are referenced here only where the new
data attaches to them. Layering is unchanged: framework-free **Domain**, ports in **Application**,
EF Core in **Infrastructure**.

Conventions retained from feature 001: wrapped IDs (`readonly record struct`), value objects as
immutable `record`s with structural equality, collections persisted as **jsonb string arrays** via
`HasJsonbListConversion<string>()`, single VOs via `HasJsonbConversion<T>()`, **append-only** EF
migrations, `Result<T>` for expected failures.

---

## 1. New enums (Domain)

```
Domain/Enrichment/EnrichmentState.cs
  enum EnrichmentState { Pending, Produced, Failed }      // offer_enrichment AND offer_fit

Domain/Cv/CvProcessingState.cs
  enum CvProcessingState { Pending, Produced, Unreadable, Failed }   // CV profile
```

`Pending` is the default at creation and after invalidation. `Failed` is terminal until inputs change
or a manual re-run re-arms it. `Unreadable` (CV only) is an authoritative worker verdict with **no
retries**, counted as neither pending nor failed.

---

## 2. Value objects (Domain, framework-free)

### `InputHash` — `Domain/Enrichment/InputHash.cs`
```
record InputHash(string Algorithm, int Version, string Hash)   // mirrors ContentFingerprint's shape
  const Sha256 = "SHA256"
```
Stored as three columns (`*_input_algo`, `*_input_version`, `*_input_hash`) or, equivalently, a single
`inputs_hash` string carrying `algo:version:hash`. Equality is structural over all three; a `Version`
bump forces a global recompute.

### `CvProfile` — `Domain/Cv/CvProfile.cs`  *(the AI profile; replaces the keyword `CandidateProfile` on the CV)*
```
record CvProfile(IReadOnlyList<string> Skills, string Seniority, string Summary)
```
`Skills` are plain Claude-produced strings (no `SkillRef`/catalog). `Seniority` is free text; parse
via the retained `SeniorityLevels.Parse` only where matching/ordering needs a level.

### `EnrichmentSettings` — `Domain/Settings/EnrichmentSettings.cs`  *("Enrichment configuration" entity, FR-018)*
```
record EnrichmentSettings {
  int OfferSummaryMaxWords = 60;
  int CvSummaryMaxWords    = 60;
  int MaxKeySkills         = 10;
  int FitRationaleMaxWords = 30;
  int RetryLimit           = 3;
  static Default => new();
}
```
All caps are **soft** (guidance to the worker + loose write-back validation); `RetryLimit` drives the
`Pending → Failed` transition. Stored on `AppSettings` (see §6).

---

## 3. Input-hash composers (Domain, pure)

`Domain/Enrichment/` — SHA-256 over canonical sorted-key JSON, the same style as
`ContentFingerprint.Compute`. **Versioned** so a formula change can force a global recompute.

| Composer | Inputs | Note |
|---|---|---|
| `OfferEnrichmentInputs.Hash(OfferContent)` | Major-tier `ContentFingerprint.Hash` **+ `DescriptionHtml` + company + location** | INCLUDES description (FR-006) — distinct from `ContentFingerprint`, which excludes it |
| `CvProfileInputs.Hash(ReadOnlySpan<byte> documentBytes)` | the CV file bytes | computed in Infrastructure at upload (IO stays out of Domain) |
| `EffectiveProfile.Version(CvProfile, ProfilePreferences)` | canonical produced profile (ordered skills + seniority + summary) + preferences | null until a profile is `Produced` |
| `OfferFitInputs.Hash(offerEnrichmentHash, effectiveProfileVersion, ScoringWeights)` | composite of the three | FR-004/006/007 |

Propagation (no fan-out writes needed for correctness; eager hooks make counts cheap — see §7):
- CV change → new `CvProfileInputs` → profile recomputes → `EffectiveProfile.Version` changes/absent →
  every `OfferFitInputs` differs → **all fits pending** (FR-007/SC-004).
- Weights change → `OfferFitInputs` differs for all offers → **all fits pending**.
- Single offer content/description change → that offer's `OfferEnrichmentInputs` and `OfferFitInputs`
  differ → **that offer's** summary/skills + fit pending.
- Offer becomes unavailable → content hash unchanged → enrichment + fit **stay Produced** (FR-017).

---

## 4. `OfferEnrichment` (new aggregate / table `offer_enrichment`)

1:1 with `Offer` (PK = `OfferId` = FK → `offers(id)` cascade). A mutable derived cache whose **payload**
is written only by the worker; the scan path touches only its lifecycle `State` (create `Pending` /
`Invalidate` on content change — §7), never its summary/skills payload.

| Field | Type | Notes |
|---|---|---|
| `OfferId` | `OfferId` (PK/FK) | reuse existing wrapped id |
| `State` | `EnrichmentState` | `varchar(20)` via `HasConversion<string>()` |
| `Attempts` | `int` | resets to 0 on produce/invalidate |
| `Summary` | `string?` | plain text (≤ `OfferSummaryMaxWords`, soft) |
| `KeySkills` | `IReadOnlyList<string>` | `key_skills` jsonb (≤ `MaxKeySkills`, soft) |
| `InputsHash` | `string?` | freshness/stale-write guard |
| `ProducedAt` | `DateTimeOffset?` | |
| `LastError` | `string?` | reason of last failed attempt |

Methods: `CreatePending(OfferId)`, `MarkProduced(summary, keySkills, inputsHash, at)`,
`RecordFailure(error, retryLimit)` (`Attempts++`; `Failed` at limit), `Invalidate()`
(`Pending`, attempts→0, payload kept internally), `Rearm()` (`Failed → Pending` on manual re-run).
Index on `State`.

---

## 5. `OfferFit` (new aggregate / table `offer_fit`)  — **supersedes read-time scoring**

1:1 with `Offer` (PK = `OfferId` = FK → `offers(id)` cascade). Standalone so the worker writes it
without loading the `Offer` aggregate.

| Field | Type | Notes |
|---|---|---|
| `OfferId` | `OfferId` (PK/FK) | |
| `State` | `EnrichmentState` | |
| `Attempts` | `int` | |
| `Score` | `int?` | 0..100 (FR-004) |
| `Matched` | `IReadOnlyList<string>` | `matched` jsonb |
| `Missing` | `IReadOnlyList<string>` | `missing` jsonb |
| `Rationale` | `string?` | ≤ `FitRationaleMaxWords` (soft) |
| `InputsHash` | `string?` | composite offer+profile+weights hash |
| `ProducedAt` | `DateTimeOffset?` | |
| `LastError` | `string?` | |

Methods mirror `OfferEnrichment`. Index on `State`. **`Score` is only ever displayed when
`State==Produced` and `InputsHash == current`** — otherwise the UI shows pending/failed; **never** a
non-AI fallback (FR-005).

---

## 6. `CandidateCv` (extended) + `AppSettings` (extended)

### `CandidateCv` — `Domain/Cv/CandidateCv.cs`  *(the "CV profile (extended)" entity)*
Existing fields retained: `CvId Id`, `string FileName`, `DateTimeOffset? ExtractedAt`,
`bool IsReadable` (PdfPig gauge — **kept** as a worker hint, **not** a display gate). The legacy
`CandidateProfile? DerivedProfile` and its `derived_profile` column are **removed** (ADR-2), replaced
by the AI `CvProfile` below. **Added** columns:

| Field | Column | Type | Notes |
|---|---|---|---|
| `ProfileState` | `profile_state` | `varchar(20)` | `CvProcessingState`, default `Pending` |
| `ProfileAttempts` | `profile_attempts` | `int` | default 0 |
| `Profile` | `profile_skills` / `profile_seniority` / `profile_summary` | jsonb + varchar + text | the AI `CvProfile` |
| `EnrichmentInputHash` | `enrichment_input_hash` | `varchar(80)` | `CvProfileInputs.Hash` at upload |
| `ProfileProducedAt` | `profile_produced_at` | `timestamptz` | |

Methods: `SetExtractionGauge(isReadable, inputHash, at)` (at upload; `Pending`, attempts→0),
`ApplyProfile(CvProfile, inputHash, at)` (`Produced`), `MarkUnreadable(at)` (`Unreadable`, profile
null), `RecordProfileFailure(retryLimit, at)` (`Attempts++`; `Failed` at limit), `RearmProfile()`.
Replacing a CV = new `CvId` = fresh `Pending` row.

### `AppSettings` — `Domain/Settings/AppSettings.cs`
Add `EnrichmentSettings Enrichment { get; private set; } = new();` + `UpdateEnrichment(EnrichmentSettings)`.
Persisted as a jsonb column `enrichment` on the existing `app_settings` singleton (id = 1). Existing
`Normalization`, `Weights`, `Preferences` unchanged (FR-013). `Weights` are **relabelled** "guidance
to Claude" in the UI (FR-011) — storage unchanged.

---

## 7. Invalidation hooks (eager `state` writes; additive, FR-013-safe)

| Trigger (existing write path) | Action |
|---|---|
| Scan upsert, offer content change (`Offer.ApplyUpdate`) OR silent Minor description refresh (`Offer.RefreshMinorContent`) | recompute `OfferEnrichmentInputs`; if changed → `enrichment.Invalidate()` **and** `fit.Invalidate()` for that offer |
| `SettingsService.UpdateWeightsAsync` | `UPDATE offer_fit SET state='Pending', attempts=0` (all rows) |
| CV upload / profile produced / replaced / preferences change | `UPDATE offer_fit SET state='Pending', attempts=0` (all rows) |
| Offer created (scan upsert) | create `Pending` `offer_enrichment` + `offer_fit` rows |
| First enablement (FR-014 backfill) | `DatabaseInitializer`, after `MigrateAsync`, **idempotently** inserts a `Pending` `offer_enrichment` + `offer_fit` row for every offer lacking one, and computes the existing CV's `enrichment_input_hash` from the stored PDF. So the satellite rows are an **invariant** (one per offer) — there is no "row absence = pending" path |

Each eager hook above, when it detects an input change, also **re-arms** a terminal `Failed` row
(`Failed → Pending`, attempts→0) so the `state` column matches the eligibility in §8 and `failedTotal`
never counts an item whose inputs have since changed. `offer_enrichment` is invalidated **only** by
that offer's own content (never by CV/weights) — "CV/weights change invalidates fit, not summaries/
skills" (spec edge case). The "profile produced" fit invalidation is a **no-op when the new
`effectiveProfileVersion` equals the old** (identical re-produce), avoiding a needless full re-run.

**Counts are eligibility-gated queries, not a bare `COUNT(state='Pending')`** (see §8): a fit is only
counted/displayable when a current **produced** CV profile exists, so an unreadable/failed/absent
profile never leaves ~180 fits "stuck pending" and `pendingTotal` can reach 0 (SC-007). The
`inputs_hash` guard (§3) is the defensive backstop on both the worker write-back (server **recomputes
the current hash from live inputs** and rejects a mismatch as `stale`) and the read path.

---

## 8. Pending-work ordering (FR-019)

Computed in `Application/Enrichment` and used by `GET /api/enrichment/pending`:

1. **CV profiles** whose effective state is `Pending` — first.
2. **Offer summaries** that are `Pending`.
3. **Offer fits** that are `Pending` — emitted **only** for offers where the current effective
   profile is `Produced` (sequencing guarantee: profile-before-fit). If no readable CV exists, fit is
   **absent** (not pending) per the "No CV uploaded" edge case.

Within (2)+(3): **available before unavailable** (availability does **not** gate inclusion — FR-017,
just orders), then `PublishedAt` DESC (date-less last), then `OfferId`, then kind (summary before
fit). Take `limit` (default 25, max 100). Satellite rows always exist (invariant — §7 backfill), so a
target is *pending/eligible* when its row is `Pending`, OR `Produced` with `inputs_hash ≠ current`, OR
`Failed` with stored failed-hash ≠ current (inputs changed → the eager hook already re-armed it to
`Pending`). `Failed` rows whose hash still matches are excluded (FR-015) until inputs change or a
manual re-run. A fit is eligible/counted **only** when the offer has a current **produced** CV profile
(else fit is absent, not pending).

---

## 9. Read-model changes (`Application/Offers/OfferReadModels.cs`)

- `FitView` gains `Rationale` (`string?`) and `State` (`pending|produced|failed`). The Domain
  `FitBreakdown` (with its per-axis double scores) is **removed**; `FitView` never exposed those, so
  the API/frontend `FitDto` is unaffected — it only gains `rationale` + `state`.
- `OfferListItem` gains `Summary` (`string?`), `KeySkills` (`string[]`), `EnrichmentState`, `FitState`.
  `Fit` is `FitView?`. **Fit-absence is keyed on the produced CV profile, not the PdfPig gauge**:
  `Fit = null` when there is **no current produced CV profile** (no CV, or the profile is
  `Pending`/`Unreadable`/`Failed` — nothing to match against); otherwise it carries the produced
  score **or** a `pending`/`failed` state (never a fallback number). The PdfPig `IsReadable` flag is a
  worker hint only and **does not** gate display (an image-only CV that Claude profiles still shows
  fits — R4).
- `OfferListMeta` gains `PendingEnrichment` and `FailedEnrichment` counts (also exposed by
  `GET /api/enrichment/status`); `NoReadableCv` is replaced by **`HasProducedProfile`** (a current
  produced CV profile exists) so the UI's "no fit yet" messaging tracks the profile, not the gauge.
- `OfferReadService` **stops calling `Scorer.Score`**; it loads `offer_enrichment` + `offer_fit`,
  recomputes the current input hashes once per request, and projects `Produced | Pending | Failed`
  (a `Produced` row whose hash ≠ current renders as `pending`). The **default sort is two-tier**:
  **(1)** offers with a current *produced* fit, ordered by `Scorer.CombinedRank` (AI score); then
  **(2)** pending/absent-fit offers, ordered by `Scorer.DegradedRank` (salary + recency). Ordering
  only — never a displayed fit. The `published`/`salary`/`fit` sorts are unchanged.

---

## 10. Removed types (FR-005 — no non-AI fallback; see [research.md → R5](./research.md))

`Scorer.Score`, `FitScore`, `FitBreakdown`, `ScoringInput`; `CvProfileBuilder`, `ICvProfileBuilder`,
`SkillCatalog`, `SkillCatalogLoader`, `skill-catalog.json`, `SkillRef`, **`CandidateProfile`**,
`CandidateProfileMerger`; the now-unused **FuzzySharp** package (+ `Directory.Packages.props` version
+ the `skill-catalog.json` copy directive); the `SkillCatalog`/`ICvProfileBuilder` **DI registrations**
and the `ICvProfileBuilder` parameter on `CvService`; and the `derived_profile` column +
`HasJsonbConversion<CandidateProfile>()` mapping (dropped in the `LlmEnrichment` migration).

**Reworked consumers** (else the build breaks — Principle VI): `ProfileService.BuildEffectiveProfileAsync`/
`GetAsync`/`ProfileView` source the effective profile from the new AI `CvProfile` + preferences;
`CvEndpoints.ToCvDto` projects the new AI profile (`ProfileState`, `Profile.Skills/Seniority/Summary`,
`ProfileAttempts`) instead of `DerivedProfile`. **Tests**: delete `Domain.Tests/ScorerTests.cs`;
rewrite `Infrastructure.Tests/CvExtractionTests.cs` (keep the PdfPig degradation test, drop the
`CvProfileBuilder` assertion); add a guard test asserting no AI package is referenced.

**Retained**: `PdfPig` (readability + fallback), `Scorer.CombinedRank`/`DegradedRank` (feed ordering),
`ScoringWeights` (now Claude guidance), `SeniorityLevels`. The new `CvProfile` VO — not the removed
`CandidateProfile` — is the worker's profile write-back target.

---

## 11. Migration plan — `LlmEnrichment` (one new migration; Principle IX)

`dotnet ef migrations add LlmEnrichment` (next after `20260628182153_RoleGroups`). New migration only
— **no prior migration is edited**. `Up()`:

1. `CreateTable("offer_enrichment", …)` and `CreateTable("offer_fit", …)` — PK `offer_id`, FK →
   `offers(id)` cascade, index on `state`; jsonb list columns default `'[]'`.
2. `AddColumn` ×N on `candidate_cv`: `profile_state varchar(20) NOT NULL DEFAULT 'Pending'`,
   `profile_attempts int NOT NULL DEFAULT 0`, `profile_skills jsonb NOT NULL DEFAULT '[]'`,
   `profile_seniority varchar(40) NULL`, `profile_summary text NULL`, `enrichment_input_hash
   varchar(80) NULL`, `profile_produced_at timestamptz NULL`. (Defaults required — the singleton CV
   row may already exist.)
3. `AddColumn enrichment jsonb NOT NULL DEFAULT '{...defaults...}'` on `app_settings` (seeds the
   existing singleton row).
4. `DropColumn derived_profile` on `candidate_cv` (a recomputable keyword cache — no user data, so
   Principle IX holds) and remove its `HasJsonbConversion<CandidateProfile>()` mapping.

`Down()` re-adds `derived_profile` and drops the two tables + added columns. Migration is **schema
only**; the per-offer `Pending` **backfill** (FR-014) is a runtime step owned by **`DatabaseInitializer`**
(it runs after `MigrateAsync` at startup, idempotently inserting a `Pending` `offer_enrichment` +
`offer_fit` row for every offer lacking one and computing the existing CV's `enrichment_input_hash`
from the stored PDF) — consistent with the codebase convention that `DatabaseSeeder` seeds config and
migrations never seed offer data. `AppDbContext` adds `DbSet<OfferEnrichment>` + `DbSet<OfferFit>`;
configurations are auto-discovered by `ApplyConfigurationsFromAssembly`; **no** new
`ConfigureConventions` line (the `OfferId` converter already applies).
