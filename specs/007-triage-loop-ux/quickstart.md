# Quickstart & Validation: Triage-Loop UX (007)

A run-and-look validation guide (Constitution Principle VII). It proves each story end-to-end in the running
app. Acceptance detail lives in [spec.md](./spec.md) (User Stories 1–3); this file says how to exercise it.
Implementation steps live in [tasks.md](./tasks.md).

## Prerequisites

- Postgres + backend + frontend running. Either `./start.ps1` (docker-compose Postgres + `dotnet run`), or the
  host workflow: backend `dotnet run` on `:5180` with the Vite dev server (per the run-app note).
- A collected, partly-enriched set of offers (run a scan, then `/enrich` — or `/run-matcher` — to have both
  produced and pending offers, and at least a few applied offers for affinity).
- Check **both light and dark** themes for every visual step.

## Validate by story

### US1 — Feed freshness & filtering (#1, #2, #3, #5)

1. **Live enrichment (#1)**: with an idle queue the banner reads "up to date". Click **Run scan** → when it
   finishes the banner must flip to "N pending" with the /enrich nudge and new cards show "pending" — **no F5**.
   Run `/enrich` in a terminal → the pending count ticks down (~5s) and, at 0, the feed fills in. Stop the
   backend and click **Re-run all** → an inline error appears (not a silent no-op).
2. **Filters (#2)**: type a company/title substring → feed narrows after ~300 ms; set **Work mode = Remote** →
   only remote roles; combine with Source/Sort → they compose (AND).
3. **URL persistence (#3)**: set New + Fit + a source + a search term + Remote → the address bar reflects it;
   **F5**, **Back/Forward**, and opening a **copied URL** all restore the same feed; resetting to defaults
   collapses the URL to `/`; a `?offerId=` deep link still highlights its card.
4. **Freshness + resume (#5)**: the header shows "Last scan: … — {outcome} (N new)" tinted by outcome; reload
   **mid-scan** → "Scanning…" banner, Run scan disabled, list + freshness update on finish; a fresh DB shows no
   freshness line.

### US2 — Detail view as a decision surface (#8, #9, #10) + open=viewed (#4)

1. **Footer actions + badges (#8)**: open a card's **Details** → the footer shows Interested / Dismiss / Tailor
   CV / Mark applied beside "View original ↗", and the header shows status/applied/tailored badges. Mark
   applied → modal → Save flips the header to Applied and the footer to Application / Edit / Unmark, **no page
   refresh**. Press **Escape** inside the modal → only the modal closes.
2. **Prev/next (#9)**: the header shows "‹ n of N ›"; **›** and **ArrowRight** advance (a brief "Loading…", not
   the prior body); ‹ / ArrowLeft step back; ends disable; Escape still closes.
3. **Full signals (#10)**: for a produced offer the drawer shows fit rationale + matched/missing chips and
   affinity rationale + resembles — matching the card; a pending offer shows "Fit/Affinity pending"; cold start
   shows "not enough application history yet"; a failed one shows "… unavailable".
4. **Open = viewed (#4)**: open a **new** offer's detail (directly or via ›) → the header "N new" drops and the
   offer leaves the **New** view (still present under All, actionable). Confirm a background refresh doesn't
   re-fire it. In the **New** view, **Mark all reviewed** empties the queue.

### US3 — Faster, safer triage (#6, #7)

1. **Undo dismiss (#6)**: dismiss a mid-list card → it collapses in place to a one-line "Dismissed — Undo"
   without reshuffling the list or losing scroll; **Undo** within ~6 s restores it (with a "Viewed" badge);
   after the window it leaves the default feed and appears under Dismissed.
2. **Compact card (#7)**: a skill present in multiple sources appears **once** (green matched / red "missing:
   …"); the card is visibly shorter; fit/affinity rationale is behind a "Why this fit/affinity" expander; with
   < 3 applied offers, no per-card cold-start line — only the single page-level hint.

## Green gate

`cd frontend && npm run build && npm test` must pass (the 4 updated `OfferCard` tests included). No backend
build/test change is expected (backend untouched).
