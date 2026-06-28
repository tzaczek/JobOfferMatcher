# 0003 — Scheduler: in-process `BackgroundService` + Cronos poll-tick

**Status:** Accepted (ADR-3 in `plan.md`; `research.md` §3).

## Context

The app must run scans **≥3×/day on a configurable cron**, on demand via the API, and
**independently of the UI** (whether or not the SPA is open). It must also handle missed windows
correctly: after the machine has been asleep or the process was stopped, a single **catch-up**
run should fire — not a replay of every missed window. This must be testable deterministically.

## Decision

Use a plain .NET **`BackgroundService` + Cronos 0.13.0**, with schedule config and append-only
run history stored in our own PostgreSQL schema. Catch-up is implemented as a **short poll-tick**
(a `PeriodicTimer` every 30–60s), not a long compute-next-then-sleep:

```text
prev = cron.GetPreviousOccurrence(now /*UTC*/, tz)
if lastRunUtc is null or lastRunUtc < prev:
    run(trigger = firstRun ? Scheduled : CatchUp)
    advance lastRunUtc to prev   # collapse all missed windows into the most recent
```

Supporting choices: inject **`TimeProvider`** for deterministic tests; **validate the cron
string** at the boundary into `Result<T>` so a bad expression never crashes the worker; make the
run + `lastRunUtc` advance **idempotent** (unique key on `(windowUtc, trigger)`) so a mid-step
crash can't double-fire; seed first-run semantics so a fresh install does not fabricate a
catch-up for a pre-install window (Principle III). `POST /api/scans/run` calls the **same**
`IScanRunner`, and scheduled-vs-manual collisions are guarded by a single-flight semaphore.

## Consequences

- One code path unifies catch-up and normal cadence; it is **robust to laptop sleep/resume** (not
  just process restart) and re-reads config edits every tick.
- Schedule + append-only history live in our **strongly-typed** schema (a better fit for "the
  tracker reflects reality" than bolting a listener onto a third-party engine), and are tested
  deterministically against real Postgres.
- The `BackgroundService` runs **only while the host process runs** — so "≥3×/day while the app is
  closed" additionally requires OS autostart (see the README). This applies to any in-process
  scheduler.
- **Quartz.NET 3.x + AdoJobStore** (whose `FireOnceNow` is single-catch-up-then-resume) is the
  documented fallback if hand-rolled correctness becomes a burden; Hangfire and Coravel were
  rejected as over-engineered / lacking persistent catch-up (Principle X).
