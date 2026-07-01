---
description: "Task list for Tailored CV per Job Offer (004)"
---

# Tasks: Tailored CV per Job Offer

**Input**: Design documents from `specs/004-tailored-cv-generation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: INCLUDED — the constitution mandates real-DB integration tests (Principle V) and green-before-done
(Principle VI); the plan ships a test inventory. Test tasks are first-class here.

**Organization**: grouped by user story (US1–US4) so each is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US4 (Setup/Foundational/Polish carry no story label)
- Paths are repo-relative; backend = `backend/src` + `backend/tests`, frontend = `frontend/src`.

---

## Phase 1: Setup (Shared)

**Purpose**: prerequisites that aren't code behaviour.

- [X] T001 [P] Verify the PDF-renderer prerequisite: confirm `Microsoft.Playwright` is referenced in `backend/src/Infrastructure/Infrastructure.csproj` (it already is — **no new package**) and that the Chromium browser is installed for dev/test (`playwright install chromium`); note it in `specs/004-tailored-cv-generation/quickstart.md` (already documented).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the shared Domain + persistence + ports/adapters + backup inclusion every story needs. No user-facing behaviour ships here.

**⚠️ CRITICAL**: no user story can begin until this phase is complete.

### Domain (framework-free)

- [X] T002 [P] Create `TailoredCvState` enum (`Pending|Produced|Failed`) in `backend/src/Domain/TailoredCv/TailoredCvState.cs`
- [X] T003 Create the `TailoredCv` aggregate (fields per data-model §2: `OfferId` PK, `SourceCvId`, `State`, `Attempts`, `GenerationVersion`, `Prompt`, `EmphasisedSkills`, `HtmlFileName`, `PdfFileName`, `GeneratedAt`, `LastError`, `CreatedAt`; methods `CreateRequest`, `RequestRegeneration`, `MarkProduced`, `RecordFailure`, `Accepts`; private parameterless ctor for EF) in `backend/src/Domain/TailoredCv/TailoredCv.cs` (depends T002)
- [X] T004 [P] Create the pure `TailoredCvPrompt.BuildDefault(offerView, emphasisedSkills)` composer + `TailoredCvOfferView` (recipe + skills interpolation + no-fabrication clause + cv_versions layout instruction) in `backend/src/Domain/TailoredCv/TailoredCvPrompt.cs`
- [X] T005 [P] Unit tests: `TailoredCv` state machine + `GenerationVersion`/`Accepts` supersede + retry→`Failed` at limit in `backend/tests/Domain.Tests/TailoredCvTests.cs` (depends T003)
- [X] T006 [P] Unit tests: `TailoredCvPrompt.BuildDefault` (skills appear, no-fabrication clause present, layout instruction present, deterministic) in `backend/tests/Domain.Tests/TailoredCvPromptTests.cs` (depends T004)

### Persistence (EF Core + migration)

- [X] T007 Create `TailoredCvConfiguration` (table `tailored_cv`, PK `offer_id` `ValueGeneratedNever`, FK→`offers(id)` cascade, `state` `HasConversion<string>()` len 20, `emphasised_skills` `HasJsonbListConversion<string>()` default `'[]'`, `source_cv_id` uuid, `HasIndex(State)`) in `backend/src/Infrastructure/Persistence/Configurations/TailoredCvConfiguration.cs` (depends T003)
- [X] T008 Add `DbSet<TailoredCv> TailoredCvs => Set<TailoredCv>()` to `backend/src/Infrastructure/Persistence/AppDbContext.cs` (depends T003)
- [X] T009 Generate the append-only EF migration `TailoredCv` (`dotnet ef migrations add TailoredCv`) — one table, non-identity uuid PK, safe defaults per data-model §9 — committing the `*_TailoredCv.cs` + designer + updated snapshot under `backend/src/Infrastructure/Persistence/Migrations/` (depends T007, T008)

### Ports + adapters

- [X] T010 [P] Define ports + records `ITailoredCvRepository`, `ITailoredCvFileStore` (+ `TailoredCvFiles`), `IPdfRenderer`, `TailoredCvCounts` in `backend/src/Application/TailoredCv/` (depends T003)
- [X] T011 Implement `TailoredCvRepository` (tracked `GetByOfferAsync`; `GetAllAsync`/`GetPendingAsync` AsNoTracking; `AddAsync`/`Remove`; `GetCountsAsync`) in `backend/src/Infrastructure/Persistence/Repositories/TailoredCvRepository.cs` (depends T010, T008)
- [X] T012 [P] Implement `LocalTailoredCvFileStore` — flat `tailored-{OfferId:N}.html`/`.pdf` in the **same** `Cv:StoragePath ?? {BaseDirectory}/cv-data` root (save both, get-pdf-path, get-html, delete) in `backend/src/Infrastructure/TailoredCv/LocalTailoredCvFileStore.cs` (depends T010)
- [X] T013 [P] Implement `PlaywrightPdfRenderer` (`IPdfRenderer`; singleton; lazy `Playwright.CreateAsync()`→`Chromium.LaunchAsync`; `SetContentAsync`+`PdfAsync(A4, PrintBackground)`; `IAsyncDisposable`; mirrors `PlaywrightTheProtocolClient`) in `backend/src/Infrastructure/TailoredCv/PlaywrightPdfRenderer.cs` (depends T010)
- [X] T014 [P] Integration smoke test: the **real** `PlaywrightPdfRenderer` renders sample HTML to a non-empty A4 PDF (`%PDF` header, 2-page sample) in `backend/tests/Infrastructure.Tests/TailoredCv/PdfRendererTests.cs` (depends T013)

### Shared wire DTOs + DI + endpoint group skeleton

- [X] T015 [P] Define wire DTOs (`TailoredCvView`, `TailoredCvDraftView`, the pending work item, the result item, `SubmitResultsRequest`/`SubmitResultsResponse`) per the contracts in `backend/src/Application/TailoredCv/TailoredCvContracts.cs` (depends T003)
- [X] T016 Register Infrastructure adapters: `ITailoredCvRepository`→`TailoredCvRepository` (scoped), `ITailoredCvFileStore`→`LocalTailoredCvFileStore` (singleton), `IPdfRenderer`→`PlaywrightPdfRenderer` (singleton) in `backend/src/Infrastructure/DependencyInjection.cs` (depends T011, T012, T013)
- [X] T017 Create the `TailoredCvEndpoints` group skeleton (`api.MapGroup("/tailored-cv").AddEndpointFilter<LoopbackOnlyFilter>()`, stub handlers filled per story) in `backend/src/Web/Endpoints/TailoredCvEndpoints.cs` and wire `api.MapTailoredCvEndpoints();` in `backend/src/Web/Endpoints/FeatureEndpoints.cs`

### Backup/restore inclusion (FR-017)

- [X] T018 Add `"tailored_cv"` to `BackupTables.InsertOrder` **after `"offers"`** (FK load order) in `backend/src/Application/Backup/BackupTables.cs`
- [X] T019 [P] Add the **completeness guard test**: assert the set of mapped data-table names in `AppDbContext.Model` (minus `__EFMigrationsHistory`) equals `BackupTables.InsertOrder` in `backend/tests/Infrastructure.Tests/Backup/BackupTablesCompletenessTests.cs`

### Frontend scaffolding (shared)

- [X] T020 [P] Add `TailoredCvDto`, `TailoredCvDraftDto`, `TailoredCvState` to `frontend/src/api/types.ts`
- [X] T021 [P] Create the `frontend/src/api/tailoredCv.ts` client skeleton (`getDraft`, `generate`, `getTailored`, `listTailored`, `deleteTailored`; `downloadTailoredPdf` added in US3) using `api.*` from `client.ts` (depends T020)

**Checkpoint**: foundation ready — schema migrated, adapters wired, backup covers the new table, endpoint group mounted. User stories can begin.

---

## Phase 3: User Story 1 — Generate a tailored CV from an offer (Priority: P1) 🎯 MVP

**Goal**: from an offer, open a modal that points out the offer's skills and shows the exact (editable) prompt with the source CV attached; Generate; the worker produces a tailored CV; view it in-app.

**Independent Test**: open an offer → "Tailor CV" → modal shows emphasised-skills chips + the exact prompt + the attached CV name → Generate → state `pending` → run `/tailor-cv` → state `produced` → the tailored CV (HTML preview) is shown and contains only real CV content.

### Implementation — backend

- [X] T022 [US1] Implement `TailoredCvService` core in `backend/src/Application/TailoredCv/TailoredCvService.cs`: `GetDraftAsync` (source-CV selection per data-model §5 + default emphasised-skills per §4 + `TailoredCvPrompt.BuildDefault`, honouring the optional `skills` recompose), `GenerateAsync` (**`maintenance.WaitWhileActiveAsync`** — it is a DB write that must defer through a restore; then create or `RequestRegeneration` → version bump → `Pending` → persist; `Result<TailoredCvView>` with `OfferNotFound`/`NoCvOnFile`/`InvalidTailoredCvRequest`), `GetPendingWorkAsync` (build pending items with `sourceCv.path` via `ICvFileStore.GetAbsolutePath` + offer view), `SubmitResultsAsync` (`maintenance.WaitWhileActiveAsync`; `Accepts` guard → `superseded`; on `produced` render via `IPdfRenderer` + store both files + `MarkProduced`; render/`failed` → `RecordFailure`), `GetAsync` (reuses `IOfferReadService`/`ICvRepository`/`ISettingsRepository`/`MaintenanceGate`). **Both `GenerateAsync` and `SubmitResultsAsync` consult the gate** (the two write paths). (depends T010, T011, T012, T013, T015)
- [X] T023 [US1] Register `TailoredCvService` (scoped) in `backend/src/Application/DependencyInjection.cs` (depends T022)
- [X] T024 [US1] Fill US1 endpoints in `backend/src/Web/Endpoints/TailoredCvEndpoints.cs`: `GET /offer/{id}/draft[?skills=]`, `POST /offer/{id}`, `GET /offer/{id}`, `GET /offer/{id}/preview` (text/html, inline), worker `GET /pending`, `POST /results` (depends T017, T022)
- [X] T025 [US1] Create the `/tailor-cv` worker slash command in `.claude/commands/tailor-cv.md` per `contracts/worker-protocol.md` (drain `/pending`; read source CV by path + `cv_versions/v2_two_column.html`+`NOTES.md`; produce tailored HTML; no fabrication; echo `generationVersion`; POST `/results`) (depends T024)

### Tests — US1

- [X] T026 [P] [US1] Application tests in `backend/tests/Application.Tests/TailoredCvServiceTests.cs`: generate-create sets `Pending`/version 1; write-back accept→`Produced` (with a **fake `IPdfRenderer`**); render-throws→`Failed`; `NoCvOnFile`/`OfferNotFound` `Result`s; default emphasised-skills + source-CV selection rules (depends T022)
- [X] T027 [P] [US1] Infrastructure integration test (real Postgres + fake `IPdfRenderer`) in `backend/tests/Infrastructure.Tests/TailoredCv/TailoredCvFlowTests.cs`: e2e `draft→generate→pending→results(HTML)→produced`; `tailored_cv` jsonb round-trip; `/preview` returns the stored HTML (depends T024)
- [X] T028 [P] [US1] Loopback-guard HTTP test for `/api/tailored-cv/*` (403 from a non-loopback remote IP, fail-closed on unknown) in `backend/tests/Infrastructure.Tests/TailoredCv/TailoredCvLoopbackTests.cs` (depends T024)

### Implementation — frontend

- [X] T029 [US1] Add a "Tailor CV" button to `.offer-card__actions` + local modal state + conditional `<TailorCvModal/>` in `frontend/src/components/OfferCard/OfferCard.tsx` (depends T021)
- [X] T030 [US1] Create `TailorCvModal` (clone the `ApplyModal` portal + focus-trap + `role="dialog"`; reuse `ApplyModal.css`; on open call `getDraft`; show toggleable emphasised-skills chips + the attached source-CV name + the editable prompt `<textarea>`; **Generate**; `pending`/`produced`/`failed` via `enrichmentStatusClass`; on `produced` show an inline `<iframe>` of `/preview`) in `frontend/src/components/TailorCvModal/TailorCvModal.tsx` (depends T021, T029)
- [X] T031 [P] [US1] Frontend tests (Vitest + RTL) in `frontend/src/components/TailorCvModal/TailorCvModal.test.tsx`: modal shows draft (skills + exact prompt + attached CV); Generate calls the client; pending→produced preview renders (depends T030)

**Checkpoint**: US1 fully functional — a tailored CV can be generated from an offer and viewed in-app (MVP).

---

## Phase 4: User Story 2 — See, edit, regenerate from the exact prompt (Priority: P1)

**Goal**: the full prompt is visible + editable; toggling skills updates it; Regenerate produces a new CV that supersedes the prior one; shown prompt == used prompt.

**Independent Test**: in the modal, edit the prompt and toggle a skill (prompt updates), Regenerate, run `/tailor-cv` → the new CV reflects the edit and replaces the prior; `GET /offer/{id}` shows the stored prompt equals what was submitted; a doubly-regenerated slow result returns `superseded`.

### Implementation

- [X] T032 [US2] Wire **skill-toggle ⇄ prompt update** in `TailorCvModal` — while the prompt is the unedited default, toggling a chip re-fetches `getDraft(offerId, { skills })` and updates the visible prompt; once the user manually edits, toggles update only the selection (no clobber) — in `frontend/src/components/TailorCvModal/TailorCvModal.tsx` (depends T030)
- [X] T033 [US2] Add the **Regenerate** action in `TailorCvModal` (POST `/offer/{id}` with the edited prompt + current selection → `pending` → produced replaces prior) in `frontend/src/components/TailorCvModal/TailorCvModal.tsx` (depends T030)

### Tests — US2

- [X] T034 [P] [US2] Application test in `backend/tests/Application.Tests/TailoredCvServiceTests.cs`: `RequestRegeneration` bumps `GenerationVersion`/re-`Pending`; a write-back echoing the **old** version after a second regenerate returns `superseded` and writes nothing; the stored `Prompt` equals the submitted prompt (SC-003) (depends T022)
- [X] T035 [P] [US2] Frontend test in `frontend/src/components/TailorCvModal/TailorCvModal.test.tsx`: editing the prompt + toggling a skill updates the visible prompt; Regenerate posts the edited prompt (depends T033)

**Checkpoint**: US1 + US2 work — transparent, editable, regeneratable prompt.

---

## Phase 5: User Story 3 — Download the tailored CV as a polished PDF (Priority: P2)

**Goal**: download the produced tailored CV as a print-correct A4 PDF; download unavailable while pending/failed.

**Independent Test**: from a produced CV, Download saves a `*.pdf` that opens as a 2-column A4 CV (correct pages, intact layout); for a pending/failed CV the action is unavailable and `…/download` returns `409 TailoredCvNotReady`.

### Implementation

- [X] T036 [US3] Implement `GET /offer/{id}/download` (stream the produced PDF via `Results.File(absolutePath, "application/pdf", "CV - {company} - {title}.pdf")`; `409 TailoredCvNotReady` when state ≠ produced) in `backend/src/Web/Endpoints/TailoredCvEndpoints.cs` (depends T024)
- [X] T037 [US3] Implement `downloadTailoredPdf(offerId)` (raw `fetch` → `res.blob()` → `saveBlob` + `filenameFromDisposition`, Accept `application/pdf`; mirror `api/backup.ts`) in `frontend/src/api/tailoredCv.ts` (depends T021)
- [X] T038 [US3] Add a **Download** button (enabled only when `hasPdf`/produced) to `TailorCvModal` in `frontend/src/components/TailorCvModal/TailorCvModal.tsx` (depends T030, T037)

### Tests — US3

- [X] T039 [P] [US3] Infrastructure test in `backend/tests/Infrastructure.Tests/TailoredCv/TailoredCvFlowTests.cs`: `…/download` returns `application/pdf` for a produced CV and `409` for pending/failed (depends T036)
- [X] T040 [P] [US3] Frontend test in `frontend/src/components/TailorCvModal/TailorCvModal.test.tsx`: Download disabled until produced; click triggers the blob download (depends T038)

**Checkpoint**: US1–US3 work — generate, iterate, download.

---

## Phase 6: User Story 4 — Store + reach from the offer and a dedicated page (Priority: P2)

**Goal**: tailored CVs persist, are indicated on their offer (reopen), and are all listed on a dedicated page that links back to each offer; they survive an offer going unavailable.

**Independent Test**: generate for two offers → each offer card indicates a tailored CV + reopens it → the **Tailored CVs** page lists both with links back + view/regenerate/download/remove → a tailored CV persists on the page after its offer delists.

### Implementation

- [X] T041 [US4] Add `ListAsync` (all, newest first) + `DeleteAsync` (remove row + both files via `ITailoredCvFileStore.Delete`) to `backend/src/Application/TailoredCv/TailoredCvService.cs` (depends T022)
- [X] T042 [US4] Fill `GET /api/tailored-cv` (list) + `DELETE /offer/{id}` endpoints in `backend/src/Web/Endpoints/TailoredCvEndpoints.cs` (depends T041, T024)
- [X] T043 [US4] In `frontend/src/pages/Offers/OffersPage.tsx` fetch `listTailored()` and pass a per-offer presence/state lookup to `OfferCard`; in `frontend/src/components/OfferCard/OfferCard.tsx` show a "Tailored CV" indicator + reopen when one exists (depends T021, T029)
- [X] T044 [US4] Create `TailoredCvsPage` (+ `TailoredCvsPage.css`) listing all tailored CVs — each row links back to its offer with view (`/preview`) / regenerate / download / remove; sibling style of the CV page — in `frontend/src/pages/TailoredCvs/TailoredCvsPage.tsx` (depends T021, T037)
- [X] T045 [US4] Add `{ to: '/tailored-cvs', label: 'Tailored CVs' }` to `NAV` and the import + `<Route path="/tailored-cvs" .../>` in `frontend/src/App.tsx` (depends T044)

### Tests — US4

- [X] T046 [P] [US4] Infrastructure test in `backend/tests/Infrastructure.Tests/TailoredCv/TailoredCvFlowTests.cs`: list returns all; delete removes the row + both files; a tailored CV remains retrievable after its offer is marked unavailable (FR-018) (depends T042)
- [X] T047 [P] [US4] Frontend test in `frontend/src/pages/TailoredCvs/TailoredCvsPage.test.tsx`: the page lists items + links back to offers; the offer-card indicator appears when a tailored CV exists (depends T044, T043)

**Checkpoint**: all four stories independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T048 [P] Backup/restore round-trip integration test including a `tailored_cv` row **and** its flat `cv-data/tailored-*.html/.pdf` files (backup → wipe → restore → assert the row + files return byte-identical) in `backend/tests/Infrastructure.Tests/Backup/BackupRestoreTailoredCvTests.cs`
- [X] T049 [P] Extend the `MaintenanceGate` gating test: a tailored-CV `SubmitResultsAsync` **defers** during an active restore (mirror the enrichment-defer case) in `backend/tests/Infrastructure.Tests/Backup/MaintenanceGatingTests.cs`
- [X] T050 [P] Confirm the existing no-AI-package guard test covers the new code (no `@anthropic-ai`/AI SDK added by 004) — add an explicit assertion/comment in the guard test (`backend/tests/Infrastructure.Tests/...`)
- [X] T051 [P] **FR-019 regression gate**: run the full **001/002/003** backend suites (`Domain.Tests`, `Application.Tests`, `Infrastructure.Tests`) **and** the frontend suite green to confirm collection/feed/scheduler, enrichment/fit, and backup/restore behaviour is unchanged by 004 (additive design — no existing test should need editing; if one does, that is a regression to investigate)
- [X] T052 [P] **No-fabrication fidelity heuristic** (defence-in-depth for FR-006/SC-002, which has no fully-deterministic test): add a pure helper `TailoredCvFidelity.FindAddedTokens(generatedText, sourceCvText)` in `backend/src/Domain/TailoredCv/TailoredCvFidelity.cs` (case-folded token-set difference, ignoring layout/boilerplate words) + a test `backend/tests/Application.Tests/TailoredCvFidelityTests.cs` (controlled fixtures: in-source HTML ⇒ no added tokens flagged; HTML injecting a fabricated employer ⇒ flagged). **Optionally** surface its output as an advisory `LogWarning` in `SubmitResultsAsync` (non-blocking — never fails a generation). This aids the T053 manual review; it is not a runtime gate
- [ ] T053 Run `specs/004-tailored-cv-generation/quickstart.md` end-to-end: all four stories + cross-cutting (locality/403, backup-includes-tailored, no-CV edge) and **visually verify** (Principle VII) the modal, the dedicated page, and an opened downloaded PDF — including an explicit **no-fabrication spot-check** of a generated CV against the source CV (FR-006/SC-002), assisted by the T052 heuristic. (FR-019 regression is its own gate, T051.)
- [X] T054 [P] Document the feature + the `/tailor-cv` worker run step in the repo README/docs (if present)

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)** → no deps.
- **Foundational (P2)** → after Setup; **BLOCKS all stories**. Order inside: Domain (T002→T003; T004) → Persistence (T007,T008→T009) → adapters (T010→T011/T012/T013→T014) → DTOs/DI/endpoints (T015,T016,T017) → backup (T018,T019) → frontend scaffolding (T020→T021).
- **US1 (P3)** → after Foundational. **MVP.**
- **US2 (P4)**, **US3 (P5)**, **US4 (P6)** → after Foundational; each builds on US1's service/modal (so practically after US1), but each is independently testable.
- **Polish (P7)** → after the stories it touches.

### Story dependencies

- **US1** — needs only Foundational. Delivers the generate+view MVP.
- **US2** — reuses `TailoredCvService.GenerateAsync` (regenerate path already built in T022) + the US1 modal; adds edit/toggle/regenerate UI + supersede test.
- **US3** — adds the download endpoint + client + button; depends on a produced CV (US1) but is its own slice.
- **US4** — adds list/delete + the dedicated page + the offer indicator.

### Same-file serialization (NOT parallel)

- `TailorCvModal.tsx`: T030 → T032 → T033 → T038 (US1→US2→US3) — sequential.
- `TailorCvModal.test.tsx`: T031 → T035 → T040 — sequential.
- `TailoredCvEndpoints.cs`: T017 → T024 → T036 → T042 — sequential.
- `TailoredCvService.cs`: T022 → T041 — sequential.
- `TailoredCvFlowTests.cs`: T027 → T039 → T046 — sequential.
- `OfferCard.tsx`: T029 → T043 — sequential.
- `DependencyInjection.cs` (Application): T023 (Infrastructure DI T016 is a different file — parallel-safe).

### Parallel opportunities

- **Foundational**: T002, T004 (Domain) ∥; T005, T006 (Domain tests) ∥; T012, T013, T014 (file store, renderer, render test) ∥ after T010; T015, T019, T020 ∥; T021 after T020.
- **US1**: T026, T027, T028 (tests) ∥ once their targets exist; T026 ∥ with frontend T029/T030.
- **Polish**: T048, T049, T050, T051, T052, T054 ∥ (T053 — the final manual quickstart + visual + no-fabrication review — runs last, after the others).

---

## Parallel Example: Foundational adapters

```bash
# After T010 (ports defined), these three target different files:
Task: "Implement LocalTailoredCvFileStore in backend/src/Infrastructure/TailoredCv/LocalTailoredCvFileStore.cs"   # T012
Task: "Implement PlaywrightPdfRenderer in backend/src/Infrastructure/TailoredCv/PlaywrightPdfRenderer.cs"          # T013
Task: "Real-render smoke test in backend/tests/Infrastructure.Tests/TailoredCv/PdfRendererTests.cs"               # T014
```

## Parallel Example: User Story 1 tests

```bash
Task: "Application tests in backend/tests/Application.Tests/TailoredCvServiceTests.cs"            # T026
Task: "Infra e2e test in backend/tests/Infrastructure.Tests/TailoredCv/TailoredCvFlowTests.cs"   # T027
Task: "Loopback guard test in backend/tests/Infrastructure.Tests/TailoredCv/TailoredCvLoopbackTests.cs" # T028
```

---

## Implementation Strategy

### MVP first (US1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational (CRITICAL) → 3. Phase 3 US1 → **STOP & VALIDATE** the generate+view loop end-to-end with `/tailor-cv` → demo.

### Incremental delivery

- Foundational ready → **US1** (generate + view, MVP) → **US2** (edit/regenerate) → **US3** (download PDF) → **US4** (store + dedicated page). Each adds value without breaking the prior; run the relevant quickstart checks at each checkpoint.

### Cross-feature caution

- T018 (`BackupTables` edit) + T019 (completeness guard) + T009 (migration) must land **together** so a backup taken after the migration includes the new table (FR-017). T048 is the end-to-end proof.

---

## Notes

- [P] = different files, no incomplete-task dependency. [Story] maps a task to US1–US4 for traceability.
- Tests are required (Principle V/VI): Domain/Application unit tests + **real-Postgres** integration tests (Testcontainers); only `IPdfRenderer` is faked in the DB suite (T013/T014 cover the real renderer).
- Reuse, don't reinvent: `LoopbackOnlyFilter`, `MaintenanceGate`, `ApplyModal`(+css), `backup.ts` download pattern, `enrichmentStatusClass`, `EnrichmentSettings.RetryLimit`, `ICvFileStore.GetAbsolutePath`.
- Commit per task or logical group; keep `Domain` framework-free; never add an AI SDK (FR-005/SC-007).
- Stop at any checkpoint to validate a story independently; run `/speckit-analyze` before `/speckit-implement`.
