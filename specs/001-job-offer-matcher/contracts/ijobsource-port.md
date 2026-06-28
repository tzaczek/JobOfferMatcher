# Contract: `IJobSource` port (collection abstraction)

The Application-layer port every job source implements, so sources are added without redesign
(FR-003) and the lightest-reliable-first / escalate-on-block model (FR-040) is uniform. The
port lives in `Application`; adapters live in `Infrastructure`. Domain types only (wrapped IDs,
VOs); no framework types leak across the port.

## Port

```csharp
// Application/Scanning/IJobSource.cs
public interface IJobSource
{
    SourceId Id { get; }
    SourceKind Kind { get; }                 // DirectApi | InteractiveBrowser

    // Collect offers for the source's configured search. Streams normalized offers so the
    // orchestrator can upsert + observe incrementally. Honors polite pacing internally.
    IAsyncEnumerable<CollectedOffer> CollectAsync(
        JobSourceSearch search, CancellationToken ct);
}

// Result of one source collection pass, reported to the orchestrator.
public sealed record CollectionResult(
    ScanOutcome Outcome,                     // Complete | Partial | Failed
    IncompleteReason? Reason,                // set when not Complete (FR-036)
    int CollectedCount);
```

`CollectedOffer` carries the **normalized** offer the matching/dedup layer consumes
(source-agnostic): `ExternalRef`, title, company, `IReadOnlyList<SalaryBand>` (raw),
location, work mode, employment type, seniority, required/nice skills, optional description,
canonical URL, and source dates. The adapter maps the source's native payload → `CollectedOffer`
and wraps the native id into `ExternalRef` **at the boundary** (no raw `Guid`/string IDs past
Infrastructure — Principle II).

## Escalation trigger (FR-040) — built now, adapter deferred

`DirectApi` adapters MUST detect a "blocked" condition and surface it rather than failing
silently:

- HTTP `403`/`429` after polite retry/backoff, or a detected anti-bot/CAPTCHA challenge →
  return `CollectionResult(Partial, Reason: ChallengeDetected, …)` **and** raise a
  `SourceBlocked` signal the orchestrator turns into the manual-login handshake **iff** an
  `InteractiveBrowser` adapter exists for that source.
- Today no source needs login, so the `InteractiveBrowser` adapter is a **deferred stub**
  (`NotConfiguredInteractiveBrowserSource`) behind the same port; the trigger + handshake
  contract exist so adding it later is additive (Principle X / research §1–§2).

```csharp
// Application/Scanning/IInteractiveBrowserSession.cs  (port; Playwright adapter deferred)
public interface IInteractiveBrowserSession
{
    // Opens a HEADED browser, prompts the user (UI shows "waiting for manual login"),
    // and completes when the user clicks Done (authoritative) — research §2.
    Task<Result<SessionReady>> EnsureLoggedInAsync(SourceId source, CancellationToken ct);
}
```

## Orchestration responsibilities (Application, not the adapter)

The `ScanOrchestrator` owns the cross-cutting rules so adapters stay thin:

1. **Single-flight**: an explicit `SemaphoreSlim`/named-mutex guard; concurrent scan requests
   return `Result.Failure(ScanInProgress)` (not a profile-lock crash). 
2. **New / updated / unchanged**: lookup by `ExternalRef`; decide via identity existence +
   `ContentFingerprint` diff (data-model §Offer). Never decide new-vs-seen from source dates.
3. **Observation + history**: append `OfferObservation` per seen offer; append
   `OfferVersion`/`OfferEvent` on change; bump `LastSeen`.
4. **Disappearance**: only when `Outcome == Complete` (with the <50% sanity guard) reconcile
   absent offers → `NoLongerAvailable` (FR-015).
5. **Role grouping**: attach to a `RoleGroup` via the conservative deterministic gate (FR-016).
6. **Run record**: write the immutable `ScanRun` with counts + outcome + reason (FR-020),
   keyed `UNIQUE(window_utc, trigger)` for idempotent catch-up.

## Contract tests (Infrastructure.Tests, offline)

Run against **checked-in recorded JSON fixtures** (sanitized list + detail samples) — no live
calls in the normal suite (Principles V/VI). Required assertions:

- mapping: a recorded list/detail payload → expected `CollectedOffer` (skills, both salary
  bands with basis, dates, canonical URL, `guid`→`ExternalRef`).
- category map `7 ↔ "net"` holds (guards an upstream remap).
- `workplaceType` client-filter keeps remote+hybrid and **flags/keeps an UNKNOWN new value**
  (never silently drops).
- pagination: terminates on "zero new guids" + respects the `ceil(total/20)+1` cap; dedups by
  `guid`; over-range `from=` returning page 1 does not loop.
- a `403` fixture yields `Partial/ChallengeDetected` and raises `SourceBlocked`.
- at most ONE explicitly opt-in "live smoke" test (skipped by default).
