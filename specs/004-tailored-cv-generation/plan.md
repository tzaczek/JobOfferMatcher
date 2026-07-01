# Implementation Plan: Tailored CV per Job Offer

**Branch**: `004-tailored-cv-generation` (spec directory; no git branch created — repo has no `before_*` hook)
| **Date**: 2026-06-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/004-tailored-cv-generation/spec.md` (with Clarifications
Session 2026-06-30: source = the feature-002 uploaded CV; download = PDF only; one tailored CV per
offer / latest-only; the editable prompt holds instructions + emphasised skills while the source CV is
an attached, visible, read-only input).

**Constitution**: `.specify/memory/constitution.md` v1.1.0 — **not amended**. This feature **extends**
the 002 "Claude-as-worker" decision to a new output kind (a tailored CV); Principle IV is upheld and
Principle IX is respected. Feature-specific decisions are recorded as ADR-1..ADR-4 (Principle XI).

## Summary

Let the user generate a **CV tailored to one specific offer** — emphasising that posting's skills —
from a **transparent, editable prompt**, then download it as a polished PDF; store it, and reach it
both from the offer and from a dedicated page. This is a **fourth output kind** layered onto the exact
002 pattern: the backend is a passive local store + **queue**, and the user's own **Claude Code worker
under the Max plan** is the sole generator (the backend makes **no** external AI call — FR-005,
mirrors 002 FR-012/SC-005). The one genuinely new mechanism is **HTML→PDF rendering**, done in-process
with the **already-present Playwright/Chromium** (feature 001's scraper) — so **no new dependency**.

Technical approach (from [research.md](./research.md), grounded in the live 001/002/003 code via a
four-way parallel codebase investigation, 2026-06-30):

- **Opt-in, offer-keyed, latest-only satellite** `tailored_cv` (PK `OfferId` = FK → `offers(id)`
  cascade), modelled on `OfferFit` but **created on demand** (not eagerly materialised for every
  offer). It holds the editable `Prompt`, the `EmphasisedSkills` (jsonb), the `SourceCvId`, a
  `TailoredCvState` (`Pending|Produced|Failed`) + `Attempts`, a monotonic `GenerationVersion`
  supersede guard, the produced `HtmlFileName`/`PdfFileName`, `GeneratedAt`, and `LastError`. Regenerate
  overwrites in place (one CV per offer — Clarification). **One** new append-only migration `TailoredCv`.
- **Generation-version guard instead of input-hash staleness** (ADR-4). A tailored CV is **user-driven**
  (the user edits the prompt and clicks regenerate) — it is **not** auto-invalidated by CV/offer/weight
  changes, so 002's recompute-on-write-back hashing is unnecessary. Each (re)generate bumps
  `GenerationVersion` and sets `Pending`; the worker echoes the version; the write-back is accepted only
  when `version == current && state == Pending` (a later regenerate harmlessly discards a slow worker's
  stale result, like 002's `stale` outcome).
- **Worker stays text-only** (ADR-2). A new re-runnable slash command **`.claude/commands/tailor-cv.md`**
  (`/tailor-cv`) drains a loopback-only `/api/tailored-cv` queue: it reads the **source CV PDF by local
  path** (exactly as `/enrich` does — the binary never traverses HTTP, FR-005/Principle IV), reads the
  `cv_versions/` layout recipe (`v2_two_column.html` + `NOTES.md`) from the repo, follows the (possibly
  edited) prompt + emphasised skills, and returns **tailored HTML**. It fabricates nothing — only
  re-emphasises real CV content (FR-006).
- **Backend renders HTML→PDF in-process via Playwright** (ADR-1). On write-back the `TailoredCvService`
  renders the returned HTML to an A4 PDF with `IPage.PdfAsync` through a new `IPdfRenderer` port
  (`PlaywrightPdfRenderer`, a singleton mirroring `PlaywrightTheProtocolClient`'s lazy Chromium launch),
  stores **both** the HTML (regeneratable source) and the PDF (download artifact), and marks `Produced`.
  A render failure is a `RecordFailure` (no PDF ⇒ not downloadable). **No new NuGet package** — Playwright
  is already referenced by Infrastructure.
- **Flat storage under `cv-data/` so 003 backs it up for free** (ADR-3). Generated files are written as
  **flat** `tailored-{OfferId:N}.html` / `tailored-{OfferId:N}.pdf` in the **same `cv-data` root** as the
  uploaded CVs, via a small `ITailoredCvFileStore` resolving the identical `Cv:StoragePath ?? {BaseDir}/
  cv-data` directory. 003's `LocalCvFileStore.EnumerateAll()` is **top-level only** — flat files are
  captured by the existing backup zip + atomic restore swap with **zero changes** to 003's file handling.
  The DB half needs exactly one edit: add `"tailored_cv"` to `BackupTables.InsertOrder` (after `offers`).
  A **new completeness guard test** (model tables == `BackupTables.InsertOrder`) closes the silent-omission
  gap that exists today, permanently protecting FR-017.
- **Endpoints — a loopback-only `/api/tailored-cv` group** (ADR-2/IV) reusing `LoopbackOnlyFilter`:
  worker `GET /pending` + `POST /results`; UI `GET /` (list, dedicated page), `GET /offer/{id}` (reopen),
  `GET /offer/{id}/draft` (the server-assembled default prompt + emphasised-skills selection + the
  attached source-CV reference — FR-013/FR-003), `POST /offer/{id}` (create/regenerate),
  `GET /offer/{id}/download` (stream the PDF — the only place CV-derived bytes leave over loopback, to the
  local user who owns them), `DELETE /offer/{id}`.
- **Quiesce with the existing `MaintenanceGate`** (FR of 003 reused): the tailored-CV write paths
  (`SubmitResultsAsync`, the generate/regenerate command) call `maintenance.WaitWhileActiveAsync(ct)` —
  the same defer pattern as `EnrichmentService.SubmitResultsAsync` — so a restore's `TRUNCATE tailored_cv`
  + `cv-data` swap is consistent. No change to 003's gate.
- **UI**: a "Tailor CV" action on `OfferCard` opens a modal (cloned from the `ApplyModal` portal/
  focus-trap pattern) showing the emphasised-skills chips (toggleable), the attached source-CV name, and
  the editable prompt `<textarea>`, with Generate / Regenerate / Download; a new **`/tailored-cvs`** page
  (sibling of the CV page) lists all tailored CVs. PDF download reuses the `backup.ts` blob→`<a download>`
  pattern. New status surfaced with the existing `enrichmentStatusClass` chips (pending/produced/failed).

The full design is in [research.md](./research.md), [data-model.md](./data-model.md),
[contracts/tailored-cv-api.md](./contracts/tailored-cv-api.md),
[contracts/worker-protocol.md](./contracts/worker-protocol.md), and [quickstart.md](./quickstart.md).

## Technical Context

**Language/Version**: C# 14 on **.NET 10** (backend); **TypeScript + React 19** via Vite (frontend).
Unchanged from features 001/002/003.

**Primary Dependencies**:
- Backend: ASP.NET Core 10 minimal APIs; EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL`;
  **Microsoft.Playwright** (already referenced by `Infrastructure.csproj` for the 001 theprotocol
  scraper) — **reused** for HTML→PDF via `IPage.PdfAsync`. **PdfPig** retained (002 CV readability
  gauge + fallback text, reused as the source-CV fallback for the worker). **Added: none** — and,
  as in 002, explicitly **no** `@anthropic-ai`/AI SDK (FR-005, enforced by the existing no-AI-package
  guard test, extended to assert the new code adds none).
- Frontend: React 19, Vite, TypeScript, the existing hand-rolled typed `fetch` client (`api/client.ts`),
  the `ApplyModal` portal/focus-trap pattern, the `backup.ts` blob-download pattern, central design
  tokens + `theme/index.ts` helpers. **Added: none.**
- **Worker**: Claude Code (the user's Max plan) — an *external tool*, not a backend dependency.

**Storage**: PostgreSQL via EF Core, **append-only** migrations applied at startup (`MigrateAsync`).
**One** new migration `TailoredCv` (one table, non-identity uuid PK, all columns safe-defaulted). The
generated HTML+PDF files live as **flat files in the existing `cv-data` directory** (`Cv:StoragePath ??
{AppContext.BaseDirectory}/cv-data`, gitignored) — no new directory, no new config key.

**Testing**: xUnit unit tests (Domain `TailoredCv` state machine + `GenerationVersion` supersede guard;
the Domain `TailoredCvPrompt` default-prompt composer; the `TailoredCvService` generate/regenerate/
write-back accept-vs-supersede logic). **Real-PostgreSQL** integration tests via Testcontainers
(Principle V): the new table + jsonb round-trip; the end-to-end `draft → generate → pending → results
(HTML) → render → produced → download` path (with a **fake `IPdfRenderer`** so the integration suite
stays offline/deterministic — the real `PlaywrightPdfRenderer` gets one focused render-smoke test);
the **backup/restore round-trip** now including a `tailored_cv` row + its flat `cv-data` files; the new
**`BackupTables` completeness guard test**; the `SchemaInvariantTests` no-serial guard (already present)
covering the new table. The **loopback guard** on `/api/tailored-cv/*` is an HTTP-layer test. The
untouched 001/002/003 suites are the FR-019 regression contract. Frontend: Vitest + RTL for the modal
(skills toggle ↔ prompt update, generate/regenerate/download/pending/failed states) and the dedicated
page; the tailored-CV client.

**Target Platform**: local-first, single-user; Windows 11 dev. The app runs as a foreground ASP.NET
Core process on `localhost:5180`; the worker is an interactive Claude Code session in the same repo
sharing the `cv-data/` filesystem (the **host-process** run mode — same support boundary as 002 ADR-4).

**Performance Goals**: not latency-bound — generation is **on-demand** and asynchronous; un-produced
items show "pending". A worker pass drains pending tailored-CV requests in batches. The one synchronous
cost is the **per-write-back PDF render** (a Chromium page render of a 2-page CV ≈ sub-second); the
renderer reuses a lazily-launched singleton browser. Targets are completeness + transparency, not
latency: SC-002 (no fabrication), SC-003 (shown prompt == used prompt), SC-005 (print-correct A4 PDF),
SC-006 (every tailored CV reachable from both surfaces).

**Constraints**: local-first; **no external AI service**; backend transmits **0** offer/CV records
externally (mirrors 002 SC-005); the **source** CV binary never leaves disk (worker reads by path); the
generated **PDF** is served only over the **loopback-restricted** `/api/tailored-cv/*` channel to the
local user; generated files gitignored under `cv-data`; append-only migrations; async all the way;
nullable on, warnings-as-errors in Domain + Application.

**Scale/Scope**: 1 user; tailored CVs created opt-in for a handful of offers (not ~180); one source CV
typically; 4 user stories. The `tailored_cv` table is bounded by the number of offers the user chose to
tailor for — typically tens, not the full offer count.

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design. All gates PASS — no violations.
The 002 "Claude-as-worker" decision is **extended**, not superseded; the NON-NEGOTIABLE Principles III
and IV are upheld. Feature-specific decisions are recorded as ADR-1..ADR-4 (Principle XI).*

| # | Principle | Status | How this plan satisfies it |
|---|-----------|--------|----------------------------|
| I | Layered architecture, deps inward | ✅ PASS | New `Domain/TailoredCv` (framework-free aggregate + `TailoredCvState` + the pure `TailoredCvPrompt` composer); ports (`ITailoredCvRepository`, `ITailoredCvFileStore`, `IPdfRenderer`) + `TailoredCvService` in **Application**; EF config, Playwright renderer, file store, endpoints in **Infrastructure/Web**. Generate/regenerate return `Result<T>`; queue/list reads are read-only. No MediatR (YAGNI). |
| II | Strongly-typed domain, no primitive obsession | ✅ PASS | Reuse `OfferId` (satellite PK/FK) + `CvId` (`SourceCvId`); no raw `Guid`. New `TailoredCvState` enum; `GenerationVersion` is an `int` **counter** (not an ID — like `Attempts`), so no primitive-ID obsession. `Result<T>` for expected failures (offer-not-found, no-CV-on-file, invalid request). |
| III | The Tracker Reflects Reality (NON-NEG) | ✅ PASS | The tailored CV is **derived only from the user's real CV** — re-emphasised/reordered, never fabricated (FR-006); the worker is instructed to invent nothing. States are explicit (`pending/produced/failed`); a render/worker failure is `failed`, never an empty CV presented as real. No demo/placeholder CV is persisted. |
| IV | Personal Data Private & Local (NON-NEG) | ✅ PASS | Backend makes **no** external AI call and adds **no** AI SDK (FR-005, no-AI-package guard test). The **source CV binary** is delivered to the worker as a **filesystem path** (never over HTTP), exactly as 002. The worker channel transmits prompt + emphasised skills + the source-CV path/fallback text over the **loopback-restricted** `/api/tailored-cv/*` group (reusing `LoopbackOnlyFilter`, the established control). The generated PDF download is loopback-only to the local user who owns it. Generated files stay in gitignored `cv-data`. **0** records transmitted externally (SC-007). |
| V | Real Database in Tests | ✅ PASS | The new table, migration, jsonb round-trip, the full generate→produce→download path, and the backup/restore round-trip (incl. the new table + flat files) are tested on **real PostgreSQL** (Testcontainers). The DB is never mocked; only `IPdfRenderer` is faked in the DB suite (with one real-render smoke test). |
| VI | Green Before Done (NON-NEG) | ✅ PASS (process) | Each user story closes only on a green local suite. The untouched 001/002/003 suites are the FR-019 regression contract. |
| VII | UI Changes Require Visual Verification | ✅ PASS | The offer "Tailor CV" button + modal (skills toggle, editable prompt, generate/regenerate/download, pending/failed), and the new `/tailored-cvs` page, are run-and-looked-at (and a generated PDF opened) before "done". |
| VIII | One Source of Design Truth | ✅ PASS | Reuse `.btn`/`.btn--primary`/`.btn--ghost`, `.chip chip--skill`/`.chip--missing`, the `ApplyModal` portal + `ApplyModal.css` classes, and `enrichmentStatusClass`/`fitColorVar` from `theme/index.ts`. The generated **CV** layout is the single `cv_versions` recipe — one source of CV design truth — not scattered per-offer markup. No new color/px literals. |
| IX | Your Data Is Recoverable | ✅ PASS | **One** new append-only migration; **no** prior migration edited; non-identity uuid PK + safe defaults keep the 003 restore invariants (`SchemaInvariantTests`, OLDER→HEAD). Tailored CVs (table + flat files) are **included in 003 backup/restore** (FR-017) — one `BackupTables` edit + a **new completeness guard test** that prevents this and any future table from being silently dropped from backups. Generated CVs are also recomputable (regenerate). |
| X | Simple by Default (YAGNI) | ✅ PASS | **No new dependency** (reuse Playwright + PdfPig + `LoopbackOnlyFilter` + `MaintenanceGate` + `ApplyModal`); **no input-hash machinery** (user-driven `GenerationVersion` guard); **flat `cv-data` files** (no 003 enumerate/zip/swap changes); opt-in rows (no eager backfill, no per-offer satellite invariant); reuse `EnrichmentSettings.RetryLimit` (no new settings/column). One table, one migration, one slash command. |
| XI | Documented Decisions, Immutable History | ✅ PASS | ADR-1..ADR-4 record PDF-rendering, the text-only worker, flat-storage/backup-inclusion, and the generation-version guard. Conventional Commits, one logical change each. |

**Decisions (ADR-style, per Principle XI):**

- **ADR-1 — Render HTML→PDF in-process with the already-present Playwright (reject a new PDF library and
  reject worker-side rendering).**
  - *Context*: the deliverable is a **PDF** (Clarification); the `cv_versions` recipe renders the
    two-column HTML to A4 via headless Chrome. The backend already references **Microsoft.Playwright**
    (`Infrastructure.csproj`; `PlaywrightTheProtocolClient` launches `Chromium.LaunchAsync`).
  - *Decision*: add an `IPdfRenderer` port; implement `PlaywrightPdfRenderer` (singleton, lazy
    `Playwright.CreateAsync()` → `Chromium.LaunchAsync` → `page.SetContentAsync(html)` →
    `page.PdfAsync(new PagePdfOptions { Format = "A4", PrintBackground = true })`, `IAsyncDisposable`,
    mirroring the existing client). Render at write-back; a render failure → `RecordFailure`.
  - *Rationale*: **no new NuGet dependency** (Principle X); keeps the **worker text-only** (it produces
    HTML, exactly as 002 produces summary text — no binary write-back, no worker needing Chrome); the
    render is deterministic + server-side **testable** (a fake `IPdfRenderer` keeps the DB suite offline).
  - *Rejected*: a new PDF NuGet (QuestPDF/DinkToPdf/PdfSharp/wkhtmltopdf) — new dependency, and most can't
    faithfully render the existing CSS-grid `cv_versions` layout; **worker-side rendering** (Chrome
    headless in the `/tailor-cv` session writing PDF bytes back) — pushes binary handling + a browser
    requirement onto the worker and diverges from the clean 002 text-only contract; **frontend
    `window.print()`** — not a stored, server-held artifact and not a guaranteed A4 file (FR-009/FR-010).

- **ADR-2 — A fourth Claude-Code worker output kind; text-only HTML; loopback-only queue (extend 002, do
  not call any external AI).**
  - *Decision*: a re-runnable `.claude/commands/tailor-cv.md` drains `/api/tailored-cv/pending` and posts
    HTML to `/api/tailored-cv/results`, reading the source CV by path and the `cv_versions` layout from
    the repo. The group reuses `LoopbackOnlyFilter`. The backend imports no AI SDK and makes no AI call.
  - *Rationale*: this **extends** the 002 load-bearing decision (Claude-as-worker, fully local) to a new
    output — it is not a new external integration, so an ADR (Principle XI), not a constitution amendment,
    is the right reconciliation (the constitution's External-services clause already blesses local LLM
    work that satisfies Principle IV). SC-007 (0 records transmitted externally) is the measurable guard.
  - *Consequence*: the supported run mode is the host-process deployment (002 ADR-4) where loopback is
    genuine and the CV path resolves on the worker's filesystem.

- **ADR-3 — Flat generated files in `cv-data/`; one `BackupTables` edit + a new completeness guard
  (so 003 covers tailored CVs for free, FR-017).**
  - *Context*: 003's `LocalCvFileStore.EnumerateAll()` is **top-level, non-recursive**, and the backup
    zip/restore-swap carry only a flat file `Name`; a `cv-data/tailored/` **subfolder would be silently
    dropped** from every backup. `BackupTables.InsertOrder` is a hardcoded list with **no** model-vs-list
    completeness test.
  - *Decision*: write generated files as **flat** `tailored-{OfferId:N}.html` / `.pdf` in the same
    `cv-data` root (a small `ITailoredCvFileStore` resolving the identical directory), so the existing
    enumerate/zip/atomic-swap covers them unchanged. Add `"tailored_cv"` to `BackupTables.InsertOrder`
    **after `offers`** (FK-correct load order). **Add a completeness guard test** asserting
    `DbContext.Model` data-table names == `BackupTables.InsertOrder`, so this table — and any future one —
    cannot be silently omitted from backups.
  - *Rationale*: satisfies FR-017 with the **minimal** change to 003 (one list entry + one new test; no
    snapshot/zip/swap edits — columns are catalog-driven), and hardens the backup contract. The new table
    uses a non-identity uuid PK + safe defaults, so `SchemaInvariantTests` and OLDER-backup→HEAD hold.
  - *Rejected*: a `cv-data/tailored/` subfolder (would require recursive enumeration + relative-path
    plumbing through 003's archive/swap — more change, more risk); a separate backup stream for tailored
    CVs (re-implements 003).

- **ADR-4 — User-driven `GenerationVersion` supersede guard, not 002's input-hash auto-staleness.**
  - *Context*: 002 outputs are **auto-invalidated** when their inputs change (offer/CV/weights), so they
    use a recompute-on-write-back **input hash**. A tailored CV is **explicitly** (re)generated by the
    user editing the prompt; the spec does **not** require auto-invalidation when the CV/offer later
    changes (the user controls regeneration — Clarification: latest-only, one per offer).
  - *Decision*: each (re)generate bumps an `int GenerationVersion` and sets `Pending`; the pending work
    item carries that version; the worker echoes it; the write-back is accepted only when
    `version == current && state == Pending`. A newer regenerate during a slow worker pass makes the old
    result arrive with a stale version → discarded (the analogue of 002's `stale` outcome).
  - *Rationale*: simpler than hashing (no canonical-JSON composer, no eager invalidation hooks, no
    `InputHash` columns) for a user-initiated, non-auto-invalidated artifact (Principle X), while still
    preventing a stale write from overwriting a newer request. A future "your CV changed since this
    tailored CV was generated" **hint** can store the source `enrichment_input_hash` for display without
    auto-invalidating — noted as an out-of-scope enhancement, not built.

**Complexity Tracking**: No constitution violations — table omitted (N/A).

## Project Structure

### Documentation (this feature)

```text
specs/004-tailored-cv-generation/
├── plan.md              # This file
├── research.md          # Phase 0 — R1..R6 decisions, rationale, alternatives
├── data-model.md        # Phase 1 — TailoredCv aggregate, state machine, supersede guard, migration,
│                         #           backup inclusion, file-naming
├── quickstart.md        # Phase 1 — run & per-user-story validation guide
├── contracts/
│   ├── tailored-cv-api.md   # Phase 1 — REST contract (UI + worker endpoints, draft, download)
│   └── worker-protocol.md   # Phase 1 — the /tailor-cv worker contract
├── spec.md              # Feature spec (with Clarifications)
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root) — delta over features 001/002/003

```text
backend/
├── src/
│   ├── Domain/
│   │   └── TailoredCv/                    # NEW: TailoredCv (aggregate), TailoredCvState enum,
│   │                                      #      TailoredCvPrompt (pure default-prompt composer)
│   ├── Application/
│   │   ├── TailoredCv/                    # NEW: ITailoredCvRepository, ITailoredCvFileStore, IPdfRenderer (ports),
│   │   │                                  #      TailoredCvService (draft/generate/regenerate/pending/results/list/get/delete),
│   │   │                                  #      TailoredCvContracts (wire DTOs + pending work item + result item)
│   │   ├── Scanning/                      # (reuse) MaintenanceGate — consulted by TailoredCvService write paths
│   │   └── DependencyInjection.cs         # +AddScoped<TailoredCvService>()
│   ├── Infrastructure/
│   │   ├── Persistence/Configurations/    # NEW TailoredCvConfiguration (table tailored_cv, PK offer_id, FK→offers cascade,
│   │   │                                  #      state varchar(20), emphasised_skills jsonb, indexes)
│   │   ├── Persistence/Migrations/        # NEW 2026XXXX_TailoredCv.cs (+ designer + snapshot): one table, safe defaults
│   │   ├── Persistence/AppDbContext.cs    # +DbSet<TailoredCv> TailoredCvs (config auto-discovered)
│   │   ├── Persistence/Repositories/      # NEW TailoredCvRepository (tracked get-by-offer, list, add, pending query, counts)
│   │   ├── TailoredCv/                    # NEW LocalTailoredCvFileStore (flat tailored-{offerId:N}.html/.pdf in cv-data root),
│   │   │                                  #      PlaywrightPdfRenderer (IPdfRenderer; lazy Chromium; A4 PdfAsync)
│   │   └── DependencyInjection.cs         # +AddScoped<ITailoredCvRepository,..>; +AddSingleton<ITailoredCvFileStore,..>;
│   │                                      #   +AddSingleton<IPdfRenderer, PlaywrightPdfRenderer>()
│   ├── Application/Backup/BackupTables.cs # EDIT: add "tailored_cv" to InsertOrder (after "offers")
│   └── Web/
│       └── Endpoints/                     # NEW TailoredCvEndpoints (worker pending/results + UI list/get/draft/generate/
│                                          #   download/delete; .AddEndpointFilter<LoopbackOnlyFilter>());
│                                          #   FeatureEndpoints.cs wires api.MapTailoredCvEndpoints()
└── tests/
    ├── Domain.Tests/                      # TailoredCv state machine + GenerationVersion guard; TailoredCvPrompt composer
    ├── Application.Tests/                 # TailoredCvService: generate/regenerate (version bump), write-back accept-vs-
    │                                      #   supersede, render-failure→failed, no-CV / offer-not-found Results
    └── Infrastructure.Tests/             # real Postgres: tailored_cv round-trip + jsonb; e2e draft→generate→results(HTML)→
                                           #   fake-render→produced→download; backup/restore incl. tailored_cv row + flat files;
                                           #   NEW BackupTables completeness guard test; loopback guard (HTTP layer);
                                           #   one PlaywrightPdfRenderer real-render smoke test; no-AI-package guard (extended)

frontend/
└── src/
    ├── api/
    │   ├── tailoredCv.ts                  # NEW: getDraft(offerId), generate(offerId, body), listTailored(), getTailored(offerId),
    │   │                                  #      deleteTailored(offerId) via api.*; downloadTailoredPdf(offerId) (blob→<a download>,
    │   │                                  #      mirrors backup.ts) ; +TailoredCvDto/TailoredCvDraftDto in types.ts
    │   └── types.ts                       # +TailoredCvDto, TailoredCvDraftDto, TailoredCvState
    ├── components/
    │   ├── OfferCard/OfferCard.tsx        # +"Tailor CV" button in .offer-card__actions; local modal state → <TailorCvModal/>
    │   └── TailorCvModal/                 # NEW: TailorCvModal.tsx (clone ApplyModal portal/focus-trap; skills chips toggle ↔
    │                                      #      editable prompt textarea; attached-source-CV line; Generate/Regenerate/Download)
    ├── pages/
    │   └── TailoredCvs/                   # NEW TailoredCvsPage.tsx (+ .css): lists all tailored CVs (sibling of Cv page),
    │                                      #      each links to its offer + view/regenerate/download
    ├── App.tsx                            # +{ to:'/tailored-cvs', label:'Tailored CVs' } in NAV; +import + <Route>
    └── theme/                            # reuse enrichmentStatusClass chips (pending/produced/failed) — no new tokens

.claude/commands/tailor-cv.md             # NEW: the /tailor-cv worker slash command (worker-protocol.md)
```

**Structure Decision**: Web-application layout, unchanged. The feature is **additive**: a new
`Domain/TailoredCv` slice, a new `Application/TailoredCv` slice with ports implemented in
`Infrastructure`, one new table + migration, one loopback-only Web endpoint group, a repo-local Claude
Code worker command, and a small UI surface (one offer button + modal + one page). The only edits to
existing code are **additive**: one line in `BackupTables.InsertOrder`, one line in `FeatureEndpoints`,
the `OfferCard` button, the `App.tsx` nav/route, and a few DI registrations. 001/002/003 behaviour is
preserved; Domain stays framework-free; the worker is an external Claude Code session.

## Phase Status

- [x] Phase 0 — Research (`research.md`): 6 unknowns resolved (R1–R6) via a four-way parallel codebase
  investigation; all `NEEDS CLARIFICATION` resolved (the spec's clarifications fed the design directly).
- [x] Phase 1 — Design & Contracts (`data-model.md`, `contracts/tailored-cv-api.md`,
  `contracts/worker-protocol.md`, `quickstart.md`); agent context (CLAUDE.md SPECKIT section) updated
  to point here.
- [ ] Phase 2 — Tasks (`/speckit-tasks`) — **not** produced by this command.

## Complexity Tracking

> No Constitution Check violations — this section is intentionally empty (N/A). Feature-specific
> decisions are recorded in ADR-1..ADR-4, not as violations.
