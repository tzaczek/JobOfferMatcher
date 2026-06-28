# 0001 — Stack lock: React + .NET 10 + PostgreSQL web app

**Status:** Accepted (ADR-1 in `plan.md`; recorded as constitution **v1.1.0**, a MINOR amendment,
per spec clarification Session 2026-06-28).

## Context

The product is a local-first, single-user job-offer aggregator and CV matcher. Before design
could proceed, the technology stack and overall shape needed to be fixed so every later decision
(persistence, hosting, testing) could assume a stable foundation, and so the constitution's
architecture principles had a concrete target.

## Decision

Lock the stack as a **local-first, single-user web application**:

- **Front end:** React 19 + TypeScript via **Vite**, with central design tokens / one theme.
- **Back end:** **.NET 10** / ASP.NET Core 10 — a single host that serves the built SPA from
  `wwwroot`, exposes `/api`, and runs the scheduler as a `BackgroundService`.
- **Database:** **PostgreSQL** via EF Core 10 + Npgsql, with **append-only** migrations applied
  at startup through `MigrateAsync()`.
- **Architecture:** layered `Domain → Application → Infrastructure → Web`, dependencies pointing
  inward; the **Domain layer is framework-free**. Framework dependencies (EF Core, Npgsql,
  HttpClient, PdfPig, Cronos, the deferred Playwright) live **only** in Infrastructure.

This is recorded as constitution v1.1.0.

## Consequences

- All Constitution Check gates in `plan.md` evaluate against this fixed stack and PASS.
- "Run it for real" is a single localhost process — no Node at runtime, no second daemon — which
  keeps all personal data on the machine (Principle IV) and gives the scheduler an interactive
  desktop session when needed.
- Wrapped IDs and value objects (Principle II) and `Result<T>` for expected failures become the
  Domain idiom; no MediatR until cross-cutting concerns actually repeat (YAGNI, Principle X).
- Changing any of these three pillars is a constitution-level amendment, not a casual refactor.
