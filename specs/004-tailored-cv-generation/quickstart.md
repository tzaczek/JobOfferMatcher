# Quickstart & Validation: Tailored CV per Job Offer

**Feature**: `004-tailored-cv-generation` | **Date**: 2026-06-30 | **Plan**: [plan.md](./plan.md)

A run + per-user-story validation guide. It references [contracts/tailored-cv-api.md](./contracts/tailored-cv-api.md),
[contracts/worker-protocol.md](./contracts/worker-protocol.md), and [data-model.md](./data-model.md)
rather than restating them. No implementation code here.

## Prerequisites

- The 002 enrichment feature works (an uploaded CV with a **produced** profile, and offers with
  enriched key-skills/fit, give the richest tailoring inputs — though tailoring works from the raw CV +
  offer skills even before enrichment runs).
- Playwright's Chromium is provisioned (already required by the 001 theprotocol scraper in the
  host-process run mode; if a fresh checkout, `playwright install chromium` once).
- The `cv_versions/` folder is present in the repo (the worker reads `v2_two_column.html` + `NOTES.md`).

## Run

```powershell
./start.ps1            # docker-compose Postgres + dotnet run (host process) on http://localhost:5180
```

Migrations apply at startup (`MigrateAsync`) — the new `TailoredCv` migration creates `tailored_cv`.
Open the SPA, upload a CV on **CV & Profile** if you haven't, and run `/enrich` once so offers have
key-skills/fit (optional but recommended).

## The worker

In a Claude Code session in the repo:

```
/tailor-cv
```

It drains `GET /api/tailored-cv/pending`, reads the source CV by path + the `cv_versions` layout,
produces tailored HTML, and posts to `POST /api/tailored-cv/results`; the backend renders each to a PDF.
Re-runnable and idempotent; stops when the queue is empty.

---

## User Story 1 — Generate a tailored CV from an offer (P1)

**Validate**:
1. On the Offers feed, an offer card shows a **"Tailor CV"** action. Click it → a modal opens.
2. The modal **points out the offer's skills** (emphasised-skills chips, pre-selected) and shows the
   **exact prompt** (editable `<textarea>`) plus the **attached source CV** name (read-only).
3. Click **Generate** → the tailored CV shows **pending** (`enrichmentStatusClass` chip).
4. Run `/tailor-cv` in a Claude session → the state flips to **produced**.
5. Reopen the modal (or the dedicated page) → the generated CV is available; spot-check that emphasised
   skills are foregrounded and that **no employer/date/skill appears that isn't in your source CV**
   (SC-002 / FR-006).

**Acceptance**: AC1 (modal shows skills + exact prompt), AC2 (generation produces a tailored CV), AC3
(no fabrication), AC4 (pending before the worker runs).

## User Story 2 — See, edit, regenerate from the exact prompt (P1)

**Validate**:
1. In the modal, confirm the **full** prompt is visible and editable (nothing hidden; the CV is shown as
   attached, not pasted into the box).
2. Toggle a skill off/on → the **prompt text updates** to reflect the selection (FR-004).
3. Edit the prompt (e.g. add "lead with leadership experience"), click **Regenerate**. The state returns
   to **pending** and `generationVersion` increments.
4. Run `/tailor-cv` → the **new** CV reflects your edit and **replaces** the prior one (latest-only).
5. Confirm via `GET /api/tailored-cv/offer/{id}` that the stored `prompt` equals what you submitted
   (SC-003 — shown == used).

**Acceptance**: AC1 (full prompt visible), AC2 (edit→regenerate→new CV supersedes), AC3 (skill toggle
updates prompt), AC4 (stored prompt == used prompt).

**Supersede check**: regenerate twice quickly, then run the worker — the older result returns
`outcome: "superseded"` (visible in the worker's POST response) and does not overwrite the newer request.

## User Story 3 — Download the tailored CV as a polished PDF (P2)

**Validate**:
1. With a **produced** tailored CV, click **Download** → a `*.pdf` saves (blob→`<a download>` like
   backup). Open it: a **two-column A4** CV, correct page count, intact layout (matches `cv_versions`).
2. While a CV is **pending** or **failed**, the **Download action is unavailable** (FR-009) — confirm
   `GET …/download` returns `409 TailoredCvNotReady`.

**Acceptance**: AC1 (print-correct PDF download), AC2 (download unavailable until produced).

## User Story 4 — Store + reach from the offer and a dedicated page (P2)

**Validate**:
1. Generate tailored CVs for **two** offers.
2. Each offer card indicates a tailored CV exists and lets you **reopen** it (view/edit-prompt/
   regenerate/download).
3. Open **Tailored CVs** in the nav → both are listed, each **links back to its offer** with view /
   regenerate / download (and Remove).
4. Reopen one → it shows the generated CV **with the exact prompt + emphasised skills** that produced it.
5. Mark one offer's source unavailable (or let it delist) → its tailored CV **still appears** on the
   dedicated page and downloads (FR-018).

**Acceptance**: AC1 (offer shows + reopens), AC2 (dedicated page lists all + links back), AC3 (reopen
shows prompt+skills), AC4 (persists when offer unavailable).

---

## Cross-cutting validation

- **Locality (SC-007 / Principle IV)**: with the app running and `/tailor-cv` active, confirm the
  backend makes no external AI call (the no-AI-package guard test passes; only loopback traffic on
  `/api/tailored-cv/*`). Hitting `/api/tailored-cv/pending` from a non-loopback address returns `403`.
- **Backup/restore (FR-016/FR-017)**: Settings → **Backup** downloads a zip that contains the
  `tailored_cv` rows (`db/tailored_cv.copy`) **and** the flat `cv-data/tailored-*.html/.pdf` files.
  Restore it into a wiped DB → the tailored CVs (and their PDFs) come back. The **new completeness guard
  test** fails if `tailored_cv` were ever missing from `BackupTables.InsertOrder`.
- **No-CV edge case**: with no CV uploaded, the modal shows an "add a CV first" state and
  `GET …/draft` returns `409 NoCvOnFile` — generation is not offered (no fabrication).
- **Regression (FR-019)**: the untouched 001/002/003 suites stay green (collection, feed, enrichment,
  fit, backup/restore unchanged).

## Test inventory (per Principle V/VI)

- **Domain.Tests**: `TailoredCv` state machine + `GenerationVersion`/`Accepts` supersede; `TailoredCvPrompt.BuildDefault`.
- **Application.Tests**: `TailoredCvService` generate/regenerate (version bump), write-back
  accept-vs-superseded, render-failure→failed, `NoCvOnFile`/`OfferNotFound` `Result`s, default
  emphasised-skills selection + source-CV selection.
- **Infrastructure.Tests (real Postgres)**: `tailored_cv` round-trip + jsonb; e2e draft→generate→
  pending→results(HTML)→**fake-render**→produced→download; backup/restore incl. a `tailored_cv` row +
  flat files; **`BackupTables` completeness guard**; `SchemaInvariantTests` (existing) over the new
  table; loopback guard (HTTP layer); one **real** `PlaywrightPdfRenderer` render smoke test; no-AI-package
  guard (extended).
- **Frontend (Vitest + RTL)**: modal (skill toggle ↔ prompt update, generate/regenerate/download/
  pending/failed), the dedicated page, the `tailoredCv` client (incl. the blob PDF download).
