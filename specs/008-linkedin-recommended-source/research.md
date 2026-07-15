# Phase 0 — Research: LinkedIn Recommended Jobs Source (008)

All decisions below were resolved against the **live 001–007 code** (read during Phase 0), not
assumptions. Format per decision: **Decision / Rationale / Alternatives rejected**. No
`NEEDS CLARIFICATION` remain — the four spec clarifications (2026-07-15) fed the design directly.

Grounding reads (file:line anchors the ADRs rely on):
- `Application/Scanning/ScanOrchestrator.cs` — source-agnostic scan loop; identity dedup by
  `ExternalRef(source_id, native_key)`; per-source `context.Seen`; body-fetch predicate
  (`new || Updated || storedBody is null`, lines 210-214 / 255-258); `<50%` sanity guard; terminal
  record on interruption; **knows `request.Trigger`**.
- `Application/Scanning/IJobSource.cs` — `CollectAsync(search, onOffer, ct)` + `FetchBodyAsync`.
- `Infrastructure/Sources/JobSourceFactory.cs` — routes `SourceKind.InteractiveBrowser` → the
  `NotConfiguredInteractiveBrowserSource` stub (the arm we replace).
- `Domain/Scans/ScanEnums.cs` — `ScanOutcome{Complete,Partial,Failed}`,
  `IncompleteReason{LoginNotCompleted,ChallengeDetected,NetworkFailure,LayoutChanged}`,
  `TriggerType{Manual,Scheduled,CatchUp,Initial}`.
- `Application/Scanning/IInteractiveBrowserSession.cs` + `Infrastructure/Sources/Browser/NotConfigured*`
  — the deferred manual-login port (FR-040), a stub returning `LoginNotCompleted`/`NotConfigured`.
- `Infrastructure/Sources/TheProtocol/PlaywrightTheProtocolClient.cs` — the reusable Playwright
  pattern (lazy singleton browser, `SemaphoreSlim` gate, `IAsyncDisposable`, poll-until-ready loop,
  `PaceAsync`), but **ephemeral `NewContextAsync`** — no persistence.
- `Infrastructure/DependencyInjection.cs:105-131` — config-gated Playwright-vs-HTTP client swap
  (`Sources:TheProtocol:UseBrowser`), factory + `IInteractiveBrowserSession` registration.
- `Infrastructure/Scheduling/ScanSchedulerService.cs:55-57` — each scheduled scan runs in a fresh
  `CreateAsyncScope()`; `Application/DependencyInjection.cs:26` — `IScanRunner` is **scoped**.
- `Infrastructure/Persistence/Seed/DatabaseSeeder.cs` — idempotent per-id seed;
  `SeedSourceAsync` hard-codes `DirectApi/requiresLogin:false`; `SourceId.From(new Guid("…"))`.
- `Domain/Sources/JobSourceSearch.cs` + `Configurations/JobSourceConfiguration.cs:18` —
  `search_criteria` is **`jsonb`** (`HasJsonbConversion`); the offers table already has `source_id`,
  `native_key`, `identity_kind`, `description_html`.
- `.gitignore:40-43` — `browser-profiles/`, `**/.auth/`, `playwright/.auth/` already reserved
  ("session cookies = sensitive"); `cv-data/` (backed up) is separate.
- Frontend `api/types.ts` — `ScanState` already includes `waiting_for_login` + `challenge_detected`;
  `pages/Sources/SourcesPage.tsx` already offers the `InteractiveBrowser` kind + "Requires login".

---

## R1 — LinkedIn is the first real `SourceKind.InteractiveBrowser` adapter (activates FR-040)

**Decision**: Add `LinkedInSource : IJobSource` with `Kind = SourceKind.InteractiveBrowser`. In
`JobSourceFactory`, **replace** the catch-all `SourceKind.InteractiveBrowser => NotConfiguredInteractiveBrowserSource`
arm with `=> LinkedInSource`. Seed **one** LinkedIn source (config only) with a new stable id
`DefaultLinkedInSourceId = 4444…`; parameterize `SeedSourceAsync` to accept `SourceKind` +
`requiresLogin`. The whole downstream pipeline (identity dedup, version/event history,
reconciliation, Pending enrichment/fit/affinity satellites, `/enrich` worker, export, backup) is
reused unchanged — LinkedIn offers are first-class via `ExternalRef(source_id, native_key = LinkedIn
job id)` + `description_html` body.

**Rationale**: The port, the `InteractiveBrowser` kind, the stub, the routing hook, the gitignore
reservations, and the Playwright NuGet all already exist for exactly this (001 research §2 / FR-040).
Routing by **kind** (not a per-id `when`) means the seeded recommended source *and* any user-created
LinkedIn source all reach `LinkedInSource` — LinkedIn is the only interactive source, so a per-site
discriminator column is YAGNI (Principle X). The orchestrator is already source-agnostic (it only
sees `IJobSourceFactory.Create` + `CollectAsync`), so onboarding needs **no** orchestrator logic
change beyond R3.

**Alternatives rejected**: (a) route by a fixed LinkedIn GUID like the DirectApi sites — blocks
FR-007 (a user-created LinkedIn source would fall through to the stub); (b) a new `job_source`
"site/adapter" discriminator column — a migration + schema change for a single new adapter (Principle
X/IX); (c) a brand-new `SourceKind.LinkedIn` value — churns the enum + every switch for no gain over
reusing `InteractiveBrowser`, whose whole reason for existing is login-gated collection.

---

## R2 — Authenticated, persisted, **headed** Playwright context; manual login; password never stored

**Decision**: A third Playwright consumer, `PlaywrightLinkedInClient` (singleton, `IAsyncDisposable`,
`SemaphoreSlim` gate — the `PlaywrightTheProtocolClient` shape), but launched via
**`Chromium.LaunchPersistentContextAsync(userDataDir, { Headless = false })`** so LinkedIn cookies /
localStorage persist between scans (001 research §2). The profile dir resolves to an **OS app-data,
gitignored, backup-excluded** location — default
`configuration["Sources:LinkedIn:ProfilePath"] ?? {LocalApplicationData}/JobOfferMatcher/browser-profiles/linkedin`
— never under `cv-data/` (so 003 backup can't sweep it) and outside `bin/` (so `dotnet clean`
can't wipe the login — see the cv-data/bin-wipe gotcha). Login-complete is detected by navigating to
the recommended feed and polling for the authenticated DOM / absence of the `/login` wall (the same
poll-until-ready idiom the Cloudflare clear uses). **The password is only ever typed by the user into
the headed browser window** — the backend never reads, stores, transmits, or logs it (FR-008/009/012;
Principle IV).

**Rationale**: Persistent context is the single Playwright feature that gives "log in once, reuse the
session" (FR-010, SC-003) without any credential handling. Headed is mandatory — the user must be
able to type credentials and clear 2FA/checkpoints themselves (Clarification Q1). OS-app-data rooting
(vs `AppContext.BaseDirectory`, which is `bin/Debug`) makes the login survive rebuilds/cleans.
Excluding it from `cv-data/` makes FR-012a hold with **zero** backup-code change — the 003 archive
only zips the DB + `cv-data/`, so an on-disk profile elsewhere is inherently out.

**Alternatives rejected**: app-stored credentials / headless auto-login (Clarification Q1 rejected;
violates Principle IV; brittle vs 2FA/bot-detection); importing the user's everyday Chrome profile
(Q1 rejected; couples to their main browser); a `StorageStateAsync` JSON blob persisted **in the DB**
(would land in the 003 backup — violates FR-012a — and is a live credential in an unencrypted
archive); sharing one browser across all three Playwright consumers (no shared host exists today;
each launches its own — Principle X, keep the convention).

---

## R3 — Attended-vs-unattended login gate via a scoped `ScanContext`; the only orchestrator edit

**Decision**: A **scoped** `ScanContext` holder (`ScanRunId RunId`, `TriggerType Trigger`,
`bool AllowInteractiveLogin => Trigger == TriggerType.Manual`) is set by `ScanOrchestrator` at the top
of `RunCoreAsync` (the **one** orchestrator edit) and read by `LinkedInSource`, which passes
`AllowInteractiveLogin` to `EnsureLoggedInAsync`. Behavior:
- **Valid persisted session** → collect.
- **No/expired session + attended (Manual)** → launch the headed login window mid-scan and **wait**
  (bounded by `LoginTimeoutMs` / `ct`) for the user to finish; then collect.
- **No/expired session + unattended (Scheduled/CatchUp/Initial)** → return
  `CollectionResult.Failed(IncompleteReason.LoginNotCompleted, 0)` **without launching** — the
  BackgroundService scheduler never blocks.

**Rationale**: The orchestrator already knows `request.Trigger`; `IScanRunner` is **scoped** and both
entry points run in a DI scope (the manual endpoint = request scope; `ScanSchedulerService` =
`CreateAsyncScope()` per tick — verified), so a scoped `ScanContext` is naturally isolated per scan
and visible to the per-scan adapter built from the same scope. This threads exactly the two facts the
login needs (attended-ness + `ScanRunId`) **without changing `IJobSource.CollectAsync` /
`IJobSourceFactory.Create`** — the four existing adapters and the port are untouched (Principle X).
`LoginNotCompleted` already exists as the precise reason; a manual scan already `await`s `RunAsync`
inside its POST, so "the scan pauses until login" is literally the awaited login step.

**Alternatives rejected**: adding a `trigger`/`attended` parameter to `IJobSource.CollectAsync` (a
port change rippling through five adapters + the orchestrator call site, for one adapter's need);
injecting the trigger into the singleton browser client (a singleton can't take scoped scan state —
it takes `interactive` as a method arg instead); always launching for any trigger (Q4 rejected —
hangs the unattended scheduler); a separate standalone "Connect LinkedIn" endpoint that logs in
outside a scan (Q2 rejected in favor of mid-scan auto-launch).

---

## R4 — One LinkedIn source, multi-pass collection (Recommended + saved searches) under one `source_id`

**Decision**: The single LinkedIn source collects in **passes** inside `CollectAsync`: an optional
**Recommended** pass (personalized feed) then **N saved-search** passes (keywords/location/distance/
recency), all streaming into the **same** `onOffer` callback and therefore the same per-source
`context.Seen` + `(source_id, native_key)` identity. A job seen in several passes upserts **once**
(US2 AC2). Each pass is independently tolerant: a pass that blocks/breaks contributes
`Partial`/`ChallengeDetected`|`LayoutChanged` and the other passes still run (US2 AC3). Passes are
configured on the source's `JobSourceSearch` (extended, R5).

**Rationale**: Cross-pass dedup is a hard requirement (US2 AC2: a job in *both* the feed and a search
is one offer). Offer identity is scoped to `(source_id, native_key)`; putting recommended + searches
under **one** `source_id` makes dedup fall out of the existing machinery for free, and the
orchestrator's `context.Seen` + Complete-gated reconciliation + `<50%` sanity guard all work
unchanged across the union of passes. FR-007 "more than one LinkedIn configuration … each collected
independently" is satisfied as multiple saved searches within the one source, each an independent
pass. This is *simpler* than N sources, not just more correct.

**Alternatives rejected**: one `job_source` **row per** search (recommended + each search = separate
`source_id`) — the same LinkedIn job collected by two of them would create **two** offers (distinct
`source_id`, same `native_key`), violating US2 AC2; fixing that would need net-new cross-source dedup
against the whole identity model. A single blended pass with no per-search boundary — loses the
independent-tolerance requirement (AC3).

---

## R5 — LinkedIn search config extends `JobSourceSearch` (jsonb) — **zero migrations**

**Decision**: Extend the `JobSourceSearch` record with additive, LinkedIn-oriented fields:
`bool IncludeRecommended` and `IReadOnlyList<LinkedInSearch> LinkedInSearches` where
`LinkedInSearch(string Keywords, string? Location, string? GeoId, int? Distance, string? Recency)`.
Because `search_criteria` is a **`jsonb`** column (`HasJsonbConversion`), adding record properties is
schemaless: existing rows (missing the keys) deserialize to defaults (`false` / `[]`), new rows carry
them. **No EF migration.** The recency/distance/geo map straight to the user's example URL params
(`f_TPR=r1296000` = last 15 days, `distance=50`, `geoId=90009828`).

**Rationale**: The single biggest "no data lost / stay simple" win — the config lives in the already
backed-up `job_source` table (R7), needs no schema change (Principle IX), and reuses the existing
source CRUD/endpoints/UI shape. Nesting the fields in the shared record mildly stretches its
"source-neutral" intent, but the alternative (a new jsonb column) is a migration for no benefit.

**Alternatives rejected**: a new `linkedin_config` jsonb **column** on `job_source` (a migration —
Principle IX/X); overloading existing neutral fields (`Categories` = keywords etc.) — lossy and
confusing (no home for location/distance/recency); a separate LinkedIn-searches table (a table +
migration + backup entry for a handful of strings).

---

## R6 — Config-gated Playwright client (`Sources:LinkedIn`), mirroring TheProtocol's `UseBrowser`

**Decision**: An `ILinkedInClient` port (`EnsureLoggedInAsync(interactive, ct)`, `FetchListAsync(
LinkedInListRequest, ct)`, `FetchBodyAsync(jobId, ct)`), with two DI-swapped impls exactly like
`ITheProtocolClient`: `PlaywrightLinkedInClient` (real, when `Sources:LinkedIn:UseBrowser` = true,
default) vs `NotConfiguredLinkedInClient` (login-not-completed / empty, when false — for
offline/CI/test and users who haven't provisioned Chromium). `LinkedInOptions`
(`SectionName = "Sources:LinkedIn"`): `UseBrowser`, `Headless = false`, `ProfilePath`,
`NavigationTimeoutMs`, `LoginTimeoutMs`, `RequestDelayMs`, `MaxResultsPerSearch`, `RecommendedUrl`,
`SearchUrlTemplate`, `UserAgent`, `Locale`. `PlaywrightLinkedInClient` also implements the documented
`IInteractiveBrowserSession` (its `EnsureLoggedInAsync` gains the `interactive` flag), honoring the
spec's "first activation of that port".

**Rationale**: Direct reuse of the proven TheProtocol swap keeps LinkedIn fully **offline-testable**
(fake `ILinkedInClient`) and lets the app run without launching Chromium when the user hasn't logged
in / provisioned the browser. Bounded, paced collection (`FetchListAsync` caps at
`MaxResultsPerSearch`, `PaceAsync` ~1 req/s) upholds the 001 ADR-2 polite-access risk (FR-013/014).

**Alternatives rejected**: a single monolithic `LinkedInSource` that news up Playwright directly
(untestable offline; no disable switch); wiring the real client unconditionally (breaks CI and the
no-Chromium path).

---

## R7 — No new table, no migration; backup covers config, excludes the session (FR-012a)

**Decision**: **Zero** schema change. The LinkedIn source is a row in the existing `job_source`
table, which is **already** in `BackupTables.InsertOrder` — so backup/restore covers the LinkedIn
config with no change and the `BackupTablesCompletenessTests` guard stays green (no new mapped table).
The persisted browser session lives on disk under the OS-app-data profile dir (R2), which the 003
archive does not include → FR-012a holds with no backup-code change. After a restore, the user simply
logs in again on their next manual scan (Clarification Q3).

**Rationale**: Everything the feature persists is either (a) config in an already-covered table or
(b) an on-disk session deliberately outside the backup root. This is the cleanest possible
"recoverable + private" story (Principles IX + IV) — nothing to add to the backup list, nothing to
back-fill.

**Alternatives rejected**: persisting the session in a DB table (would be swept into the unencrypted
003 archive — violates FR-012a) or a `cv-data/` subfolder (same problem — `cv-data/` is backed up).

---

## R8 — Login UX: the headed browser is the surface; the app shows an optimistic hint (no dispatch change)

**Decision**: Keep the existing **synchronous** manual-scan dispatch (`POST /scans/run` awaits
`RunAsync`). During a manual LinkedIn scan the headed Chromium window **is** the login surface; the
web UI shows an optimistic `waiting_for_login` hint ("A LinkedIn login window opened — finish signing
in there") for the duration, driven client-side from the fact that a login-required source is being
scanned (and from the last scan's `LoginNotCompleted` reason). A bounded `LoginTimeoutMs` caps the
wait; on timeout the scan finishes `Failed/LoginNotCompleted`.

**Rationale**: The `waiting_for_login`/`challenge_detected` frontend states already exist; the headed
browser already provides the authoritative interaction, so no backend handshake registry, no
background executor, and no new endpoint are needed for a single local user (Principle X). The POST
already holds open for the full scan today, so a mid-scan login wait is not a new shape.

**Alternatives rejected (noted for a future multi-tab/observability need)**: background-dispatch the
manual scan + a singleton login-handshake registry keyed by `ScanRunId` + status-driven
`waiting_for_login` (001 research §2). More robust for polling multiple viewers, but a real dispatch
change + new state surface — deferred (YAGNI); the synchronous headed-window path fully satisfies the
clarified requirement.

---

## R9 — LinkedIn is browser automation, **not** an AI call; enrichment path unchanged

**Decision**: LinkedIn collection adds **no** AI SDK and makes **no** external AI call — it is
Playwright DOM reading. LinkedIn offers get the same Pending `OfferEnrichment`/`OfferFit`/
`OfferAffinity` satellites at scan-upsert (existing orchestrator code) and are produced by the local
`/enrich` worker exactly like every other offer (002/006). The `NoAiDependencyTests` no-AI-package
guard stays green.

**Rationale**: Preserves Principle IV (no external AI egress) and SC-005 (LinkedIn offers are fully
first-class in fit/affinity) with zero enrichment change. LinkedIn text reaches only the loopback
`/api/enrichment` worker, exactly as other offers do.

**Alternatives rejected**: any server-side parsing/classification via an AI SDK (violates FR / the
no-AI guard); special-casing LinkedIn enrichment (unnecessary — the pipeline is source-agnostic).

---

## Testing strategy (Principle V/VI) — offline & deterministic

- **Never hit live LinkedIn in tests** (network + auth + anti-bot). All backend tests use a **fake
  `ILinkedInClient`** returning canned list/body/login results — mirroring the faked-source pattern
  and heeding the "scan integration tests hit live network" caution.
- Domain/unit: `JobSourceSearch` jsonb round-trip incl. back-compat with old JSON lacking the new
  keys; `ScanContext.AllowInteractiveLogin` mapping (Manual→true; Scheduled/CatchUp/Initial→false).
- Application: `LinkedInSource.CollectAsync` — recommended + search passes dedup to one offer (AC2);
  one failing pass → Partial, others continue (AC3); attended→login attempted vs unattended→
  `Failed(LoginNotCompleted)` with **no** login attempt (FR-011, via fake client + fake context).
- Infrastructure (real Postgres/Testcontainers): seed creates the LinkedIn source
  (InteractiveBrowser, requiresLogin, enabled); scan-with-fake-client upserts LinkedIn offers
  (identity = job id; body captured; Pending satellites created — SC-005); unattended-no-session →
  ScanRun `Failed/LoginNotCompleted` **and prior LinkedIn offers retained** (FR-015/SC-004);
  backup/restore round-trip with a LinkedIn source (config covered) + assert the browser profile is
  NOT in the archive (FR-012a); `NoAiDependencyTests` + `BackupTablesCompletenessTests` still green
  (FR regression contract — no new table, no AI dep).
- The real `PlaywrightLinkedInClient` is verified **manually** per `quickstart.md` (Principle VII —
  run it and look), not in the automated suite.
