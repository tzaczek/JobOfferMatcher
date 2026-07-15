# Phase 1 — Data Model: LinkedIn Recommended Jobs Source (008)

**Headline: no new table, no EF migration.** Everything the feature persists is either config in the
already-mapped `job_source` table (jsonb-extended, schemaless) or an on-disk browser session outside
every persisted store. This section documents the shapes, the invariants, and why no migration is
needed.

---

## 1. `JobSourceSearch` — additive jsonb fields (no migration)

`Domain/Sources/JobSourceSearch.cs` gains two LinkedIn-oriented properties, stored in the existing
`search_criteria` **`jsonb`** column (`JobSourceConfiguration.cs:18` `HasJsonbConversion`). Adding
record properties is schemaless — old rows without the keys deserialize to defaults.

| Field (new) | Type | Meaning / default |
|---|---|---|
| `IncludeRecommended` | `bool` | Collect the personalized *Recommended* (JYMBII) feed. Default `false`; seeded `true` for the LinkedIn source. |
| `LinkedInSearches` | `IReadOnlyList<LinkedInSearch>` | Saved keyword searches, each an independent pass (R4). Default `[]`. |

New value object `Domain/Sources/LinkedInSearch.cs`:

| Field | Type | Maps to LinkedIn URL param |
|---|---|---|
| `Keywords` | `string` (required, non-blank) | `keywords=` |
| `Location` | `string?` | human label (display only) |
| `GeoId` | `string?` | `geoId=` (e.g. `90009828`) |
| `Distance` | `int?` | `distance=` (e.g. `50`) |
| `Recency` | `string?` | `f_TPR=` (e.g. `r1296000` = last 15 days) |

Existing `JobSourceSearch` fields (`Categories`, `ExperienceLevels`, …, `WorkplaceKeep`) are unused
by the LinkedIn adapter and untouched. **Back-compat**: the jsonb converter (System.Text.Json)
ignores absent members → every existing `justjoin.it`/`theprotocol`/`nofluffjobs` row keeps working.

---

## 2. `JobSource` (LinkedIn instance) — reused aggregate, no schema change

A LinkedIn source is an ordinary `JobSource` row (`Domain/Sources/JobSource.cs`) with:

| Column | Value for LinkedIn |
|---|---|
| `id` | `DefaultLinkedInSourceId` = `44444444-4444-4444-4444-444444444444` (stable, idempotent seed) |
| `name` | "LinkedIn" (user-editable) |
| `kind` | `InteractiveBrowser` (routes to `LinkedInSource` — R1) |
| `search_criteria` (jsonb) | `{ IncludeRecommended: true, LinkedInSearches: [ …seeded starter search… ], … }` |
| `requires_login` | `true` |
| `enabled` | **`false`** (see the note below — revised during implementation) |

`SeedSourceAsync` (`DatabaseSeeder.cs`) is parameterized to accept `SourceKind` + `requiresLogin`
(+ `enabled`; previously hard-coded `DirectApi`/`false`/`true`) so the LinkedIn row can be seeded. Seed
is idempotent by id; user edits to the row (searches, enable/disable, rename) are never overwritten on
restart. Additional user-created LinkedIn sources are possible but the **single seeded source with
multiple saved searches** is the intended shape (R4 — cross-pass dedup).

> **Revised at implementation time — seeded `enabled: false` (was `true`).** A login-gated source cannot
> collect until the user signs in. If it were seeded **enabled**, every **unattended** (scheduled) scan
> would fail it with `LoginNotCompleted`, dragging the whole run's outcome to `Failed`. Disappearance
> reconciliation's baseline lookup (`ScanRunRepository.GetLastCompleteForSourceAsync`) is gated on the
> **run-level** `Outcome == Complete`, so a chronically-`Failed` run would leave **every other source**
> without a "last complete run" — permanently disabling the `<50%` sanity guard and risking a
> mass-unavailable of live offers (FR-015 violation; caught by `DisappearanceReconciliationTests`). So the
> source is seeded **disabled**; the user enables it from the Sources page when ready to log in (scanning
> it explicitly by id still works while disabled). This trades a touch of first-run friction for not
> breaking reconciliation for the DirectApi sources.

---

## 3. Collected LinkedIn Offer — reuses `offers` unchanged

No offer schema change. A LinkedIn posting maps to the existing offer shape:

| Offer field | Source |
|---|---|
| `ExternalRef.SourceId` | the LinkedIn `job_source.id` |
| `ExternalRef.NativeKey` | LinkedIn numeric **job id** (e.g. `4428922336`, from the card / `currentJobId` / job URN) |
| `ExternalRef.IdentityKind` | `NativeId` |
| `Content.Title / Company / Location / WorkMode` | list card (work mode: remote/hybrid/on-site when shown) |
| `Content.CanonicalUrl` | `https://www.linkedin.com/jobs/view/{jobId}/` |
| `Content.SalaryBands` | usually empty (LinkedIn rarely discloses on PL listings) → renders "not available" |
| `DescriptionHtml` (Minor-tier body) | the job detail pane, via `FetchBodyAsync` (fetched only for new/updated/body-missing offers — existing orchestrator predicate) |

Identity `(source_id, native_key)` gives cross-scan dedup (FR-005) and, because all passes share one
`source_id`, cross-pass dedup (US2 AC2). Enrichment/fit/affinity satellites are created **Pending** at
upsert by the existing orchestrator code (SC-005) — no change.

---

## 4. LinkedIn Session (on-disk, not persisted in any store)

A **Playwright persistent context** directory (cookies + localStorage). Not an entity — no DB row, no
column.

- **Location**: `Sources:LinkedIn:ProfilePath` ?? `{LocalApplicationData}/JobOfferMatcher/browser-profiles/linkedin`.
- **Lifecycle**: created on first login; reused across scans until LinkedIn expires/revokes it; the
  user re-logs-in (mid-manual-scan) to refresh.
- **Privacy / recovery**: gitignored (`browser-profiles/` / `**/.auth/` reserved) and **excluded from
  the 003 backup** (outside `cv-data/` and the DB) → FR-012a. Holds no app-readable password — only
  the browser's own session cookies (Principle IV). Survives `dotnet clean` (outside `bin/`).

---

## 5. `ScanContext` (scoped, in-memory) — attended-ness carrier

New scoped service `Application/Scanning/ScanContext.cs` (no persistence):

| Member | Type | Notes |
|---|---|---|
| `RunId` | `ScanRunId` | set by the orchestrator at scan start |
| `Trigger` | `TriggerType` | Manual / Scheduled / CatchUp / Initial |
| `AllowInteractiveLogin` | `bool` (derived) | `Trigger == Manual` |
| `Begin(runId, trigger)` | method | called once by `ScanOrchestrator.RunCoreAsync` |

Scoped lifetime is safe: `IScanRunner` is scoped and both entry points run in a DI scope (request /
`ScanSchedulerService.CreateAsyncScope`). `LinkedInSource` reads `AllowInteractiveLogin` and passes it
to `EnsureLoggedInAsync`. No other adapter or the port signature is affected.

---

## 6. Scan outcomes for LinkedIn (reused enums — no new values)

| Situation | `ScanOutcome` | `IncompleteReason` |
|---|---|---|
| Collected cleanly | `Complete` | — |
| No/expired session, **unattended** scan | `Failed` | `LoginNotCompleted` |
| Login timed out (attended, user didn't finish) | `Failed` | `LoginNotCompleted` |
| Anti-automation / checkpoint wall mid-collection | `Partial` | `ChallengeDetected` |
| Selectors/layout broke, or `<50%` sanity guard | `Partial` | `LayoutChanged` |
| Transport/browser crash | `Failed` | `NetworkFailure` |

All four `IncompleteReason` values and all three `ScanOutcome` values already exist — **no enum
change**. FR-015/SC-004: a `Failed`/`Partial` LinkedIn scan never reconciles (reconciliation is
Complete-gated) and never removes prior offers.

---

## 7. Config — `LinkedInOptions` (`Sources:LinkedIn`)

Not persisted (appsettings / defaults), mirroring `TheProtocolOptions`:

| Key | Default | Purpose |
|---|---|---|
| `UseBrowser` | `true` | swap `PlaywrightLinkedInClient` ↔ `NotConfiguredLinkedInClient` |
| `Headless` | `false` | **must be headed** for manual login |
| `ProfilePath` | `{LocalApplicationData}/JobOfferMatcher/browser-profiles/linkedin` | persistent context dir |
| `NavigationTimeoutMs` | `45000` | per-navigation |
| `LoginTimeoutMs` | `180000` | bound the mid-scan login wait |
| `RequestDelayMs` | `1500` | polite pacing (~<1 req/s) |
| `MaxResultsPerSearch` | `50` | bounded collection (FR-013) |
| `RecommendedUrl` | `https://www.linkedin.com/jobs/collections/recommended/` | feed pass |
| `SearchUrlTemplate` | `https://www.linkedin.com/jobs/search-results/?keywords={keywords}&geoId={geoId}&distance={distance}&f_TPR={recency}` | search pass |
| `UserAgent` / `Locale` | realistic desktop / `en` | context options |

---

## 8. Backup / export impact

- **Backup**: none. `job_source` already in `BackupTables.InsertOrder`; no new table →
  `BackupTablesCompletenessTests` unchanged. The session profile is out of scope by location (§4).
- **Export**: none required. `OfferExport` already carries `Source` (name), `Description` (body),
  `AffinityScore` — a LinkedIn offer exports like any other (SC-005). No new export field.

---

## 9. Data-integrity invariants (Principles III / IV / IX)

1. **Identity is stable & deduped** — `(source_id, LinkedIn job id)`; one offer across scans and
   across recommended/search passes (FR-005, US2 AC2).
2. **No fabricated data** — a missing salary/body renders "not available"; a failed scan yields an
   honest `Failed/Partial` outcome, never invented offers (Principle III).
3. **Credentials never persisted** — no password anywhere; only the browser's own session cookies, on
   disk, gitignored, backup-excluded (Principle IV, FR-009/012/012a).
4. **No existing data dropped or edited** — additive jsonb fields only; no migration; other sources
   untouched (Principle IX).
