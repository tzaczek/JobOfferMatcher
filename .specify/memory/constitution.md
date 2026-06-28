<!--
SYNC IMPACT REPORT
==================
Version change: (none) → 1.0.0 (INITIAL RATIFICATION) → 1.1.0 (MINOR: Technology
Stack locked at first /speckit-plan — confirmed as a local-first single-user WEB
app: React front end + .NET 10 back end + PostgreSQL; supersedes the provisional
XAML/SQLite default. No principles added/removed; only the PROVISIONAL stack
section and its Follow-up TODO are resolved.)

Origin: Adapted from the CodeAwareness Constitution v2.1.2. That document
governs a large multi-context .NET/pgvector/Ollama/React-MFE platform; this one
governs a solo-built **job-search application tracker** whose core scope is
**application tracking**. The structure (numbered NON-NEGOTIABLE principles,
locked constraints, Forbidden list, governance + semver amendments) is preserved;
the content is rewritten for this project and right-sized for a single developer.

Principle mapping from the source:
  KEPT & adapted:
    - Clean Architecture / CQRS  → I. Layered Architecture (CQRS-lite, MediatR optional)
    - Strongly-Typed Domain      → II. Strongly-Typed Domain (verbatim spirit)
    - Real Dependencies in Tests → V. Real Database in Tests (DB only; no Ollama/whisper)
    - All Tests Pass Before Merge→ VI. Green Before Done (solo: local green, no CI gate)
    - UI Visual Verification     → VII. UI Changes Require Visual Verification (XAML/desktop)
    - Single Source of Design Truth → VIII. One Source of Design Truth (XAML resources/theme)
    - Immutable History          → XI. Documented Decisions, Immutable History
  ADDED (domain-specific):
    - III. The Tracker Reflects Reality (data integrity + status lifecycle)
    - IV. Personal Data Stays Private and Local (PII/secrets, local-first)
    - IX. Your Data Is Recoverable (append-only migrations, export, backup)
    - X. Simple by Default (YAGNI — explicit counterweight to inherited rigor)
  DROPPED (CodeAwareness-specific; would be cargo-cult here):
    - Bounded contexts / shared-kernel split (single small app)
    - Stryker.NET mutation thresholds, Testcontainers-for-everything
    - MFE bundle budgets, Custom-Element/Shadow-DOM design system
    - Playwright e2e coverage matrix, Stop-hook doc enforcement
    - whisper/Ollama/pgvector stack and their forbidden-mocks rules

Ratified: 2026-06-28
Last amended: 2026-06-28

Follow-up TODOs:
  - [TECH STACK] RESOLVED (v1.1.0, 2026-06-28): stack locked at first /speckit-plan
    for feature 001-job-offer-matcher. Web app — React front end, .NET 10 back end
    (ASP.NET Core), PostgreSQL via EF Core. No external service is required;
    collection runs locally (HTTP + browser automation on the user's machine) and
    any LLM/job-board-API send remains opt-in and OFF by default (Principle IV).
-->

# Job-Search Tracker Constitution

This constitution governs all work in this repository — a single-developer
**job-search application tracker**. It captures the load-bearing principles every
spec, plan, and implementation must respect. For operational details (exact
tooling, code style, commit conventions, how to run the app), defer to
`CLAUDE.md`. The constitution is the *why*; CLAUDE.md is the *how*.

## Core Principles

### I. Layered Architecture, Dependencies Point Inward
The app layers as `Domain → Application → Infrastructure → Presentation`, with
dependencies pointing **inward only**. Domain has zero framework dependencies —
no EF, no UI, no IO. Application orchestrates use cases; Infrastructure owns
persistence and any external calls; Presentation (ViewModels/Views, MVVM) holds
no business logic. Commands mutate and return `Result<Unit>`/`Result<TId>`;
queries are read-only — keep them distinct and keep handlers thin. A formal
mediator (MediatR) and pipeline behaviors are **welcome but optional**: reach for
them when cross-cutting concerns (validation, logging) start repeating, not
before (see Principle X).

### II. Strongly-Typed Domain — No Primitive Obsession
IDs are wrapped (`ApplicationId`, `CompanyId`, `ContactId`); raw `Guid`/`int` IDs
are forbidden in domain and application code. Domain concepts are real types:
`ApplicationStatus`, a `Salary`/`Money` value object with currency, `DateApplied`
— not loose strings and decimals. Value objects are immutable records with
structural equality; aggregates own their consistency boundary and external code
mutates only through the root. Expected failures (e.g. an invalid status
transition) use `Result<T>`; exceptions are reserved for genuinely exceptional
conditions.

### III. The Tracker Reflects Reality (NON-NEGOTIABLE)
This app is the single source of truth for a real, ongoing job search; its data
must be trustworthy. An application moves through a **defined lifecycle**
(e.g. Lead → Applied → Screening → Interview → Offer → Closed[Accepted/Rejected/
Withdrawn]); illegal transitions are rejected in the domain, not silently
allowed. Stage changes are recorded as **append-only history** with timestamps,
so "when did I apply / hear back" is always answerable. Fabricated, placeholder,
or demo records are never committed or persisted as if real; any seed/sample data
is clearly marked as such and is kept out of the live store.

### IV. Personal Data Stays Private and Local (NON-NEGOTIABLE)
The tracker holds personal data: your contact details, recruiter names, salary
figures, interview notes, and your CV. The live database, exports, and any
secrets/credentials are **local-first** and **never committed** — `.gitignore`
covers the DB file, exports, `.env`, and any `appsettings.*.local` files before
the first run. No PII or secrets appear in source, logs, or test fixtures. If a
feature sends data to an external service (an LLM to tailor a CV, a job-board
API), it sends the **minimum necessary**, the decision is recorded (Principle XI),
and it is off by default.

### V. Real Database in Tests
Persistence and queries are tested against a **real database engine**, not mocked
repositories or in-memory fakes that diverge from production behavior. Integration
tests run on real SQLite (or the chosen engine) — provisioned per-test from
migrations — so EF mappings, constraints, and status-transition rules are actually
exercised. Mocks are fine for genuinely external boundaries (clock, an outbound
HTTP client); they are not a substitute for testing real data access.

### VI. Green Before Done (NON-NEGOTIABLE)
A change is "done" only when the full local suite is **green on your machine** —
unit tests, integration tests (real DB), and any build/analyzer checks the
project ships. Local green is the gate; this is a solo project, so there is no CI
badge to defer to. Flaky tests are fixed at the source — retry-until-green and
silent skipping are forbidden; skipping a test requires a written reason and a
tracked follow-up. Mutation testing and coverage tools are encouraged as signal
but are not mandatory gates.

### VII. UI Changes Require Visual Verification
Type checks and unit tests verify code correctness, not feature correctness.
Every UI-affecting change follows: plan → edit → **run the app and look at it** →
confirm the user-visible outcome. For XAML this includes resolving binding errors
before claiming success. Exercise the golden path and the obvious edge cases in
the running app before reporting the change done. If the app cannot be launched
in the current session, say so explicitly rather than claiming it works.

### VIII. One Source of Design Truth
Colors, brushes, spacing, typography, and styles come from **central resource
dictionaries / a single theme**, referenced via `StaticResource`/`DynamicResource`
(or the chosen UI framework's equivalent) — never hardcoded literals scattered
through views. A status color or a control style is defined once and reused.
Off-the-shelf heavyweight UI kits are avoided unless a concrete need justifies one
(Principle X); shared visual primitives live in one place.

### IX. Your Data Is Recoverable
Losing your tracked applications is unacceptable. Database migrations are
**append-only** — once a migration has run against your real data it is never
edited; corrections happen via a new migration. The data is **exportable** to a
portable, human-readable format (JSON/CSV) so it is never trapped in one binary
file or one app version, and a documented backup/export path exists. Destructive
operations (delete, bulk edit) are deliberate and recoverable where reasonable.

### X. Simple by Default (YAGNI)
This is a personal single-user app, and its architecture must stay proportional
to that. Prefer the simplest thing that satisfies the principles: no message
buses, microservices, multi-tenancy, distributed caches, or speculative
abstraction the job tracker does not need. Add structure (a mediator, a new
layer, a generic mechanism) only when a concrete, present problem demands it.
Inherited rigor from larger projects is adopted only where it earns its keep here;
complexity that conflicts with a principle must be justified (see Governance).

### XI. Documented Decisions, Immutable History
Non-obvious decisions are written down where the code lives: a `README` that
explains how to run and back up the app, and short ADR-style notes for choices
that future-you would otherwise re-litigate (why SQLite, the status lifecycle,
any external-service usage). Commits follow Conventional Commits, one logical
change each. Once a decision note is accepted it is superseded by a new note, not
silently rewritten; merged migrations and shipped commits are not retroactively
edited. No `--no-verify`, no force-push to the main branch.

## Additional Constraints

### Technology Stack (LOCKED — 2026-06-28, v1.1.0)
Confirmed at the first `/speckit-plan` for feature `001-job-offer-matcher` per the
spec clarification (Session 2026-06-28): a local-first, single-user **web app**.
- **Backend / Domain**: .NET 10, C# with nullable reference types enabled.
  ASP.NET Core hosts the API and serves the built front end on `localhost`.
- **Front end**: **React** single-page app. Components hold no business logic; view
  logic lives in hooks/state and a typed API-client layer, kept unit-testable.
  (This replaces the provisional XAML/MVVM option. Principles VII and VIII apply to
  the web UI: "run the app and look at it" before claiming a UI change works, and
  colors/spacing/typography/styles come from central design tokens / one theme —
  the web equivalent of XAML resource dictionaries — never scattered literals.)
- **Storage**: **PostgreSQL** via EF Core, a local instance, EF Core append-only
  migrations (Principle IX). (SQLite is no longer the default; PostgreSQL was named
  in the request and is locked here.)
- **Background work**: an in-process .NET Hosted Service runs the scan scheduler
  independently of whether the UI is open (the concrete scheduler is chosen in the
  feature plan).
- **Testing**: xUnit (+ an assertion library), real-DB integration tests on a real
  PostgreSQL engine per Principle V, unit tests for Domain/Application logic and
  for front-end view logic.
- **External services**: none required. Collection runs locally (HTTP + browser
  automation on the user's machine). Any LLM/job-board-API integration is opt-in,
  OFF by default, and must satisfy Principle IV before it ships.

### Quality Gates (locally enforced, solo)
Verified by you on your machine before a change is considered done.
- Nullable reference types **on**; warnings treated as errors in Domain and
  Application projects.
- Domain + Application: a reasonable line-coverage target (≥ ~70%) on the logic
  that matters — status transitions, validation, mappings — not coverage theater.
- Architecture: dependencies point inward (Principle I); a lightweight
  architecture test (e.g. NetArchTest) is encouraged once layers stabilize.
- `.gitignore` demonstrably excludes the live DB, exports, secrets, and `.env`
  before the app is first run (Principle IV).

### Forbidden (review-blocking)
- `Console.WriteLine` / `Debug.WriteLine` left in committed code (use logging).
- `// TODO` without a tracked note or reason.
- Suppressing warnings/nullable (`#pragma warning disable`, `!` null-forgiving)
  without an inline justification.
- Mocking the database instead of testing against a real engine (Principle V).
- Committing the live database, data exports, secrets, PII, or `.env`.
- Hardcoded colors/sizes/styles in views instead of central resources (Principle
  VIII).
- Anemic domain models; generic `IRepository<T>`; static mutable singletons.
- `dynamic`, `.Result`, `.Wait()`, `GetAwaiter().GetResult()` — async all the way.
- Raw `Guid`/`int` IDs in domain or application code (Principle II).

## Development Workflow

### Spec-Driven Flow
1. `/speckit-specify` — write the feature spec under `specs/NNN-{slug}/`.
2. `/speckit-clarify` *(when ambiguity exists)* — resolve before planning.
3. `/speckit-plan` — produce a technical plan aligned with this constitution;
   the **first** plan also confirms and locks the Technology Stack above.
4. `/speckit-tasks` — break the plan into actionable tasks.
5. `/speckit-analyze` *(before implement)* — cross-artifact consistency check.
6. `/speckit-implement` — execute. Each significant user story closes only when
   its slice is green locally (Principle VI) and any UI is visually verified
   (Principle VII) before the next story begins.

Plans must explicitly verify alignment with each Core Principle. A plan that
conflicts with a principle requires either an amendment to this constitution or
rejection of the plan; an unavoidable, justified violation is recorded in the
plan's Complexity Tracking.

### Done Criteria (before merging to the main branch)
Principle VI (full local suite green), Principle VII (UI changes visually
verified), Principle IV (no PII/secrets/DB committed; `.gitignore` honored),
coverage targets met locally, and decision/README notes updated per Principle XI.

## Governance

This constitution supersedes ad-hoc practices and individual preference. All
plans verify compliance; complexity that conflicts with a principle must be
explicitly justified and recorded (an ADR-style note).

Amendments require: (1) a written rationale for the change, (2) update to this
file in the same change, (3) a version bump per semver — MAJOR for
backwards-incompatible principle changes or removals, MINOR for a new principle or
materially expanded guidance, PATCH for clarifications and typo fixes. Locking the
PROVISIONAL Technology Stack for the first time is a MINOR amendment.

`CLAUDE.md` is the runtime guidance file: tactical conventions live there and may
be revised without amending this constitution, provided they remain compatible
with these principles.

**Version**: 1.1.0 | **Ratified**: 2026-06-28 | **Last Amended**: 2026-06-28
