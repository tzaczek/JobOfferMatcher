# Implementation Plan: Triage-Loop UX — Offers Feed & Offer Detail

**Branch**: `007-triage-loop-ux` (spec directory; no git branch created — repo has no `before_specify` hook) | **Date**: 2026-07-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/007-triage-loop-ux/spec.md`

## Summary

Make the daily **triage loop** — the Offers feed and the offer detail view — current, filterable, actionable,
and reversible, by implementing the ten verified UX-audit findings (#1–#10 in `docs/ux-review-findings.md`,
re-confirmed against HEAD). **Technical approach**: a **frontend-only** React change that reuses existing
backend endpoints, DTO fields, and the `lib/polling.ts` `poll()` helper — **no backend change, no EF
migration, no new dependency**. The work is organized around **four shared refactors** (URL-backed toolbar
state; an optimistic feed-status update that reconciles with background refetches; a widened
`OfferDetailDrawer`; and extracted `FitBreakdown`/`AffinityBreakdown` components reused by the card and the
drawer) so the interacting findings compose instead of colliding. The concrete, file-level task sequence
already exists in [tasks.md](./tasks.md) (grounded and dependency-ordered); this plan records the technical
context, the constitution gates, and why the heavier design artifacts are intentionally minimal.

## Technical Context

**Language/Version**: TypeScript (React SPA via Vite); .NET 10 backend **unchanged**.

**Primary Dependencies**: React + `react-router-dom` v7 (functional `setSearchParams` updater), the existing
`lib/polling.ts` `poll()` helper. **No new npm or NuGet dependency.**

**Storage**: **N/A** — no new persistent data, **no EF migration, no schema change**. All new state is
transient client state (URL search params + component state).

**Testing**: Vitest + React Testing Library for frontend view logic — **4 existing `OfferCard` tests** are
updated (finding #7's card compaction); backend xUnit suite is untouched. **Visual verification (Principle
VII) is the primary acceptance gate** — every story ships a "run the app and look" check in light and dark
themes.

**Target Platform**: Local, single-user web app served on `localhost` (browser).

**Project Type**: Web application — **this feature touches only `frontend/src`** (+ its tests).

**Performance Goals**: The feed MUST NOT reshuffle under an open detail view or an in-flight dismiss/undo;
enrichment status polls at ~5s while pending; the feed reloads **once** when pending reaches 0 (never per
poll tick); dismiss and status changes apply optimistically (no full-feed refetch per triage click).

**Constraints**: Design tokens only for new styling (Principle VIII — no hardcoded colors/sizes); optimistic
updates MUST reconcile with background refetches (no resurrected/orphaned cards or stubs); loopback-only and
offline-capable — **no data leaves the machine** and enrichment stays worker-driven (the UI reflects status,
never performs AI).

**Scale/Scope**: 10 findings across ~9 frontend files — `pages/Offers/OffersPage.tsx` (hotspot, serialized
edits), `components/OfferCard/`, `components/OfferDetail/OfferDetailDrawer.tsx`,
`components/EnrichmentIndicator/`, new `components/OfferSignals/` + `components/OfferCard/DismissedStub.tsx`,
`lib/format.ts`, and `pages/Scans/ScansPage.tsx`; single-user data volumes (hundreds of offers, unpaginated).

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design (below). Constitution v1.1.0.*

| Principle | Verdict | Notes |
|---|---|---|
| I. Layered Architecture | ✅ PASS | Backend untouched. Frontend keeps view logic in components/hooks + the typed API-client layer; no business logic added. |
| II. Strongly-Typed Domain | ✅ PASS | Consumes existing typed DTOs (`OfferDto`, `EnrichmentStatusDto`, `ScanRunSummaryDto`, `OfferDetailDto`); no raw IDs, no new domain types. |
| III. Tracker Reflects Reality | ✅ PASS | No fabricated data. Status changes use existing **legal** transitions (`new→viewed`, `→interested`, `→dismissed`, restore); optimistic updates roll back to server truth on failure. |
| IV. Personal Data Local | ✅ PASS | No external call added; enrichment remains worker-driven; nothing sent off-machine. |
| V. Real DB in Tests | ✅ PASS (N/A) | No persistence change → no new DB tests; backend integration tests unchanged. |
| VI. Green Before Done | ✅ PASS (gate at implement) | `npm run build` + frontend suite green; the 4 affected `OfferCard` tests updated in the same task. |
| VII. UI Visual Verification | ✅ PASS | The load-bearing gate here — each story has an explicit run-and-look acceptance check (light + dark). |
| VIII. One Source of Design Truth | ✅ PASS | New styling references design tokens only; no scattered literals. |
| IX. Data Recoverable | ✅ PASS (trivial) | No migration, no schema, no destructive data operation; append-only rule not engaged. |
| X. Simple by Default | ✅ PASS | Frontend-only; reuses existing endpoints/patterns; only two small new presentational components; no new dependency/abstraction. This plan itself is deliberately lean (see below). |
| XI. Documented Decisions | ✅ PASS | spec + this plan + `docs/ux-review-findings.md` record the decisions; conventional commits, one story each. |

**No violations → Complexity Tracking is empty.**

**Right-sizing note (Principle X)**: the requirements are already specified and adversarially verified in
`docs/ux-review-findings.md`, and `tasks.md` is already grounded. `research.md`, `data-model.md`, and
`contracts/` are therefore intentionally minimal — they **record decisions and the no-backend-change /
no-schema guarantee** rather than derive new design. This is a conscious YAGNI choice, not an omission.

## Project Structure

### Documentation (this feature)

```text
specs/007-triage-loop-ux/
├── plan.md              # This file
├── research.md          # Technical decisions (Phase 0) — lean
├── data-model.md        # "No new data" record + client state touched (Phase 1)
├── quickstart.md        # Run + per-story validation guide (Phase 1)
├── contracts/           # Existing endpoints consumed — no new contract (Phase 1)
│   └── frontend-consumed-endpoints.md
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
├── spec.md              # Feature spec (clarified)
└── tasks.md             # Dependency-ordered tasks (already authored)
```

### Source Code (repository root)

```text
frontend/
├── src/
│   ├── pages/
│   │   ├── Offers/OffersPage.tsx        # hotspot: toolbar state, URL sync, live refresh,
│   │   │   OffersPage.css               #   freshness, optimistic dismiss, drawer wiring, card props
│   │   └── Scans/ScansPage.tsx          # reuse shared outcome-chip + datetime helpers (#5)
│   ├── components/
│   │   ├── OfferCard/
│   │   │   ├── OfferCard.tsx / .css      # deduped skills row, collapsible rationale (#7)
│   │   │   └── DismissedStub.tsx         # NEW — inline undo stub (#6)
│   │   ├── OfferDetail/OfferDetailDrawer.tsx / .css  # footer actions, prev/next, full signals (#8/#9/#10)
│   │   ├── EnrichmentIndicator/EnrichmentIndicator.tsx / .css  # live refresh + rerun error (#1)
│   │   └── OfferSignals/                 # NEW — FitBreakdown + AffinityBreakdown (#10, reused by #7)
│   │       └── OfferSignals.tsx / .css
│   └── lib/format.ts                     # formatRelativeTime + shared outcomeClass (#5)
└── tests/offers/                         # 4 OfferCard tests updated (#7)

backend/                                  # UNCHANGED — no endpoint, DTO, or migration added
```

**Structure Decision**: Web application; **only the `frontend/` project is modified** (plus its tests). The
backend is untouched — every capability this feature surfaces (title/company + work-mode filtering, per-offer
match signals and states, last-scan history, in-app offer detail, per-offer triage/apply/tailor actions)
already exists server-side; the feature exposes and normalizes it in the UI.

## Complexity Tracking

*No constitution violations — table intentionally empty.*

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
