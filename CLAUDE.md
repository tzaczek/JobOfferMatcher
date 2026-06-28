<!-- SPECKIT START -->
**Active feature**: `001-job-offer-matcher` — Job Offer Aggregation & CV-Based Matching.
For technologies, project structure, shell commands, and other context, read the current plan:
`specs/001-job-offer-matcher/plan.md` (with `research.md`, `data-model.md`, `contracts/`,
`quickstart.md` alongside it).

**Locked stack** (constitution v1.1.0): local-first, single-user **web app** — React (Vite,
TypeScript) front end + **.NET 10** (ASP.NET Core) back end + **PostgreSQL** (EF Core,
append-only migrations). Layered: `Domain → Application → Infrastructure → Web`; Domain is
framework-free. Run: `./start.ps1` (docker-compose Postgres + `dotnet run`). Tests: xUnit +
real Postgres (Testcontainers); source adapters tested against recorded JSON fixtures (offline).

**Load-bearing decisions**: collection via the public `api.justjoin.it` JSON API behind an
`IJobSource` port (Playwright manual-login fallback deferred); scheduler = `BackgroundService`
+ Cronos poll-tick with single catch-up; identity vs content are **separate hashes** (new-vs-seen
by identity existence, never source dates); raw salary stored, normalized salary derived; CV
matching fully local (PdfPig + FuzzySharp, 0–100 fit + matched/missing). No PII/secrets/DB/CVs
committed (Principle IV). See plan.md ADR-2 for the source-access accepted-risk.
<!-- SPECKIT END -->
