# Data Model: Tailored CV per Job Offer

**Feature**: `004-tailored-cv-generation` | **Date**: 2026-06-30 | **Plan**: [plan.md](./plan.md)

This feature **adds one aggregate/table** (`tailored_cv`) plus the worker-queue state that governs it.
It does **not** alter any 001/002/003 entity (FR-019); those are referenced only where the new data
attaches. Layering is unchanged: framework-free **Domain**, ports in **Application**, EF Core in
**Infrastructure**. Conventions retained: wrapped IDs (`OfferId`, `CvId`), value objects as immutable
`record`s, collections persisted as **jsonb string arrays** via `HasJsonbListConversion<string>()`,
**append-only** EF migrations, `Result<T>` for expected failures, satellite aggregates with a private
parameterless ctor + `private set` + static factory + intent-named mutators (the `OfferFit` shape).

---

## 1. New enum (Domain)

```
Domain/TailoredCv/TailoredCvState.cs
  enum TailoredCvState { Pending, Produced, Failed }
```

`Pending` is the state at create and after every (re)generate. `Produced` means **both** the HTML and
the rendered PDF exist on disk. `Failed` is terminal until the user regenerates (a fresh request
re-arms it). There is **no** `Unreadable` (that is a CV-profile concept) and **no** auto-invalidation
(R4) — so, unlike 002, there is no eager-invalidation hook and no per-offer satellite invariant.

> Decision: a **dedicated** enum (not reuse of `EnrichmentState`) keeps `Domain/TailoredCv` independent
> of `Domain/Enrichment` (no cross-aggregate coupling). The shapes coincide; the semantics are owned here.

---

## 2. `TailoredCv` (new aggregate / table `tailored_cv`)

**Opt-in, latest-only, 1:1 with `Offer`** — PK = `OfferId` = FK → `offers(id)` cascade. Unlike the 002
satellites, a row exists **only** when the user has requested a tailored CV for that offer (no eager
materialisation, no backfill). The dedicated page lists exactly the existing rows.

| Field | Type | Column | Notes |
|---|---|---|---|
| `OfferId` | `OfferId` (PK/FK) | `offer_id uuid` | reuse wrapped id; `ValueGeneratedNever`; FK→`offers(id)` cascade |
| `SourceCvId` | `CvId` | `source_cv_id uuid` | which uploaded CV this was generated from (provenance); **no enforced FK** — the artifact survives the source CV being replaced/deleted |
| `State` | `TailoredCvState` | `state varchar(20)` | `HasConversion<string>()`; default `Pending` |
| `Attempts` | `int` | `attempts int` | default 0; resets on (re)generate; drives `Failed` at `RetryLimit` |
| `GenerationVersion` | `int` | `generation_version int` | default 1; **bumped on every (re)generate**; the worker echoes it; write-back accepted only if it matches (R4) |
| `Prompt` | `string` | `prompt text` | the **exact** instruction text used (editable; stored as used — FR-003/SC-003) |
| `EmphasisedSkills` | `IReadOnlyList<string>` | `emphasised_skills jsonb` | `HasJsonbListConversion<string>()`; default `'[]'`; the user's selected skills |
| `HtmlFileName` | `string?` | `html_file_name varchar(120)` | `tailored-{OfferId:N}.html`; null until produced |
| `PdfFileName` | `string?` | `pdf_file_name varchar(120)` | `tailored-{OfferId:N}.pdf`; null until produced |
| `GeneratedAt` | `DateTimeOffset?` | `generated_at timestamptz` | set on produce |
| `LastError` | `string?` | `last_error text` | reason of last failed attempt / render failure |
| `CreatedAt` | `DateTimeOffset` | `created_at timestamptz` | first request time |

> **FK-cascade invariant (FR-018).** The `offer_id` FK is `ON DELETE CASCADE`, yet FR-018 requires a
> tailored CV to persist when its offer "is removed from the feed". These reconcile because **offers are
> only ever soft-marked unavailable — never hard-deleted** in 001/002/003 (verified: no offer `DELETE`/
> `Remove`/`RemoveRange` path exists in the backend). So the cascade never fires in normal operation; a
> delisted/unavailable offer keeps its row and therefore its tailored CV (tested by T046). The cascade
> exists only for referential integrity under the 003 restore's full `TRUNCATE … CASCADE` (which wipes
> all stores consistently). *If a future feature ever hard-deletes offers, revisit this FK (e.g. detach/
> archive the tailored CV) so FR-018 still holds.*
>
> **Spelling.** This feature uses British **"emphasised"** uniformly across the C# property
> (`EmphasisedSkills`), the JSON field (`emphasisedSkills`), and the DB column (`emphasised_skills`).

**Methods** (Domain, framework-free):

```
static TailoredCv CreateRequest(OfferId offer, CvId sourceCv, string prompt,
                                 IReadOnlyList<string> emphasisedSkills, DateTimeOffset at)
    // State=Pending, Attempts=0, GenerationVersion=1, CreatedAt=at

void RequestRegeneration(CvId sourceCv, string prompt, IReadOnlyList<string> emphasisedSkills,
                         DateTimeOffset at)
    // supersede: State=Pending, Attempts=0, GenerationVersion++, update prompt/skills/sourceCv

void MarkProduced(int generationVersion, string htmlFileName, string pdfFileName, DateTimeOffset at)
    // guard: caller only invokes when generationVersion == GenerationVersion && State==Pending
    // State=Produced, set file names + GeneratedAt, clear LastError

void RecordFailure(int generationVersion, string? error, int retryLimit)
    // guard as above; Attempts++; State=Failed when Attempts >= retryLimit (else stays Pending)

bool Accepts(int generationVersion) => State == Pending && generationVersion == GenerationVersion
```

`Accepts` is the **supersede guard** the service checks before `MarkProduced`/`RecordFailure`; a
write-back for an old `generationVersion` (a slow worker after a newer regenerate) is rejected
(`superseded`) and nothing is written. **`PdfFileName`/`HtmlFileName` are only meaningful when
`State==Produced`** — download is offered only then (FR-009).

Index on `State` (the pending query) — `HasIndex(t => t.State)`.

---

## 3. `TailoredCvPrompt` — the default-prompt composer (Domain, pure)

```
Domain/TailoredCv/TailoredCvPrompt.cs   (pure, framework-free, unit-tested)

static string BuildDefault(TailoredCvOfferView offer, IReadOnlyList<string> emphasisedSkills)
```

Produces the editable instruction text from: the offer (title/company/seniority), the emphasised
skills (interpolated as a bullet/inline list so a skill-toggle visibly changes the prompt — FR-004),
the **`cv_versions` two-column A4 layout** instruction (FR-007), and the explicit **no-fabrication**
rule (FR-006 — "use only what is in the attached CV; re-emphasise and reorder, never invent
employers, dates, roles, or skills"). It does **not** embed the CV text (the CV is attached, R5).
`TailoredCvOfferView` is a small Application/Domain view carrying the offer fields needed (no EF types).

> The composer is pure so the default prompt is deterministic and testable; the user's edits replace it
> wholesale before generation, and whatever is submitted is stored verbatim as `Prompt`.

---

## 4. Emphasised-skills default selection (Application)

Computed in `TailoredCvService` when building the draft, from data **already on the read model**
(no new collection): the offer's enriched `KeySkills` (when `OfferEnrichment` is `Produced`) plus the
fit `Matched` ∪ `Missing` (when `OfferFit` is `Produced`), de-duplicated; **fallback** to the offer's
own `RequiredSkills` ∪ `NiceToHaveSkills` when enrichment/fit are still pending. The user toggles the
set in the modal; the chosen set is what `BuildDefault` interpolates and what is persisted.

---

## 5. Source-CV selection (Application)

`TailoredCvService` resolves the source CV as: the request's explicit `sourceCvId` if given and valid,
else the **most-recently-uploaded readable** CV (`ICvRepository.GetReadableAsync`, newest), else the
most-recent CV, else **none** → generate/draft returns a `NoCvOnFile` `Result` failure and the modal
shows the "add a CV first" empty state (edge case). The resolved `CvId` is stored on the row.

---

## 6. Lifecycle / state machine

```
            (POST /offer/{id}, no row)                 (POST /offer/{id}, row exists)
 ─────────────────────────────────────────     ─────────────────────────────────────────
                 CreateRequest                          RequestRegeneration (version++)
                      │                                              │
                      ▼                                              ▼
                  ┌────────┐   worker result (Accepts✓, valid HTML)  ┌──────────┐
                  │ Pending │ ───────────── render PDF ────────────▶ │ Produced │
                  └────────┘                                         └──────────┘
                      │  worker result (Accepts✓, failed/empty/render error)   │
                      │                                                         │  (regenerate)
                      ▼  Attempts++ ; Failed at RetryLimit                      ▼
                  ┌────────┐                                            back to Pending
                  │ Failed │ ──── (regenerate → RequestRegeneration) ──▶ Pending
                  └────────┘
   write-back with stale generationVersion  → "superseded" (no state change, nothing written)
```

- **Retry limit** reuses `AppSettings.Enrichment.RetryLimit` (default 3) — no new settings/column (R/Plan §X).
- A restore (003) `TRUNCATE`s `tailored_cv` and swaps `cv-data`; in-flight write-backs **defer** through
  the restore window via `MaintenanceGate.WaitWhileActiveAsync` (mirrors `EnrichmentService`).

---

## 7. Ports (Application) + adapters (Infrastructure)

```
Application/TailoredCv/ITailoredCvRepository.cs
  Task<TailoredCv?> GetByOfferAsync(OfferId, ct)              // tracked
  Task<IReadOnlyList<TailoredCv>> GetAllAsync(ct)             // read-only, for the dedicated page
  Task AddAsync(TailoredCv, ct)
  void Remove(TailoredCv)
  Task<IReadOnlyList<TailoredCv>> GetPendingAsync(int limit, ct)   // State==Pending, ordered, AsNoTracking
  Task<TailoredCvCounts> GetCountsAsync(ct)                   // pending/failed/produced totals

Application/TailoredCv/ITailoredCvFileStore.cs
  Task<TailoredCvFiles> SaveAsync(OfferId, string html, byte[] pdf, ct)   // writes flat tailored-{id:N}.html/.pdf
  string GetPdfAbsolutePath(OfferId)                          // for the download endpoint
  string? GetHtml(OfferId)                                    // optional reopen/source
  void Delete(OfferId)                                        // remove both files
  // resolves the SAME directory as LocalCvFileStore: Cv:StoragePath ?? {BaseDirectory}/cv-data

Application/TailoredCv/IPdfRenderer.cs
  Task<byte[]> RenderA4Async(string html, ct)                 // PlaywrightPdfRenderer
```

`TailoredCvService` (Application, `AddScoped`) orchestrates: `GetDraftAsync`, `GenerateAsync`
(create/regenerate → `Result<TailoredCvView>`), `GetPendingWorkAsync`, `SubmitResultsAsync` (render +
persist, gate-deferred), `ListAsync`, `GetAsync`, `DeleteAsync`. Adapters: `TailoredCvRepository`
(EF, tracked gets + bulk/read queries), `LocalTailoredCvFileStore` (flat files in `cv-data`),
`PlaywrightPdfRenderer` (singleton, lazy Chromium, `IAsyncDisposable`).

---

## 8. EF configuration & DbContext

`Infrastructure/Persistence/Configurations/TailoredCvConfiguration.cs` — table `tailored_cv`, PK
`offer_id` (`ValueGeneratedNever`); `state` `HasConversion<string>()` maxlen 20; `emphasised_skills`
`HasJsonbListConversion<string>()` default `'[]'`; `source_cv_id` uuid (the `CvId` global converter
applies — **no** new `ConfigureConventions` line); `HasOne<Offer>().WithOne().HasForeignKey<TailoredCv>
(t => t.OfferId).OnDelete(Cascade)`; `HasIndex(t => t.State)`. Auto-discovered by
`ApplyConfigurationsFromAssembly`. `AppDbContext` adds `DbSet<TailoredCv> TailoredCvs => Set<…>()`.

---

## 9. Migration plan — `TailoredCv` (one new migration; Principle IX)

`dotnet ef migrations add TailoredCv` (next after `20260629112020_LlmEnrichment`). New migration only —
**no prior migration edited**. `Up()`:

1. `CreateTable("tailored_cv", …)` — `offer_id uuid` PK, `source_cv_id uuid NOT NULL`,
   `state varchar(20) NOT NULL DEFAULT 'Pending'`, `attempts int NOT NULL DEFAULT 0`,
   `generation_version int NOT NULL DEFAULT 1`, `prompt text NOT NULL DEFAULT ''`,
   `emphasised_skills jsonb NOT NULL DEFAULT '[]'`, `html_file_name varchar(120) NULL`,
   `pdf_file_name varchar(120) NULL`, `generated_at timestamptz NULL`, `last_error text NULL`,
   `created_at timestamptz NOT NULL DEFAULT now()`; FK `offer_id → offers(id)` ON DELETE CASCADE;
   `CREATE INDEX IX_tailored_cv_state`.
2. (no other tables/columns touched.)

`Down()` drops `tailored_cv`. **Additive with safe defaults** — preserves the 003 `SchemaInvariantTests`
(non-identity uuid PK, no serial) and the OLDER-backup→HEAD invariant (an old backup lacking the table
restores into HEAD with an empty `tailored_cv`; **no backfill** needed — tailored CVs are opt-in).

---

## 10. Backup/restore inclusion (FR-017) — exact edits

| File | Change |
|---|---|
| `Application/Backup/BackupTables.cs` | Add `"tailored_cv"` to `InsertOrder` **after `"offers"`** (FK→offers; load order is FK-load-bearing). The COPY snapshot, manifest, truncate, and reload all derive from this list — no other backup edit. |
| `tests/Infrastructure.Tests` (new) | **Completeness guard test**: assert the set of mapped data-table names in `AppDbContext.Model.GetEntityTypes()` (minus `__EFMigrationsHistory`) equals `BackupTables.InsertOrder` — so this table, and any future table, cannot be silently omitted from backups. |
| (none) | `PostgresSnapshotStore` (columns catalog-driven), `ZipBackupArchiveStore`, `BackupManifest`, `BackupService`/`RestoreService`, `LocalCvFileStore` enumerate/zip/swap — **unchanged** (flat files in `cv-data` + a generic per-table `.copy` are already covered). |

---

## 11. Read-model / DTO additions (no change to existing offer DTOs)

The `OfferListItem` feed DTO is **unchanged**; the frontend already carries the per-offer skill data the
modal needs. New DTOs live only under the tailored-CV contracts:

- `TailoredCvView` (UI): `{ offerId, offerTitle, company, sourceCvId, state, generationVersion,
  emphasisedSkills, prompt, hasPdf, generatedAt, lastError }`.
- `TailoredCvDraftView` (UI, not persisted): `{ offerId, prompt, emphasisedSkills, allOfferSkills,
  sourceCv: { id, fileName } | null }` — the prefilled modal.
- Worker pending item + result item — see [contracts/worker-protocol.md](./contracts/worker-protocol.md).
