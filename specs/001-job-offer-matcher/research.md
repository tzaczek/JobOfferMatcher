# Phase 0 Research: Job Offer Aggregation & CV-Based Matching

**Date**: 2026-06-28 · **Method**: 7 parallel research agents (web-enabled, live-verified) +
an adversarial verification pass on the three riskiest decisions. Corrections from that
review are marked **[CORRECTED]** and are authoritative over the first-pass finding.

All decisions respect constitution v1.1.0 (local-first, layered, strongly-typed, append-only,
real-DB tests, YAGNI, no required external service).

---

## 1. Source access — how to collect justjoin.it offers (FR-001/003/007/040)

**Decision**: Collect via justjoin.it's **public, unauthenticated JSON API** on
`api.justjoin.it` using a server-side `HttpClient`, behind a pluggable **`IJobSource`** port
(`JustJoinItSource`). Keep a real-browser (Playwright) sibling behind the *same* port as an
**escalation-only** fallback whose adapter is **deferred** (Principle X / FR-040).

- **LIST**: `GET https://api.justjoin.it/v2/user-panel/offers/by-cursor` — returns
  `{ data:[…], meta:{ from, prev, next:{cursor,itemsCount}, totalItems } }`, page size fixed
  at 20. **Pagination footgun**: advance with the **`from`** (offset) query param, **not**
  `cursor`/`page`/`offset` (those are ignored and silently re-return page 1).
- **DETAIL**: `GET https://api.justjoin.it/v1/offers/{slug}` (note **v1**, not v2). Needed
  **only** for the description `body`, `applyUrl`/`companyUrl`, `languages`, `companySize` —
  **[CORRECTED]** skills, full salary (`employmentTypes[]`), `workplaceType`, experience
  level, and all dates are **already in the LIST payload**, so fetch detail **only for
  new/changed offers** (cuts request volume, FR-007).
- **Filter mapping** (the user's saved search → server params): `categories[]=7` (=.NET),
  `experienceLevels[]=mid&experienceLevels[]=senior`, `employmentTypes[]=b2b&…=permanent`,
  `withSalary=true`, `workingTimes[]=full_time`, `orderBy=DESC`, `sortBy=salary`.
- **Workplace filter is client-side**: server `workplace`/`workplaceTypes[]` are ignored
  (only `remote=true` works). Fetch without it and filter on the per-offer `workplaceType`
  (`office|remote|hybrid`), keeping remote+hybrid. Verified: 179 .NET offers → 177 remote/hybrid.
- **Identity**: every offer has a stable `guid` (UUID) and `slug`. Canonical link =
  `https://justjoin.it/job-offers/{slug}`. Multilocation offers share one `guid`/`offerParent.slug`
  — **dedup on `guid`**. **[CORRECTED]** always fetch detail with the **current scan's slug**
  (reposts can change the slug while `guid` is stable; a persisted slug would 404).
- **Salary**: `employmentTypes[]` is an array; one offer can carry both a `b2b` and a
  `permanent` band, each `{from,to,currency,unit,gross}` **plus** vendor-precomputed FX
  (`*Pln/*Eur/*Usd/*Gbp/*Chf`). **[CORRECTED]** store the **original** `{from,to,currency,
  unit,gross}` in our `SalaryBand` VO and **normalize in the domain** — do **not** persist
  the vendor's snapshot FX as ranking truth (Principle III; reproducible ranking).
- **New-vs-seen**: **[CORRECTED]** derive **purely from whether the `guid` exists in our
  store** (FR-012). Source `publishedAt`/`lastPublishedAt` are for **update** detection
  (FR-014) and **recency sort** only — an old `publishedAt` we see for the first time is
  **new to us**.
- **Pagination termination**: **[CORRECTED]** stop when **a page contributes zero new
  `guid`s**, backed by a hard cap `ceil(totalItems/20)+1`; always dedup by `guid`. (The naive
  "next.cursor null or page<20" rule can loop on a full final page with a stale cursor.)
- **Politeness (FR-007)**: no rate-limit headers / no 429 seen across ~40 rapid requests;
  responses are Cloudflare-cached (`s-maxage=10`). Pace ~1 req/s sequentially with Polly
  backoff on 429/5xx; send a **generic non-PII `User-Agent`**; cache; detail only for changed.
- **CORS**: irrelevant — the .NET backend calls server-side.

**Rationale**: a clean public JSON API gives stable ids, structured salary split by basis,
and dates directly — far more reliable than RSC/HTML scraping, and a real browser would be
pure overhead when no login is required (FR-040). The `IJobSource` port satisfies FR-003
(add sources later) without redesign.

**Alternatives rejected**: legacy `justjoin.it/api/offers` (404, dead ~Nov 2023); scraping
the Next.js RSC stream (brittle/obfuscated); HTML scraping (heaviest, layout-fragile);
Playwright as the **primary** path (unneeded; correct only as escalation); third-party
Apify/commercial scrapers (paid, sends data off-machine — violates FR-038).

**Accepted risk [CORRECTED — load-bearing]**: `api.justjoin.it/robots.txt` **does** contain a
`User-agent: *` group ending in `Disallow: /` with a small allowlist (`/sitemap`, `/pricing`,
`/login`, `/register`) that does **not** include the offer endpoints; the `/v2/user-panel/…`
path signals an internal endpoint; the Regulamin/ToS may restrict automation. Recorded as
**ADR-2** in `plan.md`: technically reachable ≠ permitted → accepted as a deliberate, mitigated
risk for a single-user, low-volume, local, non-PII, non-redistributing tool, with an
escalation switch to manual-login on 403/challenge. **User should review the Regulamin.**

---

## 2. Browser automation — manual-login fallback (FR-004/005/006/040, edge cases)

**Decision**: When (and only when) a source blocks direct access, use **Microsoft.Playwright**
for .NET via a **headed persistent context** (`LaunchPersistentContextAsync(userDataDir)`),
behind an Application port `IInteractiveBrowserSession`; the Playwright class lives only in
Infrastructure. **The adapter is deferred** (justjoin.it needs no login); build the port +
escalation trigger now. Key choices **[CORRECTED]** from the review:

- **[CORRECTED] Default to bundled Chromium** (`playwright install chromium`) for
  reproducibility (Principle VI); offer `Channel="chrome"` only as opt-in. The "stock Chrome
  is less likely to trip bot detection" claim is marginal-to-false for headed CDP automation —
  dropped.
- **[CORRECTED] The user's "Done/Continue" click is the authoritative login-complete signal.**
  The selector/URL probe is only an *optional post-Continue verification* that re-prompts on
  failure — not an equal racer (removes the false-positive-probe failure mode and the brittle
  per-source authed-selector requirement from the first cut). Wrap any Playwright wait so its
  `TimeoutException`/"Target closed" is caught once Continue wins (waits take a `Timeout`, not
  a `CancellationToken`).
- **[CORRECTED] Explicit Application-layer single-flight** (a `SemaphoreSlim`/named mutex that
  returns `Result` on contention) — do **not** rely on the OS profile-lock throwing. A
  scheduled scan + an on-demand scan (FR-017 + FR-018) can collide; that must be a clean
  queued/Incomplete result, not a crash.
- **[CORRECTED] The login-handshake coordination registry** (a `TaskCompletionSource` keyed by
  `ScanRunId`) must be a **singleton DI service**, never a `static` mutable dictionary
  (Forbidden list).
- **[CORRECTED] Default the backend↔UI coordination to status *polling*** (Principle X); keep
  SignalR as an optional upgrade. Defer the CAPTCHA selector matrix until a real source needs it.
- **Session reuse / robustness**: persistent `userDataDir` (gitignored, under OS app-data,
  per source) carries cookies/localStorage so login happens at most once per expiry; **never
  store credentials** (FR-005). Always `ctx.CloseAsync()` (don't rely on the user clicking X);
  detect+clear a stale `SingletonLock` before launch; treat disconnect as Cancel; run the
  login/expiry probe at the **start** of every login-gated scan and re-trigger on mid-scan
  401/redirect ("session expires mid-scan" edge case).
- **Anti-bot/CAPTCHA**: detect via 403/429 + known challenge markers (Cloudflare Turnstile,
  reCAPTCHA, hCaptcha), push a `challenge_detected` state, **pause** the scan (never solve/
  guess), and mark `ScanRun` Incomplete(`ChallengeDetected`) if unresolved.
- **Headed-from-scheduler caveat**: a login-gated *scheduled* scan with no human present pops
  a window, waits the timeout, then records Incomplete (FR-036) — acceptable, and a reason the
  host must stay a **foreground** process (a session-0 Windows Service can't render the window).

**Rationale**: Playwright's persistent context is purpose-built for "log in once, reuse the
session, never store credentials"; auto-waiting makes login-success a first-class check;
clean layering keeps Domain framework-free.

**Alternatives rejected**: Selenium (no storage-state snapshot, weaker waiting, driver/version
drift); `Page.PauseAsync()` (opens the dev Inspector, not an end-user prompt); headless +
stored credentials or a CAPTCHA-solving service (violates the constitution + the spec edge
case); embedding the browser in the React UI (a separate real window is simpler/more trusted).

**Note**: version/runtime facts (Playwright 1.61.0, .NET 10 behavior) were not runtime-verified
here — confirm with a spike when the adapter is actually built.

---

## 3. Scheduling — ≥3×/day, on-demand, UI-independent, single catch-up (FR-017/018/019/039)

**Decision**: A plain **.NET `BackgroundService` + Cronos 0.13.0**, with schedule config and
append-only run history in our own PostgreSQL schema. **[CORRECTED]** implement catch-up as a
**short poll-tick**, not a long sleep:

```
// one PeriodicTimer tick every 30–60s (inject TimeProvider for testability):
var prev = cron.GetPreviousOccurrence(now /*UTC*/, tz);
if (lastRunUtc is null || lastRunUtc < prev) {
    await runner.RunAsync(trigger: firstRun ? Scheduled : CatchUp, ct);
    await store.AdvanceLastRunUtc(prev);   // collapse all missed windows into the most-recent
}
```

This unifies catch-up + normal cadence into one code path, is **robust to laptop sleep/resume**
(not just process restart), **picks up config edits every tick**, and avoids the long-`Task.Delay`
and negative-`TimeSpan` crash pitfalls of the "compute-next-then-sleep" loop.

**[CORRECTED] also required**:
- Inject **`TimeProvider`** (built into .NET 10) for `now`/delays → deterministic unit tests.
- **Validate the cron string** at the API/Application boundary → `Result<T>` (wrap
  `CronExpression.Parse`'s `FormatException`) so a bad expression never crashes the worker.
- Make run + `lastRunUtc` advance **idempotent**: give `ScanRun` a unique key on
  `(windowUtc, trigger)` (or write the row + advance in one EF transaction) so a crash mid-step
  can't double-fire a catch-up — aligns with append-only better than at-most-once.
- **First-run semantics**: seed `lastRunUtc` to the current `GetPreviousOccurrence` (or record
  the first run as `Manual`/`Initial`), so a fresh install does **not** fabricate a `CatchUp`
  for a pre-install window (Principle III).
- **On-demand + UI-independence**: `POST /api/scans/run` calls the **same** `IScanRunner`;
  the scheduler is a server-side `BackgroundService`, so scans run whether or not the SPA is
  open. Guard scheduled+manual collisions with the single-flight semaphore.
- **Configurable**: store cron string (e.g. `0 6,13,20 * * *`) + IANA/Windows time zone +
  `Enabled` in a Postgres row, edited via API; the poll-tick re-reads each tick.
- **OS autostart is mandatory for "≥3×/day while the app is closed"**: a `BackgroundService`
  only runs while the host process runs. Install the host as a Windows Service / Task Scheduler
  login-item. (Applies to **all** scheduler libraries.)

**Rationale**: meets every functional requirement with the least machinery, keeps schedule +
append-only history in **our** strongly-typed schema (better fit for "tracker reflects reality"
than bolting a listener onto a third-party engine), is async-all-the-way and trivially testable
against real Postgres.

**Alternatives**: **Quartz.NET 3.18.1 + AdoJobStore** is the documented fallback — its cron
default `FireOnceNow` *is* single-catch-up-then-resume; rejected as primary only for the second
`QRTZ_` schema + non-typed `JobKey`s + no run-history. **Hangfire** (`MisfireHandlingMode.Relaxed`
gives single catch-up) — over-engineered (job server + dashboard), YAGNI. **Coravel** — schedule
state is in-memory only; can't catch up a window missed while the machine was off without
hand-built persistence (at which point it's the Cronos approach). *Honesty note from review*:
the poll-tick **re-implements** the minute-tick model Quartz/Coravel already have — the real
justification is typed-domain/own-schema alignment, not "less code."

---

## 4. CV parsing + transparent local scoring (FR-021/022/023/025/026)

**Decision**: **Fully local, no external LLM by default.**

- **PDF text** → **UglyToad.PdfPig** (Apache-2.0, pure-managed, netstandard2.0) behind an
  Application port `ICvTextExtractor` (Infrastructure-only). Use its **layout-aware** pipeline
  (`ContentOrderTextExtractor` / `NearestNeighbourWordExtractor` + `DocstrumBoundingBoxes`),
  **not** `page.Text` — the user's CV is genuinely two-column (sidebar skills + main column),
  which a naive read interleaves. Detect "no readable CV" (scanned/image PDF) by thresholding
  output (non-whitespace chars < ~200 / words < ~40) → `Result.Failure(NoReadableCvText)` →
  graceful degradation (FR-026).
- **Profile derivation** (local heuristics): a curated **canonical skill catalog**
  (`{CanonicalId, DisplayName, Aliases[]}`, shipped as editable JSON) matched alias-exact
  first, then a **guarded `FuzzySharp` `TokenSetRatio ≥ 90`** on residual tokens (length-guarded
  so Java≠JavaScript). Seniority = max evidenced level (intern…architect). **Salary expectation
  and work-mode/employment prefs come from user config**, not the PDF (typically absent).
- **Transparent 0–100 fit model** (weights in config): **Skills 45, Seniority 20, Work-mode 12,
  Employment 8, Salary-vs-expectation 15**. Each axis returns a 0..1 value **and a human reason
  string**, producing the explicit **matched vs missing** breakdown the clarification requires
  (FR-025). Required vs nice-to-have skills weighted; missing required skills = the gap list.
- **Combined rank** (default sort) = `0.70·fitScore + 0.30·normalizedSalaryScore` (both 0–100);
  user can re-sort by fit / salary / recency (FR-024). Persist the breakdown so the UI shows
  matched/missing without recompute.
- **Graceful degradation** (FR-026): no readable CV → drop CV axes, rank by
  `0.6·normalizedSalary + 0.4·recency`; offers with unknown salary kept (FR-010), sorted last.
- **Opt-in LLM** (OFF by default, feature-flagged, local-first): later enhancements — local
  embeddings (ONNX MiniLM / Ollama) for semantic skill match, OCR for scanned CVs, NL
  explanation — layered **on top of** the transparent rubric, never replacing it.

**Rationale**: PdfPig is the only candidate that is text-extraction-purpose-built, permissively
licensed (no AGPL/producer-line burden), pure-managed, and layout-aware. The weighted rubric is
fully explainable (every point attributable to a named axis + matched/missing item), local, and
simple. Worked examples in the source research show a high-fit (99) and a low-fit (37, with an
explicit gap list) case.

**Alternatives rejected**: iText7 (AGPL/paid); Docnet.Core (native PDFium, x64-only);
PDFsharp (no extraction API); cloud LLM as default (violates local-first PII rule);
pure embedding similarity as the baseline (opaque, no clean matched/missing list).

**Risks**: PdfPig is permanently pre-1.0 — **pin an exact version** behind the port. Heuristic
quality is bounded by catalog coverage (the main accuracy lever). Pick one FuzzySharp lineage
deliberately (JakeBayer 2.0.2 vs Raffinert 4.x — thresholds differ).

---

## 5. Local-first run/deployment shape (React + .NET 10 + PostgreSQL on Windows 11)

**Decision**: **One ASP.NET Core (.NET 10) host process** is the whole app for "run it for real":
serves the Vite-built SPA as static files (`UseDefaultFiles()` + `UseStaticFiles()` +
`MapFallbackToFile("index.html")`), exposes `/api` on the same `localhost` port, runs the
scheduler as a `BackgroundService`, and launches the headed manual-login browser when needed.

- **Dev**: `dotnet run` + the first-party `Microsoft.AspNetCore.SpaProxy` auto-starts the Vite
  dev server (HMR) — single command, single URL. **Run-for-real**: `npm run build` emits the
  SPA into `wwwroot`; ASP.NET Core serves it directly (no Node at runtime).
- **PostgreSQL**: pinned **docker-compose** `postgres:17-alpine` + named volume, password in a
  **gitignored `.env`**; connection string in **.NET user-secrets** (`%APPDATA%\Microsoft\
  UserSecrets`, outside the repo). Native EDB install is the Docker-free fallback.
- **Migrations**: EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x, in Infrastructure,
  **append-only**, applied at startup via `context.Database.MigrateAsync()` (**not**
  `EnsureCreated`, which bypasses migration history — Principle IX).
- **Tests**: `Testcontainers.PostgreSql` spins a throwaway `postgres:17` per run (Principle V);
  optionally `Respawn` to reset between tests.
- **One command**: `start.ps1` → `docker compose up -d db; dotnet run --project backend/src/Web`.
- **[CORRECTED] Use classic `UseStaticFiles()` + `MapFallbackToFile()`** for the SPA — the newer
  `MapStaticAssets()` pipeline does **not** currently compose with `MapFallbackToFile`, so SPA
  deep-link refreshes 404.

**Rationale**: single-process static hosting is the canonical .NET SPA publish model — "run for
real" is one localhost process, keeping all personal data on the machine; the host is also the
scheduler daemon (no second process) and owns the interactive desktop the headed browser needs.

**Alternatives rejected**: Vite-led two-server dev (loses single-command DX; kept as fallback);
native Postgres service (pollutes OS, weaker reset story); embedded-Postgres as the daily store
(test-only, fragile); `EnsureCreated` (no migration history — violates IX); **.NET Aspire**
(extra moving parts, YAGNI); running the host as a Windows Service for the headed login flow
(session-0 isolation hides the window — keep it foreground; OS autostart is a *separate* concern).

**Risks**: Docker Desktop/WSL2 prerequisite (fallback: native EDB + embedded-postgres tests);
auto-migrate-at-startup is fine only because single-user; EF 10 changed JSON mapping (maps .NET
types as JSON complex types) — validate the Offer JSON columns against EFCore.PG 10 notes.

---

## 6. Identity, deduplication & change detection (FR-011..FR-016, FR-009/034)

**Decision**: A **two-key model** — stable IDENTITY separate from mutable CONTENT — plus
completeness-gated reconciliation, conservative cross-source grouping, and append-only history.

1. **Identity (FR-011)**: VO `ExternalRef{ SourceId, NativeKey, IdentityKind }`. `NativeKey` =
   justjoin.it `guid` (preferred) / `slug`; fallback `IdentityKind=FallbackHash` = SHA-256 over
   the **canonical URL path** (tracking stripped) or `normalize(company)|title|location`. DB
   enforces `UNIQUE(source_id, native_key)`; internal `OfferId` (wrapped Guid) is separate.
2. **Change detection (FR-014)**: VO `ContentFingerprint` = SHA-256 over canonical (sorted-key)
   JSON of **normalized Major-tier fields** — title, **as-published** salary (`min|max|currency|
   period|basis`, **not** FX-converted), sorted required skills, sorted nice-to-have, work mode,
   employment type, seniority. Description is a **Minor tier** (versioned, not user-flagged).
   Per scan, lookup by `ExternalRef`: miss → **new** (FR-012); hit + same fingerprint →
   already-seen, bump `LastSeen`, **never re-flag new** (FR-013); hit + different → **updated**,
   append a new `OfferVersion` (FR-014). **New-vs-seen is decided by identity existence, never
   by content or source dates** — `FirstSuggestedAt` set on first surfacing.
3. **Disappearance (FR-015)**: reconcile **only after a `Complete` scan** (full pagination, no
   silent extraction drops). For that source, any `Available` offer not observed in the complete
   scan → `NoLongerAvailable` + `DisappearedAt` + event; reappearance flips back. Partial/Failed
   scans persist sightings but **never** reconcile. **Sanity guard**: downgrade a "Complete" run
   to `Partial` if it returns < ~50% of the previous complete count (configurable) — prevents
   anti-bot/layout breaks from mass-false-killing live offers.
4. **Cross-source dedup (FR-016, SHOULD)**: a **non-destructive `RoleGroup`** cluster — each
   source offer keeps its own identity/link/history; the UI shows one entry per group. Gated:
   **company exact match after normalization** (strip `sp. z o.o.`/`S.A.`/`Ltd`/`GmbH`/`Inc`),
   title token-set ≥ 0.85, location compatible (same city or both remote/hybrid); merge only at
   confidence ≥ 0.85, else **default to not merging** (a missed merge is cheaper than a false
   one). Persisted user override ("same"/"not same") wins. **No ML.**
5. **History (FR-009/034)**: append-only `OfferVersion` (immutable content snapshot per change),
   `OfferObservation` (one row per offer per scan it was seen in → drives last-seen +
   disappearance), `OfferEvent` (lifecycle: FirstSeen, Surfaced/Suggested, Updated,
   BecameUnavailable, Reappeared, user status). "When first seen" = `FirstSeen` event; "when
   suggested" = first `Surfaced` event. User status (FR-031) is append-only and **orthogonal**
   to new-vs-seen (a dismissed offer that later changes becomes "updated" but stays dismissed
   and never re-appears as new).

**Rationale**: the load-bearing insight is that **identity and content must be different
hashes** — otherwise a routine salary edit mints a new identity and re-flags "new", breaking
FR-013/014 and SC-002. Gating disappearance on `Complete` scans is the spec's "careful with
partial runs" requirement. Conservative grouping + manual override handles FR-016 without ML.

**Alternatives rejected**: single combined identity+content hash (breaks new/updated);
raw URL as key (volatile); destructive merge (loses per-source history; false merges hide
roles); ML/embedding dedup (non-deterministic, YAGNI); mark-unavailable-on-any-absence
(false-kills on partial runs); in-place overwrite (violates append-only).

**Scoping assumption to document**: disappearance is per `(Source, configured-search)` — an
offer leaving the *filtered* search (e.g. salary later hidden) is treated as no-longer-available
from the user's view even if technically still live elsewhere.

---

## 7. Best-effort salary normalization (FR-008/010, Out-of-Scope: exact normalization)

**Decision**: Store the **raw** disclosed salary as an immutable `SalaryBand` VO (nullable
`min/max`, `Currency`, `SalaryPeriod`, `EmploymentBasis`, `TaxTreatment`); an Offer holds a
**list** of bands (justjoin.it publishes B2B and Permanent separately). A **pure Domain**
normalizer `Result<NormalizedSalary> Normalize(SalaryBand, SalaryNormalizationSettings)`:

1. range → one figure via configurable `RangePointStrategy` (default **Midpoint**),
2. period → monthly (yearly ÷12, hourly ×assumedHours, daily ×assumedDays — all configurable),
3. currency → base (PLN) via an **editable FX table** with an `FxAsOf` date (**no live FX API**),
4. basis → canonical (default Permanent-gross-equiv) via **one configurable, documented
   B2B↔Permanent factor** (default 0.85; folds the net↔gross + benefits gap into one knob).

Output `NormalizedSalary` carries the comparable monthly `Money`, a **`NormalizationQuality`**
chip (`Reported` / `Estimated` / `RoughEstimate`), and an ordered **`Assumptions` audit trail**.
The normalized value is **derived, never persisted as fact** (recompute when settings change —
FR-035, Principle III/IX). Settings live in a single-row table / local JSON, editable from a
Settings screen.

**UI honesty rules**: always render the **raw band(s) verbatim** as primary (e.g. "18 000–
22 000 PLN net/mo (B2B)"); show normalized only as a secondary "≈ … (est.)" with the quality
chip + expandable assumptions + a standing "rough, not exact" disclaimer; hidden salary →
"Salary not disclosed" (unknown, **never 0**); "sort by salary" is labelled "≈ normalized
(best-effort)" and pushes un-normalizable offers to a trailing group.

**Multiple-bands reducer**: normalize each band; the offer's comparable figure = the band
matching the candidate's preferred `EmploymentBasis` (from CV profile) if set, else **max**
across successfully-normalized bands.

**Rationale**: satisfies FR-008 (capture amount/range/currency/period/basis), FR-010 (nullable →
mark unknown), and the spec's explicit best-effort scope; a pure function over injected settings
keeps Domain framework-free and offline (Principle IV); derived-not-stored honors append-only.

**Alternatives rejected**: single min/max field (flattens B2B vs permanent); currency as enum
(FX table needs open-ended codes → validated ISO-4217 VO); live FX API (violates local-first);
a full Polish PIT/ZUS engine (YAGNI, out of scope); persisting the normalized figure as fact
(staleness, brushes FR-035); throwing on un-normalizable bands (use `Result<T>`).

**Risks**: the B2B↔Permanent factor + FX table are deliberately coarse (cross-basis comparisons
can be off double-digit %); surface `FxAsOf`/quality chip/assumptions and keep everything
user-editable. Reject junk currency codes at construction → `Result.Failure`.
