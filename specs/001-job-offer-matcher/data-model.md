# Phase 1 Data Model: Job Offer Aggregation & CV-Based Matching

**Date**: 2026-06-28 · Derives from [spec.md](./spec.md) Key Entities + FRs and
[research.md](./research.md) §6 (identity/dedup) and §7 (salary). Honors constitution v1.1.0:
strongly-typed (wrapped IDs, value objects, `Result<T>`), append-only history, framework-free
Domain.

## Conventions

- **Wrapped IDs** (no raw `Guid`/`int` in Domain/Application): `OfferId`, `SourceId`,
  `ScanRunId`, `OfferVersionId`, `OfferObservationId`, `OfferEventId`, `RoleGroupId`, `CvId`.
  Each is a `readonly record struct` over a `Guid` with a `New()` factory and parse/format.
- **Value objects** are immutable `record`s with structural equality and guarded construction
  (invalid input → `Result.Failure`, not exceptions).
- **Aggregates** own their consistency boundary; external code mutates only via the root.
- **Append-only**: `*_version`, `*_observation`, `*_event`, and finalized `scan_run` rows are
  never updated/deleted. The `offers` row holds a **denormalized current snapshot** for fast
  reads, rebuilt from events when needed.
- **Derived, not stored**: `NormalizedSalary` and `FitScore` are computed on demand (or cached
  tagged with a settings/CV version), **never** persisted as captured facts (FR-035).

---

## Value Objects

### Identity & change

| VO | Fields | Rules |
|----|--------|-------|
| `ExternalRef` | `SourceId`, `NativeKey: string`, `IdentityKind` | `NativeKey` required, non-empty. Unique per source. |
| `IdentityKind` (enum) | `NativeId, Slug, CanonicalUrl, FallbackHash` | Prefer `NativeId`/`Slug`; hash only as fallback. |
| `ContentFingerprint` | `Algorithm` (`"SHA256"`), `Version: int`, `Hash: string` | Computed by a **pure Domain function** over canonical sorted-key JSON of normalized Major-tier fields. `Version` bump suppresses the "updated" flag for algorithm-only deltas. |
| `ChangeTier` (enum) | `Major, Minor` | Major = title/salary/skills/work-mode/employment/seniority (user-flagged "updated"). Minor = description (versioned silently). |

### Salary (research §7)

| VO | Fields | Rules |
|----|--------|-------|
| `Money` | `Amount: decimal`, `Currency` | Pure record; reused as normalized output. |
| `Currency` | `Code: string` | Validated ISO-4217 3-letter uppercase; usable as FX-table key. Junk → `Result.Failure(UnknownCurrency)`. |
| `SalaryPeriod` (enum) | `Hourly, Daily, Monthly, Yearly` | — |
| `EmploymentBasis` (enum) | `B2B, Permanent, Unknown` | — |
| `TaxTreatment` (enum) | `Gross, Net, Unknown` | — |
| `SalaryBand` | `AmountMin?`, `AmountMax?`, `Currency?`, `Period?`, `Basis`, `Tax` | **All nullable** (FR-010). Hidden salary = empty band list, **never** a zero band. |
| `NormalizedSalary` (derived) | `ComparableMonthly: Money`, `NormalizedToBasis`, `NormalizedToPeriod=Monthly`, `Quality`, `Assumptions[]`, `Source: SalaryBand` | Produced by `SalaryNormalizer.Normalize`; never stored as fact. |
| `NormalizationQuality` (enum) | `Reported, Estimated, RoughEstimate` | Primary UI honesty signal. |
| `NormalizationAssumption` | `Text: string` | Ordered audit trail (e.g. "midpoint 18000–22000 = 20000"). |
| `SalaryNormalizationSettings` | `BaseCurrency=PLN`, `FxToBase: IReadOnlyDictionary<Currency,decimal>`, `AssumedMonthlyHours=168`, `AssumedMonthlyWorkingDays=21`, `B2bToPermanentFactor=0.85`, `CanonicalBasis=Permanent`, `RangeStrategy=Midpoint`, `FxAsOf: DateOnly`, `FxSource: string` | Editable data (single-row table / JSON). No live FX API. |
| `RangePointStrategy` (enum) | `Min, Midpoint, Max, Percentile` | Default `Midpoint`. |

### Matching (research §4)

| VO | Fields | Rules |
|----|--------|-------|
| `CandidateProfile` | `Skills: SkillRef[]`, `Seniority: SeniorityLevel`, `SalaryExpectation?: {Floor, Target}`, `PreferredWorkModes[]`, `PreferredEmployment[]` | Derived from CV(s) + user config. Skills/seniority from CV; salary/prefs from config. |
| `SkillRef` | `CanonicalId: string`, `DisplayName`, `Aliases[]` | From editable JSON skill catalog. |
| `SeniorityLevel` (enum) | `Intern, Junior, Mid, Senior, Lead, Principal, Architect` | Ordinal; profile takes MAX evidenced. |
| `FitScore` (derived) | `Value: int` (0–100), `Breakdown: FitBreakdown` | Computed; persisted only as a cached snapshot tagged with CV/weights version. |
| `FitBreakdown` (derived) | `Matched: ReasonItem[]`, `Missing: ReasonItem[]`, per-axis `{Skills, Seniority, WorkMode, Employment, Salary}` scores + reasons | The explicit **what-matches / what-does-not** list (FR-025). |
| `ScoringWeights` | `Skills=45, Seniority=20, WorkMode=12, Employment=8, Salary=15` | Config; sum=100. |

### Cross-source

| VO | Fields | Rules |
|----|--------|-------|
| `MatchConfidence` | `Value: double` (0..1) | Merge only at ≥ 0.85. |

---

## Aggregates & Entities

### Offer (aggregate root)

The single open position, per source. Denormalized current snapshot + append-only children.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `OfferId` | Internal identity. |
| `ExternalRef` | `ExternalRef` | `UNIQUE(source_id, native_key)`. |
| `SourceId` | `SourceId` | FK → JobSource. |
| `Title`, `Company` | string | Normalized + raw retained. |
| `SalaryBands` | `IReadOnlyList<SalaryBand>` | Raw, authoritative (FR-008/010). |
| `Location`, `WorkMode`, `EmploymentType`, `Seniority` | typed | WorkMode ∈ office/remote/hybrid. |
| `RequiredSkills`, `NiceToHaveSkills` | `string[]` | As collected; empty allowed (unknown, FR-010). |
| `DescriptionHtml` | string? | Minor tier; sanitized for display. |
| `CanonicalUrl` | string | `https://justjoin.it/job-offers/{slug}` (FR-029). |
| `PublishedAt`, `LastPublishedAt`, `ExpiredAt` | DateTimeOffset? | Source dates → **update detection + recency only**, never new-vs-seen. |
| `CurrentFingerprint` | `ContentFingerprint` | Latest Major-tier hash. |
| `FirstSeenAt`, `LastSeenAt`, `FirstSuggestedAt` | DateTimeOffset | FR-009/034. `FirstSuggestedAt` set once → decides new-vs-seen. |
| `Availability` | `AvailabilityStatus` | `Available` / `NoLongerAvailable`. |
| `DisappearedAt` | DateTimeOffset? | Set on `Complete`-scan reconciliation. |
| `RoleGroupId` | `RoleGroupId?` | Cross-source cluster (non-destructive). |
| `UserStatus` | `UserOfferStatus` | Current denormalized status (from latest status event). |

**`AvailabilityStatus`** transitions:

```
            (collected, complete scan)
   (new) ─────────────► Available ──────────────► NoLongerAvailable
                            ▲                            │
                            └──────── Reappeared ────────┘   (seen again in a later complete scan)
```

**`UserOfferStatus`** (FR-031) `{ New, Viewed, Interested, Dismissed }` — append-only events;
**orthogonal** to availability and to new-vs-seen. `Dismissed` never re-appears as new (SC-002).
Validation: only the user changes status; illegal transitions rejected in Domain via `Result`.

### Children of Offer (append-only)

| Table/entity | Key fields | Purpose |
|--------------|-----------|---------|
| `OfferVersion` | `Id`, `OfferId`, `CreatedAt`, `ChangeTier`, snapshot (title/salary/skills/desc), `Fingerprint` | Immutable content snapshot per change (FR-014/034). |
| `OfferObservation` | `Id`, `OfferId`, `ScanRunId`, `ObservedAt`, `Fingerprint` | One row per offer per scan it was seen in → drives `LastSeen` + disappearance reconciliation. Indexed on `(scan_run_id)`, `(offer_id)`. |
| `OfferEvent` | `Id`, `OfferId`, `OccurredAt`, `Type`, `Payload` | Lifecycle: `FirstSeen, Surfaced, Updated, BecameUnavailable, Reappeared, StatusChanged`. |

### JobSource

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `SourceId` | — |
| `Name` | string | e.g. "justjoin.it". |
| `Kind` | enum `{ DirectApi, InteractiveBrowser }` | Selects the `IJobSource` adapter. |
| `SearchCriteria` | JSON | Editable filter params (FR-002) — categories, levels, employment types, withSalary, workingTimes, ordering; + the client-side `workplaceType` keep-set. |
| `RequiresLogin` | bool | Drives escalation trigger (FR-006). |
| `Enabled` | bool | FR-003/enable-disable. |

### CandidateCv

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `CvId` | — |
| `FileName` | string | Local file (gitignored). |
| `ExtractedAt` | DateTimeOffset? | — |
| `IsReadable` | bool | False if PdfPig output below threshold → graceful degradation (FR-026). |
| `DerivedProfile` | `CandidateProfile?` | Cached derived profile; recomputed on CV/catalog change. |

Multiple CVs allowed (FR-022); the profile is built from the **best-fitting** CV per offer
(spec edge case) — in practice merge skills/seniority across readable CVs, prefer the strongest.

### OfferMatch (derived/recommendation)

Computed per (Offer, CandidateProfile). **Not** a stored fact; cached tagged with CV +
weights + normalization-settings version, invalidated on any change.

| Field | Type | Notes |
|-------|------|-------|
| `OfferId` | `OfferId` | — |
| `FitScore` | `FitScore` (0–100) | FR-023. |
| `Breakdown` | `FitBreakdown` | matched/missing (FR-025). |
| `NormalizedSalaryForRank` | `NormalizedSalary?` | FR-024; null → salary term contributes nothing. |
| `CombinedRank` | double | `0.70·fit + 0.30·normSalary` (CV present) or `0.6·normSalary + 0.4·recency` (FR-026). |

### ScanRun (append-only once finalized)

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `ScanRunId` | — |
| `StartedAt`, `FinishedAt` | DateTimeOffset | — |
| `WindowUtc` | DateTimeOffset? | The cron window this run satisfies; **`UNIQUE(window_utc, trigger)`** for idempotent catch-up. |
| `Trigger` | `TriggerType` `{ Manual, Scheduled, CatchUp, Initial }` | FR-020. |
| `SourceIds` | `SourceId[]` | Sources covered. |
| `Counts` | `{ Collected, New, Updated, Unavailable, Failed }` | FR-020. |
| `Outcome` | `ScanOutcome` `{ Complete, Partial, Failed }` | Gates disappearance reconciliation (FR-015). |
| `IncompleteReason` | enum? `{ LoginNotCompleted, ChallengeDetected, NetworkFailure, LayoutChanged }` | FR-036. |

**Outcome rules**: a run is `Complete` only if full pagination walked with no extraction drops;
if it returns < ~50% of the previous `Complete` count for a source (configurable), **downgrade
to `Partial`** (no reconciliation) and surface for review.

### RoleGroup (cross-source cluster, non-destructive)

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `RoleGroupId` | — |
| `MemberOfferIds` | `OfferId[]` | Each member keeps its own identity/link/history (FR-011/034). |
| `Confidence` | `MatchConfidence` | Auto-merge ≥ 0.85; else separate. |
| `UserOverride` | enum? `{ Same, NotSame }` | Persisted; wins over heuristic. |

### Settings (singletons)

- `SalaryNormalizationSettings` (above) — single-row / JSON, editable.
- `ScheduleConfig`: `{ Cron: string, TimeZone: string, Enabled: bool }`, validated at the API
  boundary (`Result` on bad cron). `LastRunUtc` persisted for the catch-up poll-tick.
- `ScoringWeights` — editable.
- `SkillCatalog` — editable JSON.

---

## PostgreSQL schema (tables)

```
job_source(id pk, name, kind, search_criteria jsonb, requires_login, enabled)
candidate_cv(id pk, file_name, extracted_at, is_readable, derived_profile jsonb)

offers(id pk, source_id fk, native_key, identity_kind,
       title, company, location, work_mode, employment_type, seniority,
       salary_bands jsonb, required_skills jsonb, nice_skills jsonb, description_html,
       canonical_url, published_at, last_published_at, expired_at,
       current_fingerprint, fingerprint_version,
       first_seen_at, last_seen_at, first_suggested_at,
       availability, disappeared_at, role_group_id fk null, user_status,
       UNIQUE(source_id, native_key))

offer_version(id pk, offer_id fk, created_at, change_tier, snapshot jsonb, fingerprint)
offer_observation(id pk, offer_id fk, scan_run_id fk, observed_at, fingerprint,
                  INDEX(scan_run_id), INDEX(offer_id))
offer_event(id pk, offer_id fk, occurred_at, type, payload jsonb)

scan_run(id pk, started_at, finished_at, window_utc, trigger, source_ids jsonb,
         count_collected, count_new, count_updated, count_unavailable, count_failed,
         outcome, incomplete_reason,
         UNIQUE(window_utc, trigger))

role_group(id pk, confidence, user_override null)

app_settings(id pk single-row, salary_norm jsonb, schedule jsonb, last_run_utc,
             scoring_weights jsonb, skill_catalog jsonb)
```

**Upsert mechanics**: per offer, read-by-`(source_id, native_key)` → branch
(insert-new / unchanged / updated / reappeared) → append children, inside a transaction, with
`UNIQUE(source_id, native_key)` as the safety net (plain `ON CONFLICT` is insufficient because
the branch decides *different* appended rows).

## Requirement coverage (selected)

| FR / SC | Where satisfied |
|---------|-----------------|
| FR-008/010 salary capture + unknowns | `SalaryBand[]` (nullable), raw stored authoritatively |
| FR-009/034 first/last seen, suggested history | `offers` timestamps + `offer_event` (FirstSeen/Surfaced) |
| FR-011 stable identity | `ExternalRef` + `UNIQUE(source_id, native_key)`; `guid` native key |
| FR-012/013 new vs seen, no re-flag | identity existence + `FirstSuggestedAt`; unchanged fingerprint only bumps LastSeen |
| FR-014 updated | `ContentFingerprint` diff → new `OfferVersion` + `Updated` event |
| FR-015 disappeared | `Complete`-scan reconciliation + sanity guard → `NoLongerAvailable` |
| FR-016 cross-source dedup | non-destructive `RoleGroup`, conservative gate, user override |
| FR-020 run record | `scan_run` counts + outcome + reason |
| FR-023/025 fit + matched/missing | `FitScore` + `FitBreakdown` (derived) |
| FR-024/026 ranking + degradation | `CombinedRank`; CV-absent path ranks by salary+recency |
| FR-031 user status persists | append-only `StatusChanged` events; `Dismissed` never re-new |
| FR-035 no fabricated data | normalized salary + fit are **derived**, never stored as fact |
| SC-002 unchanged surfaced as new ≤ once | new-vs-seen by identity existence only |
