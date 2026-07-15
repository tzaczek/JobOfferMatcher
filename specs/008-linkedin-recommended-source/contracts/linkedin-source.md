# Phase 1 — Contracts: LinkedIn Recommended Jobs Source (008)

Interface/contract deltas only. No new REST endpoint is required (login rides the existing manual
scan). All ports keep Domain-only types crossing them (Principle II).

---

## 1. `IJobSource` — unchanged signature, new implementer

`LinkedInSource : IJobSource` (`Kind => SourceKind.InteractiveBrowser`). Implements the **existing**
port verbatim:

```csharp
Task<CollectionResult> CollectAsync(JobSourceSearch search,
    Func<CollectedOffer, CancellationToken, Task> onOffer, CancellationToken ct);
Task<string?> FetchBodyAsync(CollectedOffer offer, CancellationToken ct);
```

Behavioral contract:
- Reads `AllowInteractiveLogin` from the scoped `ScanContext`; calls
  `ILinkedInClient.EnsureLoggedInAsync(interactive, ct)` first.
  - login fails + unattended → return `CollectionResult.Failed(LoginNotCompleted, 0)` (no offers,
    no window).
  - login fails + attended (timeout) → same `Failed(LoginNotCompleted, 0)`.
- On a valid session, run passes in order and stream each item to `onOffer`:
  1. **Recommended** pass if `search.IncludeRecommended`.
  2. one pass per `search.LinkedInSearches[i]`.
- Aggregate pass outcomes into the worst (`Complete < Partial < Failed`); one pass failing never
  aborts the others (US2 AC3). Bounded by `MaxResultsPerSearch` per pass (FR-013).
- `FetchBodyAsync` → `ILinkedInClient.FetchBodyAsync(jobId, ct)`; returns `null` on any
  failure/block (orchestrator tolerates → "not available").

---

## 2. `IInteractiveBrowserSession` — one new parameter (the deferred port, now exercised)

```csharp
// was: Task<Result<SessionReady>> EnsureLoggedInAsync(SourceId source, CancellationToken ct);
Task<Result<SessionReady>> EnsureLoggedInAsync(SourceId source, bool interactive, CancellationToken ct);
```

- `interactive == false` (unattended): return a valid `SessionReady` iff a persisted session is
  already logged in; otherwise `Failure(LoginRequired)` **without** launching a window.
- `interactive == true` (attended): if not already logged in, launch the **headed** persistent
  context at the recommended feed and poll until logged-in (bounded by `LoginTimeoutMs`/`ct`); return
  `SessionReady` on success, else `Failure(LoginRequired)`.

`NotConfiguredInteractiveBrowserSession` (the disabled/fallback impl) is updated for the new
parameter and continues to return its `NotConfigured` failure.

---

## 3. `ILinkedInClient` (Infrastructure) — new port, TheProtocol-style DI swap

```csharp
public interface ILinkedInClient : IInteractiveBrowserSession
{
    // list one pass (recommended feed or a keyword search), bounded + paced
    Task<LinkedInListResult> FetchListAsync(LinkedInListRequest request, CancellationToken ct);
    // the job detail body for one posting
    Task<string?> FetchBodyAsync(string jobId, CancellationToken ct);
}

public sealed record LinkedInListRequest(bool Recommended, LinkedInSearch? Search, int MaxResults);
public sealed record LinkedInListResult(SourceFetchStatus Status, IReadOnlyList<LinkedInJobCard> Jobs);
public sealed record LinkedInJobCard(string JobId, string Title, string Company, string? Location,
    WorkMode WorkMode, string CanonicalUrl);
```

- `SourceFetchStatus { Ok, Blocked, Failed }` reused from `SourceCollection.cs` — the adapter maps
  `Blocked → Partial/ChallengeDetected`, `Failed → Failed/NetworkFailure`, truncation →
  `Partial/LayoutChanged`.
- DI (mirrors `Sources:TheProtocol:UseBrowser`): `Sources:LinkedIn:UseBrowser` true →
  `AddSingleton<ILinkedInClient, PlaywrightLinkedInClient>()`; false →
  `AddSingleton<ILinkedInClient, NotConfiguredLinkedInClient>()`. `LinkedInSource` depends on
  `ILinkedInClient` + scoped `ScanContext` + `IOptions<LinkedInOptions>` — resolved by
  `JobSourceFactory` via `ActivatorUtilities`.

---

## 4. `ScanContext` (Application, scoped)

```csharp
public sealed class ScanContext
{
    public ScanRunId RunId { get; private set; }
    public TriggerType Trigger { get; private set; }
    public bool AllowInteractiveLogin => Trigger == TriggerType.Manual;
    public void Begin(ScanRunId runId, TriggerType trigger) { RunId = runId; Trigger = trigger; }
}
```

`ScanOrchestrator` (scoped) calls `scanContext.Begin(run.Id, request.Trigger)` at the top of
`RunCoreAsync` — the single orchestrator edit.

---

## 5. REST / DTO deltas (no new endpoint)

- `SourceEndpoints.SearchCriteriaDto` gains `bool? IncludeRecommended` and
  `IReadOnlyList<LinkedInSearchDto>? LinkedInSearches`; `ToSearch`/`ToDto` map them (coalescing null →
  default). `LinkedInSearchDto(Keywords, Location, GeoId, Distance, Recency)`.
- The existing `POST /api/scans/run` (`{ sourceIds? }`, `TriggerType.Manual`) is the **login trigger**
  for LinkedIn — no change. Scheduled scans (BackgroundService) pass `Scheduled`/`CatchUp`/`Initial`.
- `GET /api/scans/{id}/status` unchanged; `incompleteReason: "LoginNotCompleted"` is the "login
  required" signal the UI surfaces.
- No session/cookie material crosses any endpoint (the browser holds it locally) — so no new
  loopback-only endpoint is needed (FR-012).

---

## 6. Frontend contract (minimal)

- `api/types.ts`: extend `SearchCriteriaDto` with `includeRecommended?: boolean` +
  `linkedInSearches?: LinkedInSearchDto[]`. `ScanState` already has `waiting_for_login` +
  `challenge_detected`.
- `pages/Sources/SourcesPage.tsx`: when `kind === 'InteractiveBrowser'`, show an "Include recommended
  feed" checkbox + a small saved-searches editor (keywords / location / geoId / distance / recency).
  Reuses the existing form idiom + design tokens.
- `pages/Offers/ScanBanner.tsx`: during a manual scan of a login-required source, show the
  "A LinkedIn login window opened — finish signing in there" hint; on a finished scan with
  `incompleteReason === 'LoginNotCompleted'`, show "LinkedIn login required — run a manual scan to sign
  in." (Principle VIII — token-driven, no scattered literals.)
  - **The hint is client-driven** (a prop set by `OffersPage` when it scans a login-required source).
    The `GET /api/scans/{id}/status` endpoint does **not** emit `waiting_for_login` — `ToStatus` returns
    only `running`/`completed`/`incomplete` — so do **not** wire the hint to `status.state`. (`ScanState`
    in `types.ts` declares `waiting_for_login`/`challenge_detected`, but no backend path sets them under
    the synchronous-dispatch design — ADR-6.) The login-required message specializes the existing
    `incomplete` branch, which already renders `incompleteReason`.

---

## 7. LinkedIn URL shapes (the user-supplied "what to collect")

- Recommended feed: `https://www.linkedin.com/jobs/collections/recommended/`
  (`discoveryOrigin=JOBS_HOME_JYMBII`).
- Keyword search: `https://www.linkedin.com/jobs/search-results/?keywords={kw}&geoId={geoId}&distance={distance}&f_TPR={recency}`
  (example: `keywords=Senior .NET Software Engineer`, `geoId=90009828`, `distance=50`, `f_TPR=r1296000`
  = last 15 days).
- Job detail (body + identity): `currentJobId={jobId}` / `https://www.linkedin.com/jobs/view/{jobId}/`
  — the numeric `jobId` is the offer identity (`native_key`).
