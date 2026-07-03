---
description: "Task list for Triage-Loop UX — Offers feed & offer detail (007)"
---

# Tasks: Triage-Loop UX — Offers Feed & Offer Detail

**Input**: Design documents from `specs/007-triage-loop-ux/` (plan.md, spec.md, research.md, data-model.md,
contracts/, quickstart.md) + the verified audit `docs/ux-review-findings.md` §1–§10.

**Prerequisites**: plan.md (required), spec.md (required). Design docs are lean by design (Principle X).

**Tests**: **Not TDD.** This is a display/interaction change with no new data. The only test work is updating
the **4 existing `OfferCard` tests** that finding #7 affects (T021), plus the green gate (T023). No new
contract/integration tests are requested.

**Organization**: Grouped by the three user stories in spec.md (US1/US2 are P1, US3 is P2). Each finding maps
to a requirement id (FR-00x) and to `docs/ux-review-findings.md §N`, which holds the full evidence + verifier
note per item.

**Scope**: 100% `frontend/src`. **No** backend change, **no** EF migration, **no** new dependency. Design
tokens only (Principle VIII). Status changes use existing endpoints/legal transitions exactly as `OfferCard`
already does (see `contracts/frontend-consumed-endpoints.md`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task). **Note**:
  `frontend/src/pages/Offers/OffersPage.tsx` is a hotspot edited across US1/US2/US3 — those tasks are
  **serialized** in the order given (few are `[P]`).
- **[Story]**: US1 / US2 / US3. Setup, Foundational, and Polish tasks carry no story label.
- Paths are repo-relative.

---

## Phase 1: Setup

**Purpose**: confirm the frontend prerequisites this feature reuses (no initialization needed for an existing app).

- [X] T001 Confirm **no new dependency** and verify the reused client surface exists — `q`/`workMode`
  serialization in `frontend/src/api/offers.ts`, the `poll()` helper in `frontend/src/lib/polling.ts`, and
  `listScans` in `frontend/src/api/scans.ts` — cross-checking `specs/007-triage-loop-ux/contracts/frontend-consumed-endpoints.md`. (No code change.)

---

## Phase 2: Foundational (shared component)

**Purpose**: extract the fit/affinity breakdown into a shared component so the drawer (US2 #10) and the
compacted card (US3 #7) render from **one** source — the divergence finding #10 is about.

**⚠️ Blocks US2 and US3 only** (US1 does not touch it — the MVP can proceed without this phase).

- [X] T002 [P] Create `frontend/src/components/OfferSignals/OfferSignals.tsx` (+ `OfferSignals.css`, tokens
  only) exporting `FitBreakdown({ fit, compact? })` and `AffinityBreakdown({ affinity, compact? })`, moving the
  **exact** JSX from `OfferCard.tsx` (fit block 180–221, affinity block 223–263) and preserving **every**
  lifecycle state (produced score via `fitColorVar` + rationale + matched `chip--skill`/missing `chip--missing`
  chips [fit] / resembles [affinity]; `pending`/`failed` via `enrichmentStatusClass`; affinity `insufficient`
  line with its `data-testid`). `compact` hides fit's matched/missing chips and wraps the rationale in a
  `details/summary` (used by the card; the drawer passes full). Keep the `?? []` guards (optional DTO fields).
- [X] T003 Replace the two inline fit/affinity blocks in `frontend/src/components/OfferCard/OfferCard.tsx` with
  `<FitBreakdown fit={offer.fit} />` / `<AffinityBreakdown affinity={offer.affinity} />` (full mode — **no
  visible change yet**), keeping the outer wrappers + `data-testid`s so existing tests/layout are unchanged.
  (Depends on T002.)

**Checkpoint**: card renders via the shared component with identical behavior; US2 and US3 can now reuse it.

---

## Phase 3: User Story 1 — Feed freshness & filtering (Priority: P1) 🎯 MVP

**Goal**: the feed stays current without a manual refresh, is filterable by title/company + work mode, and
remembers view/sort/source/filters across reload, navigation, and shared links; it also shows last-scan
freshness and can clear the New queue. (FR-001–FR-004, FR-010 part b — findings #1/#2/#3/#5/#4b)

**Independent Test**: run a scan → banner flips to "N pending" with no F5; run `/enrich` → counts tick down and
the feed fills at 0; set New+Fit+source+search+Remote, reload/back/share-link → all restored; header shows
"Last scan: … (N new)"; "Mark all reviewed" empties the New view.

- [X] T004 [US1] Finding #2 — add `workMode` state + a debounced `searchInput`/`q` pair (300 ms `setTimeout`
  effect), thread `q`/`workMode` into the `listOffers` call and `load()` deps, and render a `type=search` box +
  a Work-mode `select` (`""|remote|hybrid|office`, `aria-label`s) in the `offers-page__controls` toolbar, with
  `.offers-page__search` styles, in `frontend/src/pages/Offers/OffersPage.tsx` (+ `OffersPage.css`).
- [X] T005 [US1] Finding #3 — add module-scope `FEED_VIEWS`/`SORT_KEYS` allow-lists + a `readEnumParam` guard,
  move `useSearchParams` above the state block, lazy-init `view`/`sort`/`source`/`q`/`workMode` from the URL,
  and add one replace-mode sync effect (collapse to `/` at defaults; don't clobber `?offerId=`) in
  `frontend/src/pages/Offers/OffersPage.tsx`. (Depends on T004.)
- [X] T006 [P] [US1] Finding #1 — add `refreshKey?`/`onProduced?` props to
  `frontend/src/components/EnrichmentIndicator/EnrichmentIndicator.tsx`: re-fetch status on `refreshKey`
  change, poll while `pendingTotal > 0` via `poll()` (~5 s, resolve **once** at 0 → `onProduced()`), and add a
  `catch` + inline `.enrichment-indicator__error` (`var(--c-danger)`) to the re-run (+ `EnrichmentIndicator.css`).
- [X] T007 [US1] Finding #1 — bump `enrichmentRefresh` after `handleRunScan`'s `await load()` and mount
  `<EnrichmentIndicator refreshKey={enrichmentRefresh} onProduced={() => { void load(); void loadTailored() }} />`
  in `frontend/src/pages/Offers/OffersPage.tsx`. (Depends on T005, T006.)
- [X] T008 [P] [US1] Finding #5 — add `formatRelativeTime(iso)` and a shared `outcomeClass` chip mapping to
  `frontend/src/lib/format.ts`, and refactor `frontend/src/pages/Scans/ScansPage.tsx` to reuse `outcomeClass`.
- [X] T009 [US1] Finding #5 — add `lastScan` state + a non-fatal `listScans` mount effect, extract
  `pollScanRun(id)`, resume an in-flight run (`finishedAt === null` → poll + disable Run scan), and render the
  `.offers-page__freshness` line ("Last scan: {relative} — {outcome} ({counts.new} new)", tinted by the shared
  `outcomeClass`) in `frontend/src/pages/Offers/OffersPage.tsx` (+ `OffersPage.css`). (Depends on T007, T008.)
- [X] T010 [US1] Finding #4 (part b) — render a "Mark all reviewed" button (shown only in the New view) whose
  handler `Promise.all`s `setOfferStatus(id, 'viewed')` over the loaded ids then `load()`s, in
  `frontend/src/pages/Offers/OffersPage.tsx`. (Depends on T009.)

**Checkpoint**: US1 is a shippable MVP — the feed is live, filterable, URL-persisted, fresh, and clearable.

---

## Phase 4: User Story 2 — Detail view as a decision surface (Priority: P1)

**Goal**: from the offer detail view the user sees full fit/affinity state + breakdown, moves prev/next through
the visible feed, and takes the card's triage actions (applied/tailor/interested/dismiss) with live badges;
opening an offer marks it viewed. (FR-005–FR-007, FR-010 part a — findings #8/#9/#10/#4a)

**Independent Test**: open a card's Details → footer shows the triage actions + header badges; Mark applied →
modal → Save flips to Applied (no refresh); ‹/› + arrows walk the feed with an "n of N" counter and a loading
state; fit/affinity show rationale + chips + all states; opening a "new" offer removes it from the New view.

**Depends on Phase 2 (T002) for T013.**

- [X] T011 [US2] Finding #8 — widen `OfferDetailDrawer` Props with the callbacks `OfferCard` takes
  (`onSetStatus`, `onMarkApplied`, `onClearApplied`, `onOpenApplication`, `tailoredState`, `onTailoredChanged`),
  extract the fetch into `loadDetail()` (re-run after each action), add nested `ApplyModal`/`TailorCvModal`
  state, render the footer action set + header status/applied/tailored badges (from `detail.offer`), and
  **no-op the drawer's document-keydown Escape while a nested modal is open** — with footer/badge styles — in
  `frontend/src/components/OfferDetail/OfferDetailDrawer.tsx` (+ `.css`).
- [X] T012 [US2] Finding #8 — pass `onSetStatus={handleSetStatus}`, `onMarkApplied={handleMarkApplied}`,
  `onClearApplied={handleClearApplied}`, `onOpenApplication={setOpenApplicationId}`,
  `onTailoredChanged={loadTailored}`, and `tailoredState={openDetailId ? tailoredByOffer[openDetailId] : undefined}`
  to the drawer mount in `frontend/src/pages/Offers/OffersPage.tsx`. (Depends on T011; OffersPage — after T010.)
- [X] T013 [US2] Finding #10 — replace the bare-score signals block (`:87-98`) with `<FitBreakdown fit=… />`
  / `<AffinityBreakdown affinity=… />` (full mode) inside `.offer-detail__signals`, switched to a column
  layout, in `frontend/src/components/OfferDetail/OfferDetailDrawer.tsx` (+ `.css`). (Depends on T011 same file
  + T002.)
- [X] T014 [US2] Finding #9 — add optional `offerIds?`/`onNavigate?` props, recompute `index`/`hasPrev`/
  `hasNext` every render, `setDetail(null)+setError(null)` at the top of the fetch effect, render the
  `.offer-detail__nav` ‹ "n of N" › controls, and handle ArrowLeft/ArrowRight in the shared keydown effect
  (guarded on `!e.defaultPrevented` so a nested modal opts out) in
  `frontend/src/components/OfferDetail/OfferDetailDrawer.tsx` (+ `.css`). (Depends on T013 same file.)
- [X] T015 [US2] Finding #9 — pass `offerIds={data?.data.map((o) => o.offerId) ?? []}` and
  `onNavigate={setOpenDetailId}` to the drawer mount in `frontend/src/pages/Offers/OffersPage.tsx`. (Depends on
  T012 same file + T014.)
- [X] T016 [US2] Finding #4 (part a) — add `markOfferViewed(offerId)` (optimistic `setData` → `userStatus:
  'viewed'` + decrement `meta.new`, non-fatal on failure) invoked from `handleOpenDetail` **and** the prev/next
  navigate handler, **ref-guarded** so a background refetch doesn't re-fire it, in
  `frontend/src/pages/Offers/OffersPage.tsx`. (Depends on T015.)

**Checkpoint**: US2 — the drawer is a full decision surface; opening an offer marks it viewed.

---

## Phase 5: User Story 3 — Faster, safer triage (Priority: P2)

**Goal**: a shorter offer card (one deduped skills row, collapsed rationale, no cold-start noise) and an
undoable dismiss. (FR-008, FR-009 — findings #6/#7)

**Independent Test**: dismiss a mid-list card → collapses in place to a 6 s "Dismissed — Undo" with no
reshuffle; Undo restores it; after the window it leaves the default feed. A multi-source skill appears once;
rationale is behind an expander; cold start shows only the page-level hint.

**Depends on Phase 2 (T002/T003) for T019.**

- [X] T017 [US3] Finding #6 — add `dismissStubs`/`dismissTimers` state; route `next==='dismissed'` through
  `handleDismiss` (capture the `OfferDto`, optimistic `setOfferStatus('dismissed')` with no `load()`, schedule
  `commitDismiss` at 6000 ms), `commitDismiss` (drop stub + `setData` filter), `handleUndoDismiss`
  (`setOfferStatus('viewed')` + in-place flip); swap the feed-map row to the stub; clear stubs/timers on
  `view|sort|source` change and unmount — in `frontend/src/pages/Offers/OffersPage.tsx`. (Depends on T016 same
  file.)
- [X] T018 [P] [US3] Finding #6 — create `frontend/src/components/OfferCard/DismissedStub.tsx` (one-line row,
  `data-offer-id`, `btn btn--ghost btn--sm` Undo) + `.offer-card--dismissed-stub` styles in
  `frontend/src/components/OfferCard/OfferCard.css` (tokens only).
- [X] T019 [US3] Finding #7 — build one case-insensitively deduped skills row (missing → matched →
  keySkills/requiredSkills; first-seen wins; missing rendered `chip chip--missing` text `missing: {s}`),
  **remove** the three old chip rows, pass `compact` to `FitBreakdown` (hides its matched/missing chips +
  collapses rationale via `details/summary`) so the card stays compact while the drawer keeps the full
  breakdown, and add a `suppressAffinityInsufficient?` prop, updating `OfferCard.css` (remove orphaned rules) —
  in `frontend/src/components/OfferCard/OfferCard.tsx` (+ `.css`). (Depends on T003 same file + T002.)
- [X] T020 [US3] Finding #7 — pass `suppressAffinityInsufficient={data.meta.hasAffinityBasis === false}` from
  the feed map in `frontend/src/pages/Offers/OffersPage.tsx`. (Depends on T017 same file.)
- [X] T021 [P] [US3] Finding #7 — update the 4 affected tests (skill appears once via `getByText`; rationale
  behind `details` — query `{ hidden: true }` or open the summary; preserve `missing: Kafka`; cold-start
  asserts the page-level hint + no per-card `insufficient` line when suppressed) in
  `frontend/tests/offers/{OfferCard.fit,OfferCardAffinity,AffinityColdStart,OffersPage}.test.tsx`.

**Checkpoint**: US3 — compact cards + an undoable dismiss.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T022 [P] Audit all new/edited CSS for **design-token-only** compliance (no hardcoded colors/sizes,
  Principle VIII) across `OfferSignals.css`, `EnrichmentIndicator.css`, `OffersPage.css`,
  `OfferDetailDrawer.css`, `OfferCard.css`.
- [X] T023 Green gate: `cd frontend && npm run build && npm test` — all pass, including the 4 updated
  `OfferCard` tests (backend untouched, Principle VI).
- [~] T024 Visual verification per `specs/007-triage-loop-ux/quickstart.md` — US1–US3 + #4 — in **light AND
  dark** themes (Principle VII); no console/type errors. Commit per story (Conventional Commits, one logical
  change each).
  - **Done (automated/e2e)**: `tsc` clean; `oxlint` clean; **90 tests pass ×4 stable**; `npm run build` OK;
    CSS token-only audit passed; app served from the fresh build on `:5180` with real data — the feature
    endpoints return the new params end-to-end (`/api/offers?q=…&workMode=remote` → 31 filtered;
    `/api/enrichment/status` → 486 pending; `/api/scans` → latest `partial`, 10 new).
  - **Pending (needs the user)**: interactive **light + dark** eyeball pass in the browser (the Claude
    Chrome extension was not connected in this environment) and the **per-story commits** (held per the
    commit-only-when-asked policy). Backend host is left running on `:5180` for the visual pass.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: after Setup; **blocks US2 (T013) and US3 (T019)** — but **not US1**. US1 (the MVP)
  can start immediately after Setup.
- **US1 (Phase 3, P1)**: independent — the MVP.
- **US2 (Phase 4, P1)**: drawer-only tasks (T011/T013/T014) are independent of US1 and can run alongside it;
  the OffersPage wiring (T012/T015/T016) serializes after US1's OffersPage tasks (shared file).
- **US3 (Phase 5, P2)**: T019 depends on Phase 2 + T003 (shared `OfferCard.tsx`); the OffersPage tasks
  (T017/T020) serialize after US2's OffersPage tasks.
- **Polish (Phase 6)**: after the desired stories.

### Shared-file serialization (apply in this order)

- `pages/Offers/OffersPage.tsx`: **T004 → T005 → T007 → T009 → T010 → T012 → T015 → T016 → T017 → T020**.
- `components/OfferDetail/OfferDetailDrawer.tsx`: **T011 → T013 → T014**.
- `components/OfferCard/OfferCard.tsx`: **T003 → T019**.

### Cross-story coordination (the #7 ↔ #10 reconciliation)

`FitBreakdown` carries a `compact` prop (T002): the **card** (T019) uses `compact` — its own deduped skills row
replaces the component's matched/missing chips and its rationale is collapsed — while the **drawer** (T013)
uses full mode. This lets one component serve both without the card losing compactness or the drawer losing the
breakdown.

### Risks (from the audit synthesis)

- **Optimistic races**: T017's dismiss stub (6 s, no `load()`) and T016's open→viewed `setData` must reconcile
  with T007's `onProduced` reload and T009's post-scan `load()` — clear stubs/timers on filter change + unmount;
  ref-guard `markOfferViewed`; reload the feed only **once** at `pending==0` (not per tick) and never while the
  drawer is open or an undo is in flight.
- **Escape/arrow double-fire**: T014's arrow keys share the drawer's document-keydown effect with T011's nested
  modal — the `!e.defaultPrevented` / modal-open guard must suppress navigation while a modal is open.
- **`?sort=xyz`**: keep T005's allow-listed `readEnumParam` so a hand-edited/garbage param can't break sort.

### Parallel opportunities

- **[P] tasks** (different files, no unfinished dep): T002, T006, T008, T018, T021, T022.
- Cross-team: while one dev runs US1's OffersPage chain, another can build the US2 drawer (T011/T013/T014) and a
  third the `OfferSignals` extraction (T002) + tests (T021).

---

## Parallel Example: kickoff

```text
# After T001, these have no unfinished dependency and touch distinct files:
Task T002: Create components/OfferSignals/ (FitBreakdown/AffinityBreakdown)   # Foundational
Task T006: EnrichmentIndicator live refresh + rerun error                     # US1
Task T008: formatRelativeTime + shared outcomeClass in lib/format.ts          # US1
# US1's OffersPage chain (T004→T005→…) proceeds in parallel with the above.
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 (Setup) → 2. Phase 3 (US1) → 3. **STOP and validate** US1 in the running app (both themes) →
4. ship. US1 does **not** require Phase 2.

### Incremental delivery

1. Setup → US1 (MVP: live/filterable/persisted/fresh feed).
2. Phase 2 (OfferSignals) → US2 (drawer decision surface + open=viewed).
3. US3 (compact cards + undoable dismiss).
4. Polish (token audit, green gate, visual pass). Each story is demoable on its own.

---

## Notes

- `[P]` = different files, no unfinished dependency. `OffersPage.tsx` is the serialization hotspot — respect the
  order above.
- `[Story]` maps each task to a user story for traceability; every finding also links to
  `docs/ux-review-findings.md §N`.
- No backend, migration, or dependency change; nothing leaves the user's machine.
- Commit after each story (Conventional Commits); run the app and look (Principle VII) before calling a story
  done.
