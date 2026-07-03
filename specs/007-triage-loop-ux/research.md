# Research: Triage-Loop UX (007)

**Status**: No open `NEEDS CLARIFICATION`. The one product decision (finding #4 New-queue semantics) was
resolved in `/speckit-clarify` (open = viewed). Everything else was resolved by the pre-spec audit
(`docs/ux-review-findings.md`, findings #1–#10, each re-verified against HEAD `3aaed50`). This file records
the **technical decisions** the plan rests on; there were no unknown technologies to investigate — the stack,
endpoints, and DTOs already exist.

---

### D1 — Live enrichment/feed refresh without a page reload (#1)

- **Decision**: Reuse the existing `lib/polling.ts` `poll()` helper (already used by `TailorCvModal`): re-fetch
  `/api/enrichment/status` after each scan (a `refreshKey` bump) and poll while `pendingTotal > 0`, reloading
  the feed **once** when it reaches 0 (or `lastResultAt` changes). Add a `catch` + inline error to the re-run.
- **Rationale**: Mirrors an established in-repo pattern; zero new dependency; "reload once at pending==0"
  avoids reordering cards mid-triage (the verifier's explicit caveat).
- **Alternatives rejected**: WebSocket/SSE push (no infra; overkill for a single-user local app); reloading the
  feed on every poll tick (reshuffles cards under the user).

### D2 — Persist feed toolbar state (#3, pairs with #2)

- **Decision**: Mirror `view`/`sort`/`source` (+ `q`/`workMode`) into the URL via `useSearchParams`: lazy-init
  state from the URL, sync back with the react-router v7 functional updater in **`{ replace: true }`** mode, an
  allow-listed `readEnumParam` guard, and collapse to `/` at defaults.
- **Rationale**: Standard, small pattern; makes reload/back-forward/bookmark/deep-link all work; `replace` mode
  avoids spamming the history stack; the allow-list prevents a hand-edited `?sort=xyz` from breaking sort.
- **Alternatives rejected**: `localStorage` (not shareable/bookmarkable; the repo uses it only for theme);
  history `push` per change (pollutes back/forward).

### D3 — Optimistic status update + reconciliation (#6, #43-adjacent; underpins #4)

- **Decision**: Replace `handleSetStatus`'s full `await load()` with an in-place `setData` mutation of
  `data.data`; roll back + `setError` on failure; clear transient stubs/timers on `view|sort|source` change and
  on unmount so a background refetch can't resurrect a dismissed card or orphan a stub.
- **Rationale**: Removes the per-triage full-feed refetch (heavy payload) and the reshuffle/double-fire it
  causes; the reconciliation rules directly address the audit's optimistic-race risk.
- **Alternatives rejected**: Keep the full refetch (current behavior — slow, reshuffles, allows duplicate
  requests); a client state library (YAGNI for one page's local state).

### D4 — Dismiss undo affordance (#6)

- **Decision**: An **inline, in-place one-line stub** ("Dismissed — Undo") occupying the card's slot for ~6s,
  committing the removal on timeout; not a global toast.
- **Rationale**: Preserves list position and scroll (the card's slot doesn't collapse abruptly); the app has no
  toast infrastructure, so a stub is the smaller change; keeps `data-offer-id` so the deep-link querySelector
  still resolves.
- **Alternatives rejected**: A bottom-corner toast (new infra; off-position from the acted card).

### D5 — `OfferDetailDrawer` as a decision surface (#8, #9, #10)

- **Decision**: Widen the drawer's `{offerId, onClose}` Props with the callbacks `OfferCard` already takes,
  extract the fetch into a `loadDetail()` re-fetched after each footer action, pass the ordered visible
  `offerIds` + `onNavigate`, recompute the current index every render, clear the body on navigation, and guard
  the shared document-keydown effect so a nested `ApplyModal`/`TailorCvModal` (Escape) and arrow-key navigation
  don't collide. Land **#8 → #10 → #9** (serialize edits to one file).
- **Rationale**: The three findings all edit the same file and share one keydown effect and one OffersPage
  mount; sequencing them avoids merge churn and the Escape/arrow double-fire the audit flagged. `OfferDetailDto.offer`
  is a full `OfferDto`, so badges/breakdown need no extra fetch.
- **Alternatives rejected**: A dedicated `/offers/:id` route for detail (the app's established pattern is a
  modal drawer; a route change is a larger, unrelated refactor).

### D6 — Shared `FitBreakdown` / `AffinityBreakdown` (#10, reconciled by #7)

- **Decision**: Extract the exact fit/affinity JSX (all lifecycle states) from `OfferCard` into a shared
  `components/OfferSignals/`, consumed by both the card and the drawer; then compact the card (#7) against it.
- **Rationale**: Eliminates the drawer-vs-card divergence that finding #10 is about, at the source; a single
  component guarantees identical information in every state (SC-004).
- **Alternatives rejected**: Duplicate the JSX into the drawer (recreates the divergence bug; two places to
  maintain state branches).

### D7 — Open = viewed (#4, clarified 2026-07-03)

- **Decision**: Auto-transition `new → viewed` from the drawer-open / prev-next-navigate handler (at the
  `openDetailId` layer, ref-guarded so a background refetch doesn't re-fire it), plus a frontend "Mark all
  reviewed" action (`Promise.all` over the New view's loaded ids). Optimistically decrement the header `new`
  count.
- **Rationale**: `new→viewed` is already legal server-side (`Offer.ChangeUserStatus` rejects only `→new`), so
  **zero backend change**; matches the dominant triage convention (open = read); `viewed` is a soft, reversible
  state. Wiring at the `openDetailId` layer means #8/#9's drawer paths all inherit it.
- **Alternatives rejected**: A `POST /api/offers/mark-viewed` bulk endpoint (a `Promise.all` suffices for a
  single-user unpaginated feed — YAGNI, and it would add a backend change this feature otherwise avoids).

### D8 — Filters, freshness, and formatting (#2, #5)

- **Decision**: Bind a debounced search box + a work-mode select to the existing `listOffers` `q`/`workMode`
  params; read `listScans()[0]` for freshness and detect an in-flight run (`finishedAt === null`) to resume its
  poll; add `formatRelativeTime` + a shared `outcomeClass` chip mapping in `lib/format.ts` (reused by
  `ScansPage`).
- **Rationale**: The backend already supports `q` (ILike on Title+Company) and `workMode` (case-insensitive
  enum) and returns scan summaries — pure frontend wiring; sharing the helpers removes an existing date-only /
  duplicated-mapping inconsistency.
- **Alternatives rejected**: A new filtered/paginated offers endpoint (unnecessary — the params exist);
  bespoke per-page date formatting (the app already standardizes on locale date-time elsewhere).

---

**No unresolved unknowns.** Proceed to design artifacts (data-model, contracts, quickstart) and implementation
via `tasks.md`.
