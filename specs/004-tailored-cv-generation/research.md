# Research: Tailored CV per Job Offer

**Feature**: `004-tailored-cv-generation` | **Date**: 2026-06-30 | **Plan**: [plan.md](./plan.md)

Phase 0 resolved six unknowns via a four-way parallel investigation of the live 001/002/003 code
(enrichment worker backend, CV storage, 003 backup inventory, frontend) plus the `cv_versions` recipe.
Each is recorded as Decision / Rationale / Alternatives. There were **no spec `NEEDS CLARIFICATION`
markers** outstanding — the spec's Clarifications (Session 2026-06-30) fixed source=uploaded-CV,
download=PDF, latest-only/one-per-offer, and the "instructions+skills editable / CV attached" prompt
model; this research turns those into a grounded technical design.

---

## R1 — Who generates the tailored CV?

**Decision**: **Extend the 002 "Claude-as-worker" pattern** to a fourth output kind. A re-runnable
`.claude/commands/tailor-cv.md` (`/tailor-cv`) drains a loopback-only `/api/tailored-cv` queue, reads
the source CV PDF by local path + the `cv_versions` layout from the repo, follows the (editable)
prompt, and posts back **tailored HTML**. The backend imports no AI SDK and makes no external AI call.

**Rationale**: Principle IV (NON-NEGOTIABLE) and the locked 002 decision require all AI generation to
run under the user's own Max-plan Claude Code session, never a backend/external AI call (FR-005,
mirroring 002 FR-012/SC-005). The enrichment queue (`EnrichmentEndpoints` → `EnrichmentService` →
`IEnrichmentRepository`, `LoopbackOnlyFilter`, `MaintenanceGate.WaitWhileActiveAsync`) is a proven,
directly-reusable template. The `offerFit` work-kind — an offer-keyed satellite combining the offer's
skills + the produced CV profile — is the closest sibling (`EnrichmentService.BuildFitItem`).

**Alternatives considered**: (a) backend calls the paid Anthropic API — **rejected**, violates
Principle IV / FR-005. (b) a non-AI template-fill of the CV — **rejected**, can't tailor prose to a
posting and would be a "fallback" the locality model forbids. (c) a separate long-running background
generator service — **rejected** (YAGNI; the pull queue + slash command is the established shape).

---

## R2 — How is the PDF produced?

**Decision**: Render HTML→PDF **in-process in the backend** via the **already-referenced Microsoft.
Playwright** (`Infrastructure.csproj`). New `IPdfRenderer` port; `PlaywrightPdfRenderer` singleton lazily
launches Chromium (`Playwright.CreateAsync()` → `Chromium.LaunchAsync`, mirroring
`PlaywrightTheProtocolClient.EnsureBrowserAsync`), `page.SetContentAsync(html)`, `page.PdfAsync(Format
"A4", PrintBackground true)`. Render happens at **write-back**; the worker stays **text-only** (returns
HTML). A render failure → `RecordFailure` (no downloadable PDF).

**Rationale**: **No new NuGet dependency** (Principle X) — Playwright/Chromium is already used by the
001 theprotocol scraper, so the headless browser is already provisioned in the supported host-process
run mode. Keeping the worker text-only matches the clean 002 contract (it produces strings, not
binaries, and needs no Chrome). Server-side rendering is deterministic and **testable** — a fake
`IPdfRenderer` keeps the integration suite offline; one real-render smoke test covers the adapter.
Verified: `IPage.PdfAsync(PagePdfOptions)` with `Format="A4"` + `PrintBackground=true` reproduces the
`cv_versions` recipe (which itself uses `chrome --headless --print-to-pdf`, `NOTES.md`).

**Alternatives considered**: (a) a managed PDF NuGet (QuestPDF / PdfSharp / DinkToPdf / wkhtmltopdf) —
**rejected**: a new dependency, and most cannot faithfully render the existing CSS-grid two-column
`cv_versions` layout (the `NOTES.md` quirks around print pagination are Chrome-specific). (b)
**worker-side** Chrome rendering (the `/tailor-cv` session runs `chrome --headless` and posts PDF bytes
back) — **rejected**: pushes binary handling + a Chrome requirement onto the worker and diverges from
002's text-only write-back. (c) frontend `window.print()` — **rejected**: not a stored server-held
artifact, not a guaranteed A4 file (fails FR-009/FR-010/SC-005). **Render timing** — rejected lazy
render-on-download in favour of render-at-write-back so `Produced` ⇒ downloadable and render errors
surface immediately (single-user, sub-second render; noted as reversible).

---

## R3 — Where are the generated HTML/PDF files stored (and how does 003 back them up)?

**Decision**: Store generated files as **flat files in the existing `cv-data` root** —
`tailored-{OfferId:N}.html` (regeneratable source) and `tailored-{OfferId:N}.pdf` (download artifact) —
via a small `ITailoredCvFileStore`/`LocalTailoredCvFileStore` resolving the **identical** directory
(`Cv:StoragePath ?? {AppContext.BaseDirectory}/cv-data`) used by `LocalCvFileStore`.

**Rationale**: 003's `LocalCvFileStore.EnumerateAll()` uses `Directory.EnumerateFiles(_directory)` —
**top-level, non-recursive** — and the backup zip + restore swap carry only a flat file `Name` (no
relative path). **Flat files in the same root are therefore captured by the existing backup zip and the
atomic restore directory-swap with zero changes to 003's file handling** (FR-017). A
`cv-data/tailored/` *subfolder* would be **silently dropped** from every backup (enumeration is
non-recursive and `StoredCvFile.Name`/`CvFilePayload.Name` have no path component). `cv-data` is
already gitignored (FR-016). Naming by `OfferId` is unique under the latest-only / one-per-offer
clarification; regenerate overwrites the same two files. Extra non-CV files in `cv-data` are harmless —
the backup treats the directory as an opaque file set and the CV listing reads DB rows, not files.

**Alternatives considered**: (a) `cv-data/tailored/` subfolder — **rejected**: requires recursive
enumeration + relative-path plumbing through `EnumerateAll`, `ZipBackupArchiveStore` entry naming, the
`StageSwap` write loop, and the flat-`Name` records — more change and risk for no benefit at this scale.
(b) store HTML/PDF **as bytes in the DB** (`tailored_cv` columns) — **rejected**: bloats COPY-text
backups, and the 003 design deliberately keeps CV bytes on disk, not in the DB. (c) a brand-new backed-up
directory — **rejected**: re-implements 003's enumerate/zip/swap.

---

## R4 — Staleness & concurrency: how is a stale worker write prevented without overwriting a newer request?

**Decision**: A **monotonic `int GenerationVersion`** supersede guard (not 002's input-hash). Each
create/regenerate bumps `GenerationVersion`, sets state `Pending`, resets `Attempts`. The pending work
item carries the current version; the worker echoes it; `SubmitResultsAsync` accepts a result **only**
when `echoedVersion == current && state == Pending`, else returns `superseded` (writes nothing). No
auto-invalidation on CV/offer/weight changes.

**Rationale**: A tailored CV is **user-driven** — the user edits the prompt and clicks regenerate;
the spec (Clarification: latest-only, one per offer) does **not** ask for auto-invalidation when the
underlying CV/offer later changes. So 002's recompute-on-write-back **input hash** (canonical-JSON
composers, eager invalidation hooks, `InputHash` columns, the `stale` outcome) is unnecessary
machinery here (Principle X). A simple version counter still prevents the one real race — a slow worker
finishing an old generation after the user has regenerated — by discarding the stale result, exactly
the role 002's `stale` outcome plays. The version int is a counter (like `Attempts`), not a raw ID, so
Principle II is satisfied without wrapping it.

**Alternatives considered**: (a) reuse 002's input-hash + eager invalidation — **rejected**:
unnecessary complexity for a non-auto-invalidated, user-initiated artifact. (b) no guard at all —
**rejected**: a slow worker could overwrite a freshly regenerated request. (c) a Guid generation token —
**rejected**: a raw `Guid` in domain/app code trips Principle II, and a counter is simpler. **Future
hint** (not built): persist the source CV's `enrichment_input_hash` at generation time to *display* "CV
changed since this was generated" without auto-invalidating — an out-of-scope enhancement.

---

## R5 — The "exact prompt" model, source-CV attachment, and default-prompt composition (FR-003/FR-013)

**Decision**: A server-assembled **draft** (`GET /api/tailored-cv/offer/{id}/draft`) returns
`{ prompt, emphasisedSkills, sourceCv: { id, fileName } }` for the modal. The **editable prompt**
(`<textarea>`) holds the full tailoring **instructions + emphasised-skills selection**; the **source CV
is an attached, visible, read-only input** (shown as "Source CV: <fileName>") — it is read by the worker
from disk by path, **never inlined** into the prompt and **never hidden** (Clarification / FR-003). The
default prompt is composed by a **pure Domain composer** `TailoredCvPrompt.BuildDefault(offerView,
emphasisedSkills)` from: the offer (title/company/seniority), the emphasised skills, the `cv_versions`
two-column layout instruction, and the explicit **no-fabrication** rule. Emphasised skills default to
the offer's enriched `KeySkills` ∪ fit `Matched`/`Missing`, falling back to `RequiredSkills` /
`NiceToHaveSkills` when enrichment is still pending; the user toggles them, and the visible prompt
updates (the skill list is interpolated into the prompt text).

**Source-CV selection**: default to the **most-recently-uploaded readable CV**
(`ICvRepository.GetReadableAsync` newest; fall back to most-recent; if none, generation is unavailable
and the modal shows an "add a CV first" state — edge case). The request MAY override with an explicit
`sourceCvId`. The chosen `CvId` is persisted on the row for provenance.

**Rationale**: honouring "shown prompt == used prompt" (SC-003) while keeping the textbox usable — the
CV (potentially long) is attached, not pasted, yet nothing is hidden because the worker only ever sees
(a) the exact prompt the user edited and (b) the source CV the user was shown is attached. All the
per-offer skill inputs already exist on the read model (`OfferListItem.RequiredSkills /
NiceToHaveSkills / KeySkills / Fit.Matched / Fit.Missing`) — no new collection is needed.

**Alternatives considered**: (a) inline the full CV text into the editable prompt — **rejected** by the
clarification (unwieldy textbox); (b) hide skills/CV as opaque inputs — **rejected** (violates FR-003);
(c) compose the default prompt in the frontend — **rejected**: the frontend lacks the `cv_versions`
recipe and the source-CV identity; server composition keeps one source of truth and is unit-testable.

---

## R6 — Backup/restore inclusion specifics + cross-version safety

**Decision**: Add `"tailored_cv"` to `BackupTables.InsertOrder` **after `"offers"`** (it FKs
`offers(id)` cascade; load order is FK-load-bearing — `ZipBackupArchiveStore.ReadAsync` re-sorts the
restore set into `InsertOrder`). The COPY snapshot/restore and manifest need **no change** (columns are
read from `information_schema.columns`; the zip writes one `db/<table>.copy` generically). **Add a new
completeness guard test** asserting the set of mapped data-table names in `AppDbContext.Model` equals
`BackupTables.InsertOrder` (modulo `__EFMigrationsHistory`). The tailored-CV write paths consult the
existing `MaintenanceGate` via `WaitWhileActiveAsync(ct)`.

**Rationale**: today **nothing** cross-checks `BackupTables.InsertOrder` against the EF model — a new
table added without editing that list is **silently omitted from every backup** with no failing test
(the existing `manifest.Tables == BackupTables.InsertOrder` assertion is a tautology). The guard test
permanently protects FR-017 for this and future tables. The new table uses a **non-identity uuid PK**
(`OfferId`, `ValueGeneratedNever`) and **safe column defaults**, so `SchemaInvariantTests`
(no serial/identity) passes and an **OLDER** pre-004 backup restores cleanly into HEAD (the absent
`tailored_cv` rows simply stay empty — no backfill needed, since tailored CVs are opt-in). The
`MaintenanceGate` consult mirrors `EnrichmentService.SubmitResultsAsync` so a restore's `TRUNCATE
tailored_cv` + `cv-data` swap is consistent (FR-020 of 003 reused).

**Alternatives considered**: (a) skip the completeness guard and rely on remembering the list edit —
**rejected**: the silent-omission footgun is exactly what bit-rots backup completeness. (b) auto-discover
the table list from EF metadata at runtime — **rejected** for 003 (it would change 003's explicit,
reviewed, FK-ordered list); a **test** that asserts equality gives the safety without changing 003's
runtime. (c) a `tailored_cv`→`cv-data` referential check in the archive (like the existing
`candidate_cv.file_name` check) — **deferred** as optional hardening (the flat files round-trip
regardless; not required for correctness).

---

## Summary of resolved decisions

| # | Topic | Decision |
|---|-------|----------|
| R1 | Generator | Extend 002 Claude-as-worker; new `/tailor-cv` command drains a loopback `/api/tailored-cv` queue; backend makes no AI call |
| R2 | PDF | Backend renders HTML→PDF via already-present Playwright (`IPdfRenderer`/`PlaywrightPdfRenderer`); worker stays text-only |
| R3 | File storage | Flat `tailored-{OfferId:N}.html/.pdf` in the `cv-data` root → captured by 003 backup unchanged |
| R4 | Concurrency | `GenerationVersion` supersede guard (user-driven), not 002 input-hash auto-staleness |
| R5 | Prompt | Server-assembled draft; editable instructions+skills, source CV attached (visible, not inlined, not hidden); pure Domain composer |
| R6 | Backup | One `BackupTables.InsertOrder` edit (after `offers`) + a new completeness guard test; reuse `MaintenanceGate` |
