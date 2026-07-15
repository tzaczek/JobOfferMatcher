---
description: "Task list for LinkedIn Recommended Jobs Source (008)"
---

# Tasks: LinkedIn Recommended Jobs Source

**Input**: Design documents from `specs/008-linkedin-recommended-source/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/linkedin-source.md, quickstart.md

**Tests**: INCLUDED — the constitution mandates real-DB integration tests (Principle V) and
green-before-done (Principle VI); the plan ships a test inventory. Backend tests use a **fake
`ILinkedInClient`** (offline/deterministic — never hit live LinkedIn); the **real**
`PlaywrightLinkedInClient` is verified **manually** per `quickstart.md` (Principle VII).

**Organization**: grouped by user story (US1–US3), each an independently testable increment. **US1
(recommended feed into the offers feed) is the MVP.** US2 layers saved keyword searches onto US1's
adapter/client. US3 layers the login resilience (session reuse, the attended-vs-unattended gate,
graceful degradation, and the login-required UI) on top.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US3 (Setup/Foundational/Polish carry no story label)
- Paths are repo-relative; backend = `backend/src` + `backend/tests`, frontend = `frontend/src` + `frontend/tests`.

**Load-bearing constraints (from plan.md):** LinkedIn is the **first real `SourceKind.InteractiveBrowser`
adapter** (activates the deferred FR-040 path). **Authentication = manual, interactive login in a
headed, persisted Playwright context; the password is NEVER stored/transmitted/logged.** The login
window **auto-launches mid-scan on manual scans and waits**; **scheduled/unattended** scans record
`LoginNotCompleted` (no window, no hang). The session is **excluded from backups** (on-disk, outside
`cv-data/`). **ZERO migrations, ZERO new tables, ZERO new endpoints, ZERO new dependencies** — search
config rides additive `JobSourceSearch` **jsonb**; `job_source` is already backed up. **No external AI
call** (the `NoAiDependencyTests` guard stays green). **No existing data/table/migration is edited.**

---

## Phase 1: Setup (Shared)

**Purpose**: prerequisites that aren't code behaviour.

- [X] T001 [P] Confirm **no new dependency** is needed: `Microsoft.Playwright` 1.61.0 is already
  referenced (`backend/src/Infrastructure/Infrastructure.csproj`); **no NuGet, no npm, no AI SDK**.
  Confirm `browser-profiles/` / `**/.auth/` are already gitignored (`.gitignore:40-43`) and that
  `playwright install chromium` is documented (already in `README.md` + this feature's
  `specs/008-linkedin-recommended-source/quickstart.md`). No code change.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the shared plumbing every story needs — the search config shape, the scoped attended-ness
carrier + the single orchestrator edit, the `IInteractiveBrowserSession` contract change, the
`ILinkedInClient` port + its no-browser fallback, the `LinkedInSource` adapter skeleton + factory arm,
the seeded source, and DI wiring.

**⚠️ CRITICAL**: No user story can begin until this phase is complete.

### Domain (framework-free)

- [X] T002 [P] Create the `LinkedInSearch` value object (immutable record: `Keywords` (required,
  non-blank — validate in a `Create`/guard), `Location` `string?`, `GeoId` `string?`, `Distance`
  `int?`, `Recency` `string?`) in `backend/src/Domain/Sources/LinkedInSearch.cs`.
- [X] T003 [P] Extend `JobSourceSearch` with additive jsonb fields `bool IncludeRecommended` (default
  `false`) and `IReadOnlyList<LinkedInSearch> LinkedInSearches` (default `[]`) in
  `backend/src/Domain/Sources/JobSourceSearch.cs`. **No migration** — the `search_criteria` jsonb
  column absorbs them; existing rows deserialize to defaults (back-compat).

### Application (ports + scoped context)

- [X] T004 [P] Create the scoped `ScanContext` holder (`ScanRunId RunId`, `TriggerType Trigger`,
  `bool AllowInteractiveLogin => Trigger == TriggerType.Manual`, `void Begin(runId, trigger)`) in
  `backend/src/Application/Scanning/ScanContext.cs`.
- [X] T005 [P] Add a `bool interactive` parameter to
  `IInteractiveBrowserSession.EnsureLoggedInAsync(SourceId, bool, CancellationToken)` in
  `backend/src/Application/Scanning/IInteractiveBrowserSession.cs`, and update
  `backend/src/Infrastructure/Sources/Browser/NotConfiguredInteractiveBrowserSession.cs` for the new
  signature (still returns its `NotConfigured` failure).

### Infrastructure (options, client port, fallback)

- [X] T006 [P] Create `LinkedInOptions` (`SectionName = "Sources:LinkedIn"`: `UseBrowser=true`,
  `Headless=false`, `ProfilePath` default `{LocalApplicationData}/JobOfferMatcher/browser-profiles/linkedin`,
  `NavigationTimeoutMs=45000`, `LoginTimeoutMs=180000`, `RequestDelayMs=1500`, `MaxResultsPerSearch=50`,
  `RecommendedUrl`, `SearchUrlTemplate`, `UserAgent`, `Locale="en"`) in
  `backend/src/Infrastructure/Sources/LinkedIn/LinkedInOptions.cs`.
- [X] T007 [P] Create the `ILinkedInClient` port (`: IInteractiveBrowserSession`, plus
  `FetchListAsync(LinkedInListRequest, ct)` and `FetchBodyAsync(string jobId, ct)`) and its records
  (`LinkedInListRequest(bool Recommended, LinkedInSearch? Search, int MaxResults)`,
  `LinkedInListResult(SourceFetchStatus Status, IReadOnlyList<LinkedInJobCard> Jobs)`,
  `LinkedInJobCard(JobId, Title, Company, Location?, WorkMode, CanonicalUrl)`) in
  `backend/src/Infrastructure/Sources/LinkedIn/ILinkedInClient.cs`. Reuse `SourceFetchStatus`/`WorkMode`.
- [X] T008 [P] Create the `NotConfiguredLinkedInClient` fallback (used when `Sources:LinkedIn:UseBrowser=false`
  / offline / CI): `EnsureLoggedInAsync` → `Failure(LoginRequired)`, `FetchListAsync` → empty `Ok`,
  `FetchBodyAsync` → `null`) in `backend/src/Infrastructure/Sources/LinkedIn/NotConfiguredLinkedInClient.cs`.

### Adapter skeleton + wiring

- [X] T009 Create the `LinkedInSource : IJobSource` skeleton (`Kind => SourceKind.InteractiveBrowser`;
  ctor `SourceId id, ILinkedInClient client, ScanContext scanContext, IOptions<LinkedInOptions> options,
  ILogger`; `CollectAsync` calls `client.EnsureLoggedInAsync(id, scanContext.AllowInteractiveLogin, ct)`
  and returns `Failed(LoginNotCompleted, 0)` on failure — pass bodies land in US1/US2; `FetchBodyAsync`
  delegates to `client.FetchBodyAsync(jobId, ct)`) in
  `backend/src/Infrastructure/Sources/LinkedIn/LinkedInSource.cs`. (Depends on T004, T007.)
- [X] T010 Route `SourceKind.InteractiveBrowser => ActivatorUtilities.CreateInstance<LinkedInSource>(...)`
  (replacing the `NotConfiguredInteractiveBrowserSource` arm) in
  `backend/src/Infrastructure/Sources/JobSourceFactory.cs`. (Depends on T009.)
- [X] T011 Inject `ScanContext` into `ScanOrchestrator` and call `scanContext.Begin(run.Id, request.Trigger)`
  at the start of `RunCoreAsync` (the single orchestrator edit) in
  `backend/src/Application/Scanning/ScanOrchestrator.cs`. (Depends on T004.)
- [X] T012 DI wiring: in `backend/src/Application/DependencyInjection.cs`, `AddScoped<ScanContext>()`;
  in `backend/src/Infrastructure/DependencyInjection.cs`, `Configure<LinkedInOptions>(...)` and register
  the **`NotConfiguredLinkedInClient`** as the default `ILinkedInClient` (also exposed as
  `IInteractiveBrowserSession`) so the app resolves before the Playwright impl exists. **T020 replaces
  this with the `Sources:LinkedIn:UseBrowser`-gated swap** (Playwright when true, NotConfigured when
  false) — so this task only references types that already exist (T004/T006/T007/T008). (Depends on
  T004, T006, T007, T008.)
- [X] T013 Parameterize `SeedSourceAsync` with `SourceKind` + `requiresLogin` (+ `enabled`), add
  `DefaultLinkedInSourceId = SourceId.From(new Guid("44444444-4444-4444-4444-444444444444"))`, and seed
  one **LinkedIn** source (`InteractiveBrowser`, `requiresLogin: true`, **`enabled: false`** — revised
  from `true`; see data-model §2), search
  `{ IncludeRecommended = true, LinkedInSearches = [ "Senior .NET Software Engineer" starter ] }`)
  idempotently in `backend/src/Infrastructure/Persistence/Seed/DatabaseSeeder.cs`. (Depends on T003.)
  - **Discovered constraint:** seeding it **enabled** made every unattended scan-all `Failed` (login not
    completed), which — via the run-level-gated `GetLastCompleteForSourceAsync` — disabled the FR-015
    sanity guard for the other sources (a real regression caught by `DisappearanceReconciliationTests`).
    Seeded **disabled**; the user enables it when ready to log in.

### Foundational tests

- [X] T014 [P] Domain test: `JobSourceSearch` jsonb round-trip **and** back-compat (deserialize legacy
  JSON lacking the new keys → `IncludeRecommended=false`, `LinkedInSearches=[]`) in
  `backend/tests/Domain.Tests/JobSourceSearchJsonbTests.cs`.
- [X] T015 [P] Domain test: `ScanContext.AllowInteractiveLogin` mapping (Manual→true;
  Scheduled/CatchUp/Initial→false) and the `LinkedInSearch` non-blank-`Keywords` invariant in
  `backend/tests/Domain.Tests/ScanContextAndLinkedInSearchTests.cs`.
- [X] T016 [P] Infrastructure (real Postgres) test: the seeder creates the LinkedIn source
  (InteractiveBrowser, requiresLogin, enabled) idempotently across restarts, **and** the regression
  guards stay green — `NoAiDependencyTests` (no AI SDK added) and `BackupTablesCompletenessTests` (no
  new mapped table) in `backend/tests/Infrastructure.Tests/Sources/LinkedInSeedTests.cs`.

**Checkpoint**: Foundation ready — the LinkedIn source exists (routing to a login-gated adapter), the
attended-ness gate + search config are in place; user stories can begin.

---

## Phase 3: User Story 1 — Personalized recommendations into the feed (Priority: P1) 🎯 MVP

**Goal**: A manual scan (with the user logged in) surfaces the personalized LinkedIn *Recommended*
jobs in the offers feed, deduped and first-class in the pipeline.

**Independent Test**: With a logged-in session (real) or a fake `ILinkedInClient` (test), run a manual
scan → recommended offers appear with title/company/location/work-mode/link + body; a re-scan produces
no duplicates; each offer gets Pending enrichment/fit/affinity satellites.

### Tests for User Story 1 ⚠️

- [X] T017 [P] [US1] Application test: `LinkedInSource.CollectAsync` recommended pass with a fake
  `ILinkedInClient` streams each card to `onOffer`, and calls `EnsureLoggedInAsync` with the
  `ScanContext.AllowInteractiveLogin` value, in `backend/tests/Application.Tests/LinkedInSourceTests.cs`.
- [X] T018 [P] [US1] Infrastructure (real Postgres) test: a manual scan with a fake client upserts
  LinkedIn offers keyed by `(source_id, LinkedIn job id)` with the body captured and Pending
  enrichment/fit/affinity satellites created (SC-005); a second scan of the same jobs adds **zero**
  duplicates (SC-002), in `backend/tests/Infrastructure.Tests/Sources/LinkedInScanFlowTests.cs`.

### Implementation for User Story 1

- [X] T019 [US1] Implement the recommended pass in `LinkedInSource.CollectAsync`: on a valid session,
  `FetchListAsync(new LinkedInListRequest(Recommended: true, null, MaxResultsPerSearch))`, map each
  `LinkedInJobCard` → `CollectedOffer` (`ExternalRef` NativeId = `JobId`; `OfferContent`
  title/company/location/workMode/canonicalUrl), stream to `onOffer`, aggregate the pass status into a
  `CollectionResult`, in `backend/src/Infrastructure/Sources/LinkedIn/LinkedInSource.cs` (+ a small
  `LinkedInMapper` if the mapping grows). (Depends on T009.)
- [X] T020 [US1] Implement `PlaywrightLinkedInClient` (singleton, `SemaphoreSlim` gate,
  `IAsyncDisposable` — the `PlaywrightTheProtocolClient` shape) using
  `Chromium.LaunchPersistentContextAsync(ProfilePath, { Headless=false })`: `EnsureLoggedInAsync`
  (navigate `RecommendedUrl`; poll login-detect; `interactive=true` → keep the headed window open and
  wait for the logged-in signal bounded by `LoginTimeoutMs`/`ct`; `interactive=false` → `LoginRequired`
  with no window), `FetchListAsync(Recommended)` (bounded to `MaxResultsPerSearch`, paced ~`RequestDelayMs`,
  DOM-extract jobId/title/company/location/workmode/url), and `FetchBodyAsync(jobId)` (detail-pane text)
  in `backend/src/Infrastructure/Sources/LinkedIn/PlaywrightLinkedInClient.cs`. **Register it as the
  `ILinkedInClient` + `IInteractiveBrowserSession` via the `Sources:LinkedIn:UseBrowser`-gated swap** in
  `backend/src/Infrastructure/DependencyInjection.cs` (`UseBrowser=true` → `PlaywrightLinkedInClient`,
  else the `NotConfiguredLinkedInClient` default from T012). (Depends on T006, T007, T012.)
- [ ] T021 [US1] **Manual visual verification** (Principle VII) per `quickstart.md` US1: `./start.ps1`,
  `playwright install chromium` once, trigger a manual LinkedIn scan, complete the **headed** login,
  confirm personalized recommended offers land in the feed with body + pending→produced signals, and a
  re-scan doesn't duplicate. Record the result (or state explicitly if the headed browser can't launch
  in-session).
  - **Deferred (Principle VII, stated not claimed):** a headed Chromium + a live LinkedIn login is not
    available in this automated session. The real path is exercised by hand per `quickstart.md`. The
    offline behaviour is fully covered by the fake-`ILinkedInClient` suite (T017/T018).

**Checkpoint**: US1 is fully functional — recommended LinkedIn jobs are first-class in the feed (MVP).

---

## Phase 4: User Story 2 — Configure & run LinkedIn keyword searches (Priority: P2)

**Goal**: The user configures LinkedIn keyword searches (keywords/location/distance/recency) on the
LinkedIn source and their results collect into the feed; a job in both the feed and a search is one
offer.

**Independent Test**: Configure a saved search, scan → matching postings appear; a job in both the
recommended feed and the search yields a single offer; with two searches, one failing pass leaves the
scan Partial while the other pass still collects.

### Tests for User Story 2 ⚠️

- [X] T022 [P] [US2] Application test: recommended + saved-search passes stream into one `context`
  and a job appearing in both dedups to a single `onOffer` identity (US2 AC2); a pass whose fake client
  returns `Blocked`/throws → the source result is `Partial` and the other pass's offers still stream
  (US2 AC3), in `backend/tests/Application.Tests/LinkedInSourceSearchTests.cs`.
- [X] T023 [P] [US2] Frontend test (Vitest/RTL): the source editor shows + saves the LinkedIn fields
  (Include-recommended + a saved search) when kind is `InteractiveBrowser`, in
  `frontend/tests/sources/LinkedInSourceEditor.test.tsx`.

### Implementation for User Story 2

- [X] T024 [US2] Add the saved-search passes to `LinkedInSource.CollectAsync`: after the recommended
  pass, iterate `search.LinkedInSearches`, `FetchListAsync(Recommended: false, Search: s, …)` for each,
  stream into the **same** `onOffer` (cross-pass dedup via the orchestrator's `context.Seen`), and
  aggregate the worst pass outcome (`Complete < Partial < Failed`) with per-pass tolerance, in
  `backend/src/Infrastructure/Sources/LinkedIn/LinkedInSource.cs`. (Depends on T019.)
- [X] T025 [US2] Implement `PlaywrightLinkedInClient.FetchListAsync(Search)`: build the search URL from
  `SearchUrlTemplate` (keywords/geoId/distance/recency), bounded + paced extraction, in
  `backend/src/Infrastructure/Sources/LinkedIn/PlaywrightLinkedInClient.cs`. (Depends on T020.)
- [X] T026 [US2] Extend `SearchCriteriaDto` with `IncludeRecommended` + `LinkedInSearches`
  (`LinkedInSearchDto(Keywords, Location, GeoId, Distance, Recency)`) and map them in `ToSearch`/`ToDto`
  in `backend/src/Web/Endpoints/SourceEndpoints.cs`. (Depends on T003.)
- [X] T027 [P] [US2] Extend the frontend `SearchCriteriaDto` type (+`includeRecommended?`,
  +`linkedInSearches?`) in `frontend/src/api/types.ts` and carry them through create/update in
  `frontend/src/api/sources.ts`.
- [X] T028 [US2] Extend `SourcesPage` so that, when `kind === 'InteractiveBrowser'`, it renders an
  "Include recommended feed" checkbox + a saved-searches editor (keywords/location/geoId/distance/recency),
  reusing the existing form idiom + design tokens, in `frontend/src/pages/Sources/SourcesPage.tsx`.
  (Depends on T027.)
- [ ] T029 [US2] **Manual visual verification** (Principle VII) per `quickstart.md` US2: add a saved
  search, scan, confirm matching offers appear and a job present in both feed + search is a single offer.
  - **Deferred (Principle VII):** needs a live LinkedIn login (headed browser), not available in this
    automated session. Cross-pass dedup + per-pass tolerance are covered offline by T022; the editor UI
    by T023.

**Checkpoint**: US1 + US2 both work — recommended feed and configurable keyword searches, deduped.

---

## Phase 5: User Story 3 — Log in once, stay logged in, degrade gracefully (Priority: P3)

**Goal**: The session is reused across scans; a manual scan auto-launches login when needed while a
scheduled scan records "login required" without a window or a hang; blocks degrade to Partial and never
drop prior offers; the UI surfaces the login state.

**Independent Test**: After one login, repeated manual scans need no re-login; an unattended scan with
no session records `incomplete/LoginNotCompleted`, opens no window, doesn't hang, and retains prior
offers; a block yields Partial with prior offers intact; the backup excludes the session.

### Tests for User Story 3 ⚠️

- [X] T030 [P] [US3] Infrastructure test: an unattended (`Scheduled`) scan with a fake client whose
  `EnsureLoggedInAsync(interactive:false)` fails → the source records `Failed`/`LoginNotCompleted`, **no**
  interactive login is attempted, and previously collected LinkedIn offers are **retained** (FR-015,
  SC-004), in `backend/tests/Infrastructure.Tests/Sources/LinkedInLoginGateTests.cs`.
- [X] T031 [P] [US3] Infrastructure test: a fake client returning `Blocked` mid-collection → the source
  is `Partial`/`ChallengeDetected`, and a `<50%`/layout signal → `Partial`/`LayoutChanged`; prior offers
  are never mass-marked unavailable (Complete-gated reconciliation + sanity guard), in
  `backend/tests/Infrastructure.Tests/Sources/LinkedInDegradationTests.cs`.
- [X] T032 [P] [US3] Infrastructure test: a backup/restore round-trip with a configured LinkedIn source
  restores its **config** (row in the already-covered `job_source`) and the archive does **not** contain
  the browser profile dir (FR-012a), in `backend/tests/Infrastructure.Tests/Backup/BackupExcludesLinkedInSessionTests.cs`.
- [X] T033 [P] [US3] Frontend test (Vitest/RTL): `ScanBanner` shows a **client-driven** "finish LinkedIn
  login in the opened window" hint while a login-required source is being scanned (driven by a prop —
  **not** `status.state`, since the backend `/scans/{id}/status` never emits `waiting_for_login`), and
  specializes the existing `incomplete` branch to "LinkedIn login required — run a manual scan to sign
  in" when `incompleteReason === 'LoginNotCompleted'`, in `frontend/tests/offers/ScanBannerLogin.test.tsx`.

### Implementation for User Story 3

- [X] T034 [US3] In `PlaywrightLinkedInClient`: skip login when the persisted session is already
  logged-in (session reuse — SC-003), and detect an anti-automation/checkpoint wall or a login timeout →
  return `SourceFetchStatus.Blocked` / `LoginRequired`; ensure `LinkedInSource` maps `Blocked` →
  `Partial/ChallengeDetected`, list truncation → `Partial/LayoutChanged`, and unattended-no-session →
  `Failed/LoginNotCompleted` (in `backend/src/Infrastructure/Sources/LinkedIn/PlaywrightLinkedInClient.cs`
  + `LinkedInSource.cs`). (Depends on T020, T024.)
- [X] T035 [US3] Extend `ScanBanner` to surface the login hint + the login-required message. **The
  backend `/scans/{id}/status` never emits `waiting_for_login`** (`ToStatus` returns only
  `running`/`completed`/`incomplete`, and the manual POST is synchronous), so drive the hint
  **client-side** — add an `awaitingLogin`/`sourceRequiresLogin` prop set by `OffersPage` when it
  triggers a scan of a login-required source — **not** from `status.state`. Specialize the existing
  `incomplete` branch (which already renders `incompleteReason`) so `incompleteReason ===
  'LoginNotCompleted'` reads "LinkedIn login required — run a manual scan to sign in." Use design tokens
  (Principle VIII). Touches `frontend/src/pages/Offers/ScanBanner.tsx` + the `OffersPage` call site.
  (Depends on T027.)
- [ ] T036 [US3] **Manual visual verification** (Principle VII) per `quickstart.md` US3: confirm session
  reuse across manual scans; simulate an unattended scan with no session → `login required`, no window,
  no hang, prior offers retained; complete a manual re-login; resolve a checkpoint in the headed window.
  - **Deferred (Principle VII):** the durable-session / checkpoint flows need a live headed LinkedIn
    session, not available in this automated session. The unattended→`LoginNotCompleted`+retained-offers,
    block→`ChallengeDetected`, and thin→`LayoutChanged` behaviours are covered offline by T030/T031; the
    session-exclusion-from-backup by T032; the login-required banner by T033.

**Checkpoint**: All three stories work independently; login is durable and degrades safely.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T037 [P] Docs: ensure `README.md` + `quickstart.md` note the one-time `playwright install chromium`,
  the **headed** login window, the OS-app-data profile path, and that the session is **excluded** from
  backups (FR-012a).
- [X] T038 [P] Confirm any new UI state styling (login hint/badge) is a design token / shared class, not
  a scattered literal (Principle VIII), in `frontend/src` (tokens/base css + the touched components).
- [X] T039 Confirm `.gitignore` covers the browser profile and that no session/cookie/secret is
  committed or logged (Principle IV) — a quick audit of the profile path + logging in
  `PlaywrightLinkedInClient`.
- [X] T040 Run the full `quickstart.md` validation across US1–US3 and confirm the **green suite**
  (Principle VI), including the untouched regression contract: `NoAiDependencyTests`,
  `BackupTablesCompletenessTests`, and the 001–007 suites.
  - **Result:** Domain 99/99, Application 74/74, Frontend 99/99 (+ tsc/oxlint/prettier), all 16 new
    LinkedIn tests, `NoAiDependencyTests` + `BackupTablesCompletenessTests` — all green. Full
    Infrastructure suite: 136/142; the 6 red are all pre-existing **live-network** scan-all tests
    (`ScanCollectDisplayTests`, `SourceConfigTests.Disabling`, `RoleGroupingTests` ×2, `UserStatusTests`,
    a `DisappearanceReconciliationTests` case) — proven live-network-only by re-running them with the
    live theprotocol/nofluffjobs seeds off (7/7 green), independent of 008 (LinkedIn seeded disabled →
    scan-all == pre-008). See memory `[[scan-integration-tests-hit-live-network]]` +
    `[[scan-sanity-guard-vs-total]]`. The `quickstart.md` **manual** headed-login walkthrough is deferred
    (T021/T029/T036 — no headed browser / live LinkedIn login in this automated session).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all user stories**.
- **User Stories (Phase 3–5)**: all depend on Foundational. US1 is the MVP; US2 depends on US1's
  adapter/client (T019/T020); US3 depends on US1's client + US2's multi-pass (T020/T024).
- **Polish (Phase 6)**: depends on the desired stories being complete.

### User Story Dependencies

- **US1 (P1)**: after Foundational. Delivers the MVP (recommended feed).
- **US2 (P2)**: after US1 (extends `LinkedInSource`/`PlaywrightLinkedInClient` with search passes).
- **US3 (P3)**: after US1 (login gate/degradation live in the same client/adapter); its UI (T035) only
  needs the shared types (T027).

### Within Each Story

- Tests before/with implementation; the fake-client tests (T017/T018/T022/T030–T032) are offline and
  can be written first; the **real** Playwright path (T020/T025/T034) is verified by the manual tasks
  (T021/T029/T036), not the suite.
- Adapter pass logic (T019/T024) before the manual verify; client reads (T020/T025) before degradation
  handling (T034).

### Parallel Opportunities

- Setup: T001 alone.
- Foundational: **T002, T003, T004, T005, T006, T007, T008 are all [P]** (distinct files); then T009 →
  T010, and T011/T012/T013 in parallel; foundational tests **T014, T015, T016** in parallel.
- US1: tests **T017, T018** in parallel; then T019/T020.
- US2: **T022, T023, T027** in parallel; T024/T025/T026 (backend) parallel with T028 (frontend, after T027).
- US3: tests **T030, T031, T032, T033** all in parallel; impl T034 (backend) parallel with T035 (frontend).

---

## Parallel Example: Foundational

```bash
# Distinct-file foundational pieces together:
Task: "Create LinkedInSearch value object in backend/src/Domain/Sources/LinkedInSearch.cs"          # T002
Task: "Extend JobSourceSearch with jsonb fields in backend/src/Domain/Sources/JobSourceSearch.cs"   # T003
Task: "Create scoped ScanContext in backend/src/Application/Scanning/ScanContext.cs"                 # T004
Task: "Add interactive param to IInteractiveBrowserSession + update the stub session"               # T005
Task: "Create LinkedInOptions in backend/src/Infrastructure/Sources/LinkedIn/LinkedInOptions.cs"    # T006
Task: "Create ILinkedInClient port + records"                                                       # T007
Task: "Create NotConfiguredLinkedInClient fallback"                                                 # T008
```

## Parallel Example: User Story 3 tests

```bash
Task: "Unattended-no-session → Failed/LoginNotCompleted + prior offers retained (T030)"
Task: "Block → Partial/ChallengeDetected; layout/<50% → Partial/LayoutChanged (T031)"
Task: "Backup excludes the LinkedIn session profile (FR-012a) (T032)"
Task: "ScanBanner shows client-driven login hint + LoginNotCompleted message (T033)"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → Phase 2 Foundational (CRITICAL — blocks all stories).
2. Phase 3 US1 → **STOP and VALIDATE** (`quickstart.md` US1: headed login → recommended offers in the
   feed, deduped, first-class).
3. Demo the MVP: personalized LinkedIn recommendations flowing into the feed via the user's own login,
   no stored password.

### Incremental Delivery

1. Setup + Foundational → the LinkedIn source exists (login-gated adapter, search config, attended gate).
2. US1 → recommended feed (MVP). 3. US2 → configurable keyword searches (deduped). 4. US3 → durable
   login + graceful degradation + login UI. Each story adds value without breaking the previous.

---

## Notes

- [P] = different files, no dependency on an incomplete task; [Story] maps a task to its user story.
- **Never hit live LinkedIn in automated tests** — use a fake `ILinkedInClient`; verify the real
  Playwright path by hand (Principle VII).
- **Zero migrations / zero new tables / zero new endpoints / zero new deps** — keep it so (a new EF
  migration or `BackupTables` change would signal a design drift from the plan).
- Commit after each task or logical group (Conventional Commits); never commit the browser profile,
  session cookies, or any secret (Principle IV).
