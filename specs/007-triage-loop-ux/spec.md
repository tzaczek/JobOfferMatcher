# Feature Specification: Triage-Loop UX — Offers Feed & Offer Detail

**Feature Branch**: `007-triage-loop-ux` (spec directory; no git branch created — repo has no `before_specify` hook)

**Created**: 2026-07-03

**Status**: Draft

**Input**: User description: "Implement the first 10 items of the UX review." — the ten highest-priority
findings (#1–#10) from the frontend UX audit in `docs/ux-review-findings.md`, all concerning the **Offers
feed** and the **offer detail view**: keep the feed current without a manual refresh, expose the built-in
search/work-mode filters, remember the user's feed view, show data freshness, turn the detail view into a
place to decide and act, and make bulk triage faster and reversible.

**Source & traceability**: The requirements below are drawn from `docs/ux-review-findings.md`, a 9-reviewer
audit whose 53 findings were each adversarially verified against the running code; findings #1–#10 were
re-confirmed accurate against the current codebase before this spec was written. Each functional requirement
cites its originating finding (e.g. *finding #3*) so the audit remains the detailed evidence trail. This spec
describes **what and why** for stakeholders; the concrete, file-level implementation steps live in
`tasks.md` alongside it.

## Clarifications

### Session 2026-07-03

- Q: Should opening an offer's detail view auto-mark it reviewed (advance it out of the New queue)? → A: Yes — opening a detail view transitions the offer from `new` to `viewed` (a soft, reversible state; the offer stays in "All" and fully actionable), and this applies when reaching an offer via previous/next navigation; the explicit "Mark all reviewed" action in the New view is also included.

## User Scenarios & Testing *(mandatory)*

<!-- Three independently shippable stories, grouped by the surface they improve and ordered by daily-loop value.
     Each is a standalone MVP slice: any one delivers value on its own. -->

### User Story 1 - A feed that stays current and is quick to filter (Priority: P1)

As a job seeker doing daily triage, I want the offers feed to **reflect reality without a manual page
refresh** and to be **easy to filter and to return to**, so I can trust what I see and find offers in
seconds instead of scrolling.

**Why this priority**: The feed is the primary daily surface. Today it can silently mislead: after new offers
arrive it still claims enrichment is "up to date", it never updates while enrichment is being produced, it
forgets the user's chosen view/sort/source on every navigation, it hides the search and work-mode filters
that already exist behind the scenes, and it shows nothing about when offers were last collected. Fixing this
removes the most frequent friction and the feed's biggest trust gap.

**Independent Test**: Collect new offers and confirm the feed's enrichment status flips from "up to date" to
"pending" and the new offers show as pending — with no manual refresh; produce enrichment and watch the
counts tick down and the offers fill in; set a specific view, sort, source, search term, and work mode, then
reload and navigate away and back, confirming every choice is restored; confirm a "last collected" indicator
reflects the most recent collection.

**Acceptance Scenarios**:

1. **Given** offers were just collected, **When** I look at the feed, **Then** the enrichment status shows the
   true count of offers still awaiting enrichment (not "up to date"), the affected offers are marked pending,
   and I am nudged to run enrichment — without refreshing the page. *(finding #1)*
2. **Given** enrichment is being produced, **When** I keep the feed open, **Then** the pending count decreases
   in near-real-time and, once it reaches zero, the offers refresh in place so summaries and match scores
   appear — without a manual refresh. *(finding #1)*
3. **Given** an enrichment re-run request fails, **When** I trigger it, **Then** I see an error message rather
   than the button appearing to do nothing. *(finding #1)*
4. **Given** the feed, **When** I type part of a company or job title, or choose a work mode (remote / hybrid /
   office), **Then** the feed narrows accordingly and these filters combine with the existing view, source,
   and sort controls. *(finding #2)*
5. **Given** I have set a view, sort, source, and filters, **When** I reload, navigate away and back, use the
   browser's back/forward, or open a saved/shared link to the feed, **Then** the same filtered feed is
   restored; a default feed leaves a clean address, and a direct link to a specific offer still works.
   *(finding #3)*
6. **Given** the feed, **When** it loads, **Then** it shows when offers were last collected (relative time and
   outcome), visually flags a partial/failed collection, and — if a collection is already running — shows its
   live progress and prevents me from starting a duplicate. *(finding #5)*
7. **Given** the New view contains unreviewed offers, **When** I choose "Mark all reviewed", **Then** the New
   queue clears and the "new" count updates accordingly, without dismissing or otherwise re-triaging those
   offers. *(finding #4)*

---

### User Story 2 - The offer detail view as a place to decide and act (Priority: P1)

As a job seeker, when I open an offer to read its full description I want to **understand its match and act on
it right there** — see the complete fit/affinity breakdown, move to the next offer, and mark it applied /
tailor a CV / mark interested or dismiss — without closing the view and hunting for the card again.

**Why this priority**: The detail view is where the read-and-decide actually happens, yet today it is
read-only: it offers a single external link, no actions, no status indication, only bare match scores with no
explanation, and no way to move between offers. Every decision costs a close, a re-find in a long list, and an
extra click — dozens of times per session.

**Independent Test**: Open an offer's detail; confirm it offers interested / dismiss / tailor CV / mark
applied and shows the offer's current disposition; step forward and back through the visible feed with
controls and the keyboard; confirm the fit and affinity signals show their explanations and communicate
pending / unavailable / insufficient-history states consistently with the feed card behind it.

**Acceptance Scenarios**:

1. **Given** an open offer detail, **When** I view it, **Then** it offers the same triage actions available on
   the feed card (mark interested, dismiss, tailor CV, mark applied — and, once applied, open/edit/undo the
   application) and shows the offer's current status, applied, and tailored indicators. *(finding #8)*
2. **Given** I take an action in the detail view, **When** it completes, **Then** the view updates to reflect
   the new state (e.g. the status/applied indicators change) without a full page refresh; and if I open a
   nested dialog (e.g. to record the application) and press Escape, only the dialog closes. *(finding #8)*
3. **Given** an open offer detail, **When** I use the previous/next controls or the left/right arrow keys,
   **Then** I move through the offers in the currently displayed feed order, see my position ("n of N"), see a
   brief loading state instead of the prior offer's content lingering, and cannot move past the first or last
   offer. *(finding #9)*
4. **Given** an open offer detail, **When** I look at its match signals, **Then** I see each signal's score
   with its rationale and its contributing/missing factors (fit) and what it resembles (affinity), and — when
   a signal is still pending, could not be produced, or lacks enough application history — I see a clear
   corresponding message rather than a blank space, matching what the feed card shows. *(finding #10)*
5. **Given** an offer whose status is "new", **When** I open its detail view (directly or via previous/next),
   **Then** it is marked "viewed" and leaves the New queue while remaining available under "All" and fully
   actionable, and it is neither auto-dismissed nor re-triaged; a repeated open or a background refresh does
   not re-fire the transition. *(finding #4)*

---

### User Story 3 - Faster, safer bulk triage (Priority: P2)

As a job seeker triaging dozens of offers in one sitting, I want a **more compact offer card** and an
**undoable dismiss**, so I can scan quickly and never lose a good offer to a misclick.

**Why this priority**: High-volume triage is the core loop. Cards are roughly half a screen tall — the same
skills repeat in up to three places, two rationale paragraphs are always expanded, and a cold-start notice is
repeated on every card — while dismiss is a one-way trip to a separate bin with no confirmation or undo.

**Independent Test**: On an enriched feed, confirm each skill appears once (clearly marked matched vs missing),
secondary rationale is collapsed by default, and the "not enough application history" notice appears once for
the whole feed rather than on every card; dismiss a mid-list card and confirm it collapses in place to a
one-line "Dismissed — Undo" for a few seconds without the rest of the list shifting, that Undo restores it,
and that after the window it leaves the default feed.

**Acceptance Scenarios**:

1. **Given** the feed, **When** I dismiss an offer, **Then** the card collapses in place to a brief inline
   "Dismissed — Undo" (a few seconds) without reordering or reloading the rest of the feed; **When** I click
   Undo in time, the offer is restored; **When** the window passes, the offer leaves the default feed and is
   available under Dismissed. A failed dismiss restores the card and reports the error. *(finding #6)*
2. **Given** an enriched offer card, **When** I read it, **Then** each skill is shown once (marked matched or
   missing), the fit and affinity rationale are collapsed behind an expander, and the "not enough application
   history yet" notice is not repeated per card when a single feed-level notice already explains it — making
   the card shorter without hiding any available information. *(finding #7)*

### Edge Cases

- **Enrichment re-run fails** (backend momentarily unavailable): the user sees an explicit error, never a
  button that silently does nothing.
- **A collection is already in progress** when the feed loads or when the user asks to collect again: the feed
  attaches to and shows the running collection instead of erroring or starting a duplicate.
- **A direct link to a specific offer** arrives while the feed is filtered or not on its default view: the
  linked offer is still surfaced and the user's filters are not silently lost.
- **A background refresh arrives mid-action**: if enrichment completes or a collection finishes while an
  optimistic dismiss/undo is in flight or the detail view is open, a just-dismissed offer is not resurrected,
  no orphaned "undo" remains, and the feed does not reshuffle under an open detail view.
- **Cold start (too few applications)**: the affinity "not enough application history yet" state is shown once
  at the feed level and in the detail view, not repeated on every card.
- **Reading vs. re-triage (New queue)**: opening an offer (directly or via previous/next) marks it "viewed"
  and removes it from the New queue but never auto-dismisses or auto-actions it; a repeated open or a
  background refresh does not re-fire or duplicate the transition.
- **A match signal is pending or could not be produced**: the detail view says so explicitly (never blank),
  consistently with the feed card.
- **Nested dialog over the detail view**: pressing Escape or an arrow key while a nested action dialog is open
  affects only the dialog — the detail view neither closes nor navigates.
- **Ends of the list**: previous/next at the first/last offer is disabled and never wraps unexpectedly or
  errors.
- **Navigating away during an undo window**: the dismissal commits cleanly; no timers, stubs, or partial state
  linger.
- **Themes**: every new affordance stays legible and consistent in both light and dark themes.

## Requirements *(mandatory)*

### Functional Requirements

*Feed freshness & filtering (User Story 1)*

- **FR-001**: The feed's enrichment status MUST update after offers are collected and MUST refresh
  continuously while enrichment is pending, and the feed MUST refresh once when pending enrichment reaches
  zero — all without a manual page reload; a failed re-run MUST surface an error rather than silently doing
  nothing. *(finding #1)*
- **FR-002**: Users MUST be able to filter the feed by a title/company search term and by work mode, combined
  with the existing view, source, and sort controls. *(finding #2)*
- **FR-003**: The feed's view, sort, source, and filter selections MUST persist across page reloads, in-app
  navigation, and browser back/forward, MUST be reproducible from a shared/bookmarked link, MUST leave a clean
  address when at defaults, and MUST NOT disrupt an incoming direct link to a specific offer. *(finding #3)*
- **FR-004**: The feed MUST show when offers were last collected (relative time and outcome), MUST visually
  distinguish a partial or failed collection, and MUST detect a collection already in progress on load —
  showing its live status and preventing a duplicate run. *(finding #5)*

*The offer detail view as a decision surface (User Story 2)*

- **FR-005**: From the offer detail view, users MUST be able to take the same triage actions as on the feed
  card (mark interested, dismiss, tailor CV, mark applied — and, once applied, open/edit/undo the application),
  MUST see the offer's current status/applied/tailored indicators, and the view MUST reflect each action
  immediately, with nested action dialogs handling Escape without closing the detail view. *(finding #8)*
- **FR-006**: Users MUST be able to move to the previous/next offer from within the detail view using controls
  and the keyboard, following the currently displayed feed order, with a clear position indicator, a loading
  state on change (no stale content), and correct no-wrap behavior at the ends of the list. *(finding #9)*
- **FR-007**: The offer detail view MUST present each match signal's full explanation (fit rationale with
  contributing/missing factors; affinity rationale with what it resembles) and MUST communicate every signal
  state (scored, pending, unavailable, insufficient history), consistently with the feed card. *(finding #10)*

*Faster, safer triage (User Story 3)*

- **FR-008**: Dismissing an offer MUST be reversible via an inline undo for a short window and MUST remove the
  card without reordering or reloading the rest of the feed; a failed dismiss MUST restore the card and report
  the error. *(finding #6)*
- **FR-009**: The offer card MUST present each skill only once (marked matched vs missing), MUST keep secondary
  rationale collapsed by default, and MUST NOT repeat the cold-start affinity notice per card when a single
  feed-level notice already covers it — shortening the card without losing information. *(finding #7)*

*Decision pending & cross-cutting*

- **FR-010** *(finding #4)*: Opening an offer's detail view MUST transition that offer from `new` to `viewed`,
  removing it from the New queue — a soft, reversible state in which the offer remains under "All" and fully
  actionable; this applies equally when the offer is reached via previous/next navigation (FR-006). The
  transition MUST NOT auto-dismiss or otherwise re-triage the offer, and a background feed refresh MUST NOT
  re-apply or duplicate it. Users MUST also be able to **explicitly mark all offers in the New view as
  reviewed**.
- **FR-011**: These changes MUST NOT create, alter, or delete any offer, match, application, or CV data beyond
  the status changes a user explicitly triggers (which the product already supports today).
- **FR-012**: All existing feed, detail, enrichment, collection, application, and privacy behaviors outside
  these changes MUST remain unchanged; enrichment MUST stay a separate, user-run step (the feed only reflects
  its status, never performs it), and nothing MUST leave the user's machine as a result of these changes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After offers are collected and enrichment is produced, the feed reflects both — freshness,
  pending counts, and per-offer summaries/scores — with **zero manual page refreshes**.
- **SC-002**: A user can narrow the feed by title/company text and by work mode and **reproduce that exact
  filtered view 100% of the time** after a reload, a back/forward navigation, or by opening a shared link.
- **SC-003**: A user can read an offer's full description and complete a triage decision (mark interested /
  dismiss / tailor CV / mark applied) and move to the next offer **without closing the detail view**.
- **SC-004**: For any given offer, the detail view and the feed card present **identical match information in
  every state** (scored, pending, unavailable, insufficient history).
- **SC-005**: A dismissed offer can be recovered within about **6 seconds** without leaving the current view or
  losing scroll position.
- **SC-006**: The offer card presents each skill once and keeps secondary rationale collapsed by default,
  **measurably reducing card height** with no loss of available information.
- **SC-007**: Opening the detail view and navigating between offers **never shows stale content** from the
  previously viewed offer.
- **SC-008**: All existing feed, detail, enrichment, collection, and application behaviors outside these
  changes remain **unchanged (no regressions)**, and **no data leaves the user's machine** as a result of this
  feature.
- **SC-009**: Opening an offer removes it from the "New" queue, so the New count reflects what the user has
  actually reviewed and no longer grows unbounded across sessions.

## Assumptions

- These are **display and interaction improvements** to the existing single-user, local-first web app; they
  add **no new stored data** and require **no schema change**.
- `docs/ux-review-findings.md` (findings #1–#10) is the detailed requirements source; every functional
  requirement traces to a finding number and was re-verified against the current codebase before this spec.
- The underlying capabilities these stories surface **already exist** in the product — title/company and
  work-mode filtering, per-offer fit and affinity signals with their states, last-collection history, an
  in-app offer detail with the full description, and per-offer triage/apply/tailor actions. This feature
  **exposes and normalizes** them across the feed and detail view rather than adding new domain capability.
- **Enrichment stays worker-driven**: the app reflects enrichment status but never performs AI work itself;
  producing enrichment remains a separate, explicit user action.
- **Finding #4 resolved** (Clarifications 2026-07-03; see FR-010): opening an offer's detail view marks it
  `viewed` (open = reviewed), and an explicit "Mark all reviewed" action is included; this moves #4 into this
  batch's committed scope. `viewed` is the existing soft state used elsewhere (e.g. Restore) — the offer stays
  active and actionable, so an accidental open is low-cost and reversible.
- **Out of scope** (each a separate change): the remaining audit findings #11–#53, including wider detail-view
  polish, all Applications / Settings / Sources / CV / navigation / accessibility items, and the two
  cross-surface trust hazards the audit also flagged — the dead offer cross-link from Applications (#15) and
  the misleading multi-source collection outcome (#30, which requires backend work).
- **Verification** is by running the app and observing each acceptance scenario in both light and dark themes
  (the project's UI-verification principle), with the existing automated suite kept green.
