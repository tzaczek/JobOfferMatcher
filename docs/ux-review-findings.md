# UX Review — Verified Improvement Findings

**Date:** 2026-07-03 · **Scope:** entire frontend (`frontend/src`), plus backend behavior where it drives UX.

**Method:** 9 independently-scoped reviewers audited the code (feed, detail drawer, applications,
settings, sources/scans, CV flows, navigation/consistency, async feedback, accessibility/keyboard)
→ 69 raw findings → 53 after dedup → **each finding adversarially verified** by an agent instructed
to refute it against the actual code (does it already exist? are the code claims accurate? is it a
genuine improvement?). All 53 were confirmed; several impacts were revised downward and the
verifiers’ corrections are recorded per finding. Line numbers reference the code as of commit `3aaed50`.

**Constraints respected:** enrichment stays worker-driven (`/enrich`, `/tailor-cv` — no auto-AI),
single-user local-first app; findings target status clarity, fewer clicks in frequent loops,
error recovery, and consistency — not redesigns.

---

## Recommended order

**Do first — the daily loop and the two trust hazards (4 of 6 are small):**

1. [#1](#f1) Enrichment indicator loads once and never refreshes — stale 'up to date' after scans, no live feedback while /enrich runs
2. [#36](#f36) Flag tailored CVs that are stale after a CV replacement — and stop mislabeling the source CV in the modal
3. [#2](#f2) Expose the already-built title/company search and work-mode filter in the feed toolbar
4. [#3](#f3) Persist view/sort/source in the URL — feed state resets on every reload and navigation
5. [#15](#f15) Fix 'View offer' from Applications: it silently no-ops for dismissed/no-longer-available offers
6. [#30](#f30) Fix the cry-wolf 'Scan incomplete (LayoutChanged)' shown on every multi-source scan

**Then — the triage overhaul (one coherent bundle):** detail-drawer actions ([#8](#f8)), prev/next navigation
([#9](#f9)), fit/affinity breakdown + summary in the drawer, keyboard triage, optimistic status updates,
dismiss undo, "viewed" on drawer open, compact cards.

**Anytime (small, independent):** delete confirms, dirty-guard on drawers, 409 steer-to-Withdrawn choice,
ApplyModal Enter-to-save, shared focus-trap hook, restore reload, settings feedback polish, and the rest of the low/S items.

**Open questions for the product owner:** do you keep multiple CVs uploaded (decides the effective-CV badge value)?
Keyboard or mouse triage (decides the j/k investment)? Is auto-clearing "new" when the detail drawer opens the right
semantics for the New queue (behavior change, not polish)?

---

## Index

| # | Finding | Area | Impact | Effort | Corroborated |
|---|---------|------|--------|--------|--------------|
| [1](#f1) | Enrichment indicator loads once and never refreshes — stale 'up to date' after scans, no live feedback while /enrich runs | Offers feed | high | M | ×4 |
| [2](#f2) | Expose the already-built title/company search and work-mode filter in the feed toolbar | Offers feed | high | S | ×2 |
| [3](#f3) | Persist view/sort/source in the URL — feed state resets on every reload and navigation | Offers feed | high | S | ×2 |
| [4](#f4) | The 'New' queue can only shrink via Interested/Dismiss — 'viewed' is unreachable, so the count grows forever | Offers feed | high | M | ×1 |
| [5](#f5) | Show data freshness: last-scan time/outcome on the feed, and surface scheduler-triggered scans | Offers feed | medium | M | ×1 |
| [6](#f6) | Give Dismiss an inline undo instead of a full-feed round-trip through the Dismissed bin | Offers feed | medium | M | ×1 |
| [7](#f7) | Shrink card height for scanning: skills render up to three times, two rationale paragraphs always expanded, per-card 'insufficient' noise | Offers feed | medium | M | ×1 |
| [8](#f8) | Add offer actions (mark applied, tailor CV, interested/dismiss) to the detail drawer footer | Offer detail drawer | high | M | ×1 |
| [9](#f9) | Add prev/next navigation between offers inside the drawer (buttons + arrow keys) | Offer detail drawer | high | M | ×1 |
| [10](#f10) | Show fit/affinity state and the full breakdown (matched/missing, rationale, resembles) in the drawer | Offer detail drawer | medium | S | ×1 |
| [11](#f11) | Show the AI summary/key skills in the drawer and label skill groups; render niceToHaveSkills (currently shown nowhere in the app) | Offer detail drawer | medium | S | ×1 |
| [12](#f12) | Show the normalized comparable salary in the drawer, not just raw bands | Offer detail drawer | low | S | ×1 |
| [13](#f13) | Render the fetched version history and humanize event types in the History section | Offer detail drawer | low | S | ×1 |
| [14](#f14) | Add a Retry action when the offer detail fails to load | Offer detail drawer | low | S | ×1 |
| [15](#f15) | Fix 'View offer' from Applications: it silently no-ops for dismissed/no-longer-available offers | Applications | high | S | ×3 |
| [16](#f16) | Confirm (or make undoable) permanent deletes of tasks, interviews, and documents | Applications | medium | S | ×3 |
| [17](#f17) | Guard the drawer against losing typed text on overlay click / Escape | Applications | medium | S | ×2 |
| [18](#f18) | Allow moving an application between stages directly from the board | Applications | medium | M | ×1 |
| [19](#f19) | Add a needs-attention strip above the board (upcoming interviews + overdue tasks) | Applications | medium | M | ×1 |
| [20](#f20) | Turn the Unmark 409 steer-to-Withdrawn dead-end into an actionable choice | Applications | medium | S | ×1 |
| [21](#f21) | Expose edit/reschedule for interviews and tasks — the update API already supports it | Applications | medium | M | ×1 |
| [22](#f22) | Use the timeline entry `kind` for visual scanning and filtering | Applications | low | S | ×1 |
| [23](#f23) | Warn that saving fit weights resets every fit score to pending, and nudge to /enrich | Settings | medium | S | ×2 |
| [24](#f24) | Show 'Next run' for the scan schedule so the user can verify their cron before/after saving | Settings | medium | M | ×2 |
| [25](#f25) | After a successful restore, offer/force an app reload — the whole UI is stale | Settings | medium | S | ×1 |
| [26](#f26) | Settings cards vanish silently when their initial load fails — the error is never rendered | Settings | low | S | ×1 |
| [27](#f27) | Stale 'saved' confirmations persist next to edited, unsaved values — clear them on change and show a dirty hint | Settings | low | S | ×1 |
| [28](#f28) | Pipeline-stage reorder/rename failures leave the list showing an order/name the server rejected | Settings | low | S | ×1 |
| [29](#f29) | Enrichment-limits Save disables silently — say which field is invalid | Settings | low | S | ×1 |
| [30](#f30) | Fix the cry-wolf 'Scan incomplete (LayoutChanged)' shown on every multi-source scan | Sources & scans | high | M | ×1 |
| [31](#f31) | Attach the scan banner to an already-running scan instead of erroring, and stop mislabeling mid-scan failures as 'failed to start' | Sources & scans | medium | S | ×2 |
| [32](#f32) | Surface the per-source 'Scan now' action that the API already supports | Sources & scans | medium | S | ×1 |
| [33](#f33) | Make scan history live and time-precise: time-of-day, duration, and rows that stop saying 'running' forever | Sources & scans | medium | S | ×1 |
| [34](#f34) | Link scan results to the offers they actually found | Sources & scans | medium | M | ×1 |
| [35](#f35) | Translate incompleteReason enum tokens into plain-language, actionable messages | Sources & scans | low | S | ×1 |
| [36](#f36) | Flag tailored CVs that are stale after a CV replacement — and stop mislabeling the source CV in the modal | CV & tailored CVs | high | S | ×1 |
| [37](#f37) | Tailored CVs page: live-update pending rows, nudge /tailor-cv, and show the failure reason inline | CV & tailored CVs | medium | S | ×2 |
| [38](#f38) | Add a 'Reset to default prompt' button in the Tailor CV modal | CV & tailored CVs | medium | S | ×1 |
| [39](#f39) | Mark which uploaded CV is the effective one driving the profile, fit scores, and tailoring | CV & tailored CVs | medium | S | ×1 |
| [40](#f40) | Add status badges to the nav: pending/failed enrichment and overdue application tasks | Navigation & consistency | medium | M | ×1 |
| [41](#f41) | Disambiguate 'View offer': same label opens an external site on cards but internal navigation everywhere else | Navigation & consistency | low | S | ×1 |
| [42](#f42) | Set per-route document titles | Navigation & consistency | low | S | ×1 |
| [43](#f43) | Update offer status optimistically instead of a full feed refetch per triage action | Async state & feedback | high | M | ×1 |
| [44](#f44) | Surface per-card mutation errors near the action instead of an off-viewport banner at the page top | Async state & feedback | medium | M | ×2 |
| [45](#f45) | Keep the Applications board mounted during refresh instead of blanking it on every drawer change | Async state & feedback | medium | S | ×1 |
| [46](#f46) | Make poll() tolerate transient fetch failures instead of failing the whole operation | Async state & feedback | low | S | ×1 |
| [47](#f47) | Stop swallowing failures in CV delete and enrichment re-run | Async state & feedback | low | S | ×1 |
| [48](#f48) | Add keyboard triage to the Offers feed (j/k + single-key actions) | Accessibility & keyboard | high | M | ×1 |
| [49](#f49) | Give OfferDetailDrawer/ApplicationDrawer initial focus + Tab trap, and lock body scroll under overlays | Accessibility & keyboard | medium | S | ×2 |
| [50](#f50) | Make ApplyModal a real \<form> so Enter saves 'Mark applied' | Accessibility & keyboard | medium | S | ×1 |
| [51](#f51) | Add a global :focus-visible style for buttons, links and selects | Accessibility & keyboard | low | S | ×1 |
| [52](#f52) | Finish or drop the half-implemented ARIA tabs in ApplicationDrawer | Accessibility & keyboard | low | S | ×1 |
| [53](#f53) | Fix light-mode contrast of the dismissed/rough tokens that carry real data | Accessibility & keyboard | low | S | ×1 |

*Impact is the verifier-revised value. Effort: S ≈ hours, M ≈ a day-ish. Corroborated = how many of the 9
independent reviewers found the same issue.*

---

## Offers feed

<a id="f1"></a>
### 1. Enrichment indicator loads once and never refreshes — stale 'up to date' after scans, no live feedback while /enrich runs

**Impact:** high · **Effort:** M · **Found by:** feed, sources-scans, ia-consistency, async-feedback (×4)

**Today (evidence):** components/EnrichmentIndicator/EnrichmentIndicator.tsx:25-29 fetches /api/enrichment/status exactly once on mount; OffersPage.tsx:298 mounts it with no refresh mechanism, and `handleRunScan` (OffersPage.tsx:179-197) reloads only the offers list after a scan. The `rerun` handler (EnrichmentIndicator.tsx:31-38) also has no catch — a failed re-run gives no feedback. A reusable `poll` helper already exists (lib/polling.ts:31-50) and is used for scan status.

**User pain:** User clicks Run scan → 25 new offers land, every card says 'Summary pending' / 'Fit pending', but the banner right above still claims 'AI enrichment up to date' — the nudge to run /enrich never appears. Conversely, while the worker drains the queue in a terminal, the feed never updates: counts and 'pending' cards stay frozen until a manual browser refresh, so the user can't tell when enrichment is done and results are ready to triage.

**Proposal:** Lift status loading to OffersPage (or pass a refreshKey): re-fetch enrichment status after every scan completion and offer reload; while `pendingTotal > 0`, poll /api/enrichment/status every ~5s with the existing `poll` helper, and when `pendingTotal` hits 0 (or `lastResultAt` changes) trigger `load()` so produced summaries/fits appear without F5. Add a catch + error text to `rerun`.

**Verifier notes:** All cited lines verified: enrichment status is fetched once on mount (EnrichmentIndicator.tsx:25-29), never refreshed after a scan (OffersPage.tsx:191 reloads only offers), and rerun has no catch; since scans eagerly create pending enrichment rows, the "up to date" banner is actively misleading post-scan. The app already poll-while-pending for tailored CVs (TailorCvModal.tsx:84-98), so the proposal also fixes an internal inconsistency; only caveat is to trigger the feed reload once at pending==0/lastResultAt change, not per tick, to avoid reordering cards mid-triage.

<a id="f2"></a>
### 2. Expose the already-built title/company search and work-mode filter in the feed toolbar

**Impact:** high · **Effort:** S · **Found by:** feed, ia-consistency (×2)

**Today (evidence):** frontend/src/api/types.ts:159-168 (OffersQuery has `q` and `workMode`), frontend/src/api/offers.ts:8,11 (both serialized into the request), backend/src/Web/Endpoints/OfferEndpoints.cs:21-45 (endpoint accepts them) and backend OfferReadService.cs:308-316 (ILike on Title+Company, WorkMode equality). Yet the toolbar in pages/Offers/OffersPage.tsx:300-368 renders only the view segmented control, a Source select, and a Sort select — grep confirms no UI anywhere sends `q` or `workMode`. Fully working backend capability with zero frontend surface.

**User pain:** "Was there an offer from Allegro yesterday?" or "show me only remote roles" — today the user must scroll the entire unpaginated feed (all offers render in one column, OffersPage.tsx:398-415) and rely on browser Ctrl+F, which misses text hidden in other views and can't filter to remote-only. Every such lookup is a minute of scrolling instead of two keystrokes.

**Proposal:** Add to `offers-page__controls`: a debounced (~300ms) text input bound to `query.q` ("Search title or company…") and a Work-mode select (office/remote/hybrid) bound to `query.workMode`, both passed through the existing `listOffers` call in `load()` (OffersPage.tsx:132-135). No backend change needed.

**Verifier notes:** Verified end-to-end: OffersQuery.q/workMode exist and are serialized (types.ts:162,165; offers.ts:8,11), the backend filters on them (OfferEndpoints.cs:21-45; OfferReadService.cs:306-315 — WorkMode equality is at 306-310 and ILike at 312-315, a trivial drift from the cited 308-316), and no frontend UI anywhere sends either (sole listOffers call at OffersPage.tsx:132-135; the "Search" hits elsewhere are source scan criteria, a different capability). The proposal is S-effort and implementable as stated: the WorkMode enum (Office/Remote/Hybrid) is parsed ignoreCase, so lowercase select values work with no backend change.

<a id="f3"></a>
### 3. Persist view/sort/source in the URL — feed state resets on every reload and navigation

**Impact:** high · **Effort:** S · **Found by:** feed, ia-consistency (×2)

**Today (evidence):** pages/Offers/OffersPage.tsx:60-62 — `view`, `sort`, `source` are plain useState defaults ('all', 'rank', ''). The page already uses useSearchParams (line 79) but only for the one-shot `offerId` deep link (79-93). Grep shows localStorage is used only for the theme (theme/theme.ts:25,56). Nothing restores feed state.

**User pain:** The daily loop is: open Offers, switch to New, sort by Fit, filter to one source — then visit Scans/Settings/Applications or hit F5, and everything snaps back to All/Best match/All sources. The deep link from Tailored CVs (which lands with `?offerId=`) also arrives on the default view, so the highlighted card may not even be in the current filter. The user re-applies the same three controls many times a day.

**Proposal:** Mirror `view`, `sort`, and `source` (plus the new `q`/`workMode` if added) into searchParams: initialize state from the URL, write changes back with `setSearchParams(..., { replace: true })`. Bonus: back/forward navigation restores state and specific feed views become bookmarkable.

**Verifier notes:** All claims verified: view/sort/source are plain useState (OffersPage.tsx:60-62), reset on F5 and on every route change (App.tsx Routes unmounts the page), and no persistence exists anywhere (localStorage only for theme; useSearchParams only for the one-shot offerId). Pain is understated if anything — four places in the app deep-link to /?offerId= and land on the default view, where a dismissed/closed offer silently fails to highlight; the fix is a small standard pattern that also makes back/forward and bookmarks work.

<a id="f4"></a>
### 4. The 'New' queue can only shrink via Interested/Dismiss — 'viewed' is unreachable, so the count grows forever

**Impact:** high · **Effort:** M · **Found by:** feed (×1)

**Today (evidence):** The only place the UI ever sets status 'viewed' is the Restore button on dismissed offers (components/OfferCard/OfferCard.tsx:335-344). Opening the detail drawer changes nothing (components/OfferDetail/OfferDetailDrawer.tsx has no status call), and backend grep shows `Viewed` exists only as a filter value (OfferReadService.cs:292) — there is no auto-transition. The header count 'X new' (OffersPage.tsx:254) and the New view (viewQuery, OffersPage.tsx:41-42) therefore only shrink when the user explicitly acts on every single offer.

**User pain:** Daily triage: the user reads 30 new offers, finds 3 interesting, dismisses 5 — the other 22 stay 'new' forever. Tomorrow's New tab mixes yesterday's leftovers with today's genuinely new arrivals, and '· 57 new' in the subtitle stops meaning anything. The only workaround is dismissing offers you're merely neutral on, which pollutes the Dismissed bin.

**Proposal:** Two parts: (a) when the detail drawer opens for an offer with userStatus==='new', fire the existing `setOfferStatus(id,'viewed')` (api/offers.ts:22-24 — zero backend change) and refresh; (b) add a 'Mark all reviewed' button visible in the New view — either Promise.all over the loaded ids, or (cleaner, small obvious API addition) a `POST /api/offers/mark-viewed` bulk endpoint.

**Verifier notes:** All citations verified: 'viewed' is only settable via Restore on dismissed cards (OfferCard.tsx:335-344), the detail drawer never mutates status, and the backend has no auto-transition (Viewed appears only as enum/filter; Offer.ChangeUserStatus rejects only →'new', so new→viewed works today with zero backend change). One correction: the New queue does not literally grow forever — offers drop out when the source delists them (availability:'available' filter, OffersPage.tsx:42) — but the substance stands: reading an offer never clears 'new', so the count and New tab degrade into a stale mix unless the user Interested/Dismisses every single offer.

<a id="f5"></a>
### 5. Show data freshness: last-scan time/outcome on the feed, and surface scheduler-triggered scans

**Impact:** medium · **Effort:** M · **Found by:** feed (×1)

**Today (evidence):** OffersPage.tsx:65-67 — `scanStatus` starts null and is only ever set inside `handleRunScan` (179-197), so ScanBanner (ScanBanner.tsx:19 returns null when !scanning && !status) reflects only scans started from this page in this session. A scheduled scan running right now, or one that failed overnight, is invisible; `listScans()` (api/scans.ts:8-10, returns startedAt/finishedAt/outcome/counts) already exists but the feed never calls it. Nothing on the page says when offers were last collected.

**User pain:** Morning open: did the overnight scan run? Did it go Partial (which, per project memory, multi-source scans regularly do)? The feed looks identical either way, so the user either navigates to the Scans page to check, or wastes minutes re-running a full multi-source scan 'just in case'. If a scheduled scan is mid-flight, 'Run scan' is enabled and can double-run.

**Proposal:** On mount, fetch `listScans()` and render 'Last scan: 2h ago — complete (12 new)' next to the Run scan button, tinted warn on partial/incomplete outcomes; if the latest run has `finishedAt === null`, show the running banner, disable Run scan, and resume polling that run with the existing `poll`/`getScanStatus` machinery.

**Verifier notes:** All cited code behavior verified: scan status is session/page-local (OffersPage.tsx:65-67, 179-197; ScanBanner.tsx:19), listScans (api/scans.ts:8-10) returns startedAt/finishedAt/outcome/counts but is only used by ScansPage, and the feed shows no freshness info; the Settings page's bare "Last scheduled run" timestamp and the Scans history page are the navigation-cost workarounds, not the proposed capability. One correction: a true double-run cannot happen — the backend ScanConcurrencyGuard (ScanOrchestrator.cs:36-50) returns 409 ScanInProgress, so clicking Run scan during a scheduled scan yields a confusing error banner instead of a duplicate scan, which actually strengthens the case for detecting and resuming an in-flight run.

<a id="f6"></a>
### 6. Give Dismiss an inline undo instead of a full-feed round-trip through the Dismissed bin

**Impact:** medium · **Effort:** M · **Found by:** feed (×1)

**Today (evidence):** OfferCard.tsx:354-361 — Dismiss immediately calls onSetStatus → handleSetStatus (OffersPage.tsx:199-206) which POSTs and refetches the whole feed; the card vanishes from the default view (viewQuery line 50-51 uses status 'active'). The only recovery path is switching to the Dismissed view and clicking Restore (OfferCard.tsx:335-344). No confirmation, no undo, no toast.

**User pain:** Rapid triage means clicking Dismiss repeatedly down the list; one misclick on the wrong card and recovery costs: switch to Dismissed, visually hunt the offer in a bin sorted by rank (not by dismissal recency), Restore, switch back, and re-find your scroll position. Enough friction that a misclick can silently lose a good offer.

**Proposal:** After a dismiss, keep the card's slot as a one-line stub ('Dismissed — Undo') for ~6s (or a toast) whose Undo calls `setOfferStatus(id,'viewed')`; apply the removal optimistically instead of the full `load()` refetch so the rest of the list doesn't reshuffle mid-triage.

**Verifier notes:** All cited lines verified: Dismiss (OfferCard.tsx:354-360) fires setOfferStatus + full load() refetch (OffersPage.tsx:199-206) with no confirm/undo/toast anywhere in the frontend, and recovery requires the Dismissed-view round-trip in a bin sorted by rank. Minor correction: scroll is NOT lost at dismiss time itself (the feed stays mounted through refetch, OffersPage.tsx:395-397) — the scroll/context loss occurs during the recovery view-switch, which the proposal still fixes.

<a id="f7"></a>
### 7. Shrink card height for scanning: skills render up to three times, two rationale paragraphs always expanded, per-card 'insufficient' noise

**Impact:** medium · **Effort:** M · **Found by:** feed (×1)

**Today (evidence):** OfferCard.tsx renders three overlapping chip rows per card — AI keySkills (138-146), fit matched + missing (195-212), and source requiredSkills (265-273) — so the same skill (e.g. 'Python') can appear three times; the full fit rationale (192-194) and affinity rationale (235-237) paragraphs are always expanded; and during cold start every card repeats 'Affinity — not enough application history yet' (251-258) even though OffersPage.tsx:370-375 already shows the global 'Apply to at least 3 offers… (0/3)' hint. The feed is a single column with no pagination (OffersPage.tsx:398-415; OfferEndpoints.cs:21-45 has no limit), so every enriched card is ~half a viewport tall.

**User pain:** Comparing offer #3 against offer #12 means scrolling through nine half-screen cards of largely duplicated chips and prose; the two numbers that actually drive triage (fit, affinity) sit mid-card below the summary and salary. During cold start, N identical italic affinity lines add pure noise under a hint that already explains the situation once.

**Proposal:** Merge the chip rows into one deduped skills row where matched skills get the green chip style and missing ones the red 'missing' style (all data already on OfferDto); collapse the fit/affinity rationale paragraphs behind a per-card expander or defer them to the Details drawer (which already shows both signals, OfferDetailDrawer.tsx:87-98); hide the per-card insufficient line whenever `meta.hasAffinityBasis === false` since the page-level hint covers it.

**Verifier notes:** All citations verified: OfferCard.tsx renders three visually-identical chip rows (138-146, 195-212, 265-273), two always-expanded rationale paragraphs (192-194, 235-237), and an unconditional per-card insufficient line (251-258) duplicating the page-level hint (OffersPage.tsx:370-375), on an unpaginated single-column feed (OffersPage.tsx:395-416; OfferEndpoints.cs:21-45 has no limit param). One nuance: OfferDetailDrawer.tsx:87-98 shows only fit/affinity scores, not rationales, so the defer-to-drawer variant requires adding rationale rendering there — a small addition already within the stated M effort.

---

## Offer detail drawer

<a id="f8"></a>
### 8. Add offer actions (mark applied, tailor CV, interested/dismiss) to the detail drawer footer

**Impact:** high · **Effort:** M · **Found by:** detail (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/components/OfferDetail/OfferDetailDrawer.tsx:142-152 — the footer contains exactly one action, the external 'View original ↗' link. All decision actions live only on the card: OfferCard.tsx:308-402 (View offer, Details, Tailor CV, Interested, Dismiss, Mark applied, Application, Edit application, Unmark). OffersPage.tsx:418-420 mounts the drawer with only { offerId, onClose } even though every handler already exists in that component (handleSetStatus/handleMarkApplied/handleClearApplied at OffersPage.tsx:199-227, setOpenApplicationId at 410). The drawer also shows none of the card's state badges (Applied/status/Tailored CV, OfferCard.tsx:100-122), so inside the drawer you can't even tell you already applied.

**User pain:** The drawer is the decision surface — the user opens it precisely to read the full description and decide. Today the flow is: open detail → read → decide 'yes, applying' → close the drawer → visually relocate the card in a long feed → click 'Mark applied' (or Dismiss/Tailor CV). Every single triage decision costs a close + re-find + extra click, dozens of times per scan session.

**Proposal:** Extend OfferDetailDrawer props with the same optional callbacks OfferCard takes (onSetStatus, onMarkApplied/onClearApplied, onOpenApplication, tailoredState/onTailoredChanged) and pass them from OffersPage.tsx:418-420. Render Interested/Dismiss/Mark applied/Tailor CV buttons in the footer next to 'View original' (reusing ApplyModal/TailorCvModal exactly as OfferCard does at lines 405-424), and mirror the applied/status badges in the drawer header.

**Verifier notes:** All cited lines verified: the drawer (OfferDetailDrawer.tsx:142-152, mounted only at OffersPage.tsx:419 with just offerId/onClose) is a full-viewport modal with zero decision actions or applied/status badges, while every handler already exists in OffersPage (199-227, 410) — so each read-and-decide costs close + re-find + click on the primary triage surface. Minor corrections: OfferDetailDto.offer is a full OfferDto, so applied/userStatus badges need no new fetch (proposal is cheaper than stated), and note ApplyModal's Escape handler (ApplyModal.tsx:64-66) doesn't preventDefault, so nesting it over the drawer needs Escape-conflict handling (consistent with the M effort).

<a id="f9"></a>
### 9. Add prev/next navigation between offers inside the drawer (buttons + arrow keys)

**Impact:** high · **Effort:** M · **Found by:** detail (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/components/OfferDetail/OfferDetailDrawer.tsx:10-13 — Props are only { offerId, onClose }; there is no way to move to another offer without closing. OffersPage.tsx:73 holds openDetailId and OffersPage.tsx:399 already has the sorted visible feed (data.data), but passes only the single id (418-420). The drawer already has a document-level keydown effect (lines 41-47, Escape only) and already refetches when offerId changes (effect keyed on [offerId], lines 26-34).

**User pain:** Daily triage of a fresh scan means reading 20-50 descriptions in sequence. Each next offer costs: Escape → scroll the feed → find the next card → click its title. With sort by 'Best match' the user reads them in feed order anyway, so this is pure mechanical overhead repeated per offer.

**Proposal:** Pass onPrev/onNext callbacks (or the ordered id list + current index) from OffersPage; render ‹/› buttons with a 'n of N' counter in the drawer header and handle ArrowLeft/ArrowRight in the existing keydown effect. Since the fetch effect is already keyed on offerId, navigation is just setOpenDetailId(nextId) — plus add setDetail(null) at the top of the effect so the previous offer's body doesn't linger while the next one loads.

**Verifier notes:** All cited lines check out (Props {offerId,onClose} only, Escape-only keydown at 41-47, fetch effect keyed on [offerId] without a detail reset, single-id pass-through at OffersPage 418-420), and no prev/next or arrow-key navigation exists anywhere in the frontend. Sequential triage in the primary daily surface currently forces close/scroll/find/click per offer while the modal hides the feed, so this is a high-value, standard pattern; one refinement — recompute the current index from data.data by id on each render since the feed can refetch while the drawer is open.

<a id="f10"></a>
### 10. Show fit/affinity state and the full breakdown (matched/missing, rationale, resembles) in the drawer

**Impact:** medium (finder said high; revised by verifier) · **Effort:** S · **Found by:** detail (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/components/OfferDetail/OfferDetailDrawer.tsx:87-98 — the signals row renders ONLY when state === 'produced' && score != null, and then only the bare 'Fit 82/100' / 'Affinity 74/100' numbers. matched/missing/rationale (FitDto, types.ts:29-36) and resembles/rationale (AffinityDto, types.ts:49-54) are never rendered in the drawer, and pending/failed/insufficient states render nothing at all. The card, by contrast, shows every state and the full breakdown (OfferCard.tsx:180-263, including the 'not enough application history yet' cold-start message at 251-258).

**User pain:** Reading the full requirements list in the drawer is exactly when the user wants 'which of these do I match / miss?' — but the rationale and skill diff are only on the card hidden behind the overlay. Worse, for a pending or failed enrichment the drawer shows nothing where the score would be, so it is indistinguishable from 'this offer has no fit signal'; the user gets no cue that running /enrich would produce it.

**Proposal:** In the signals block, render all states: 'Fit pending — run /enrich' / 'Fit unavailable' (mirroring enrichmentStatusClass usage from OfferCard.tsx:215-219) and 'Affinity — not enough application history yet' for insufficient. Under a produced score, render the rationale paragraph plus matched (chip--skill) and missing (chip--missing) chips and the affinity resembles list — the exact markup already exists in OfferCard.tsx:191-247 and can be extracted into a shared FitBreakdown/AffinityBreakdown component.

**Verifier notes:** All citations are exact: OfferDetailDrawer.tsx:87-98 renders only bare produced scores and silently renders nothing for pending/failed/insufficient, while OfferCard.tsx:180-263 handles every state plus the full rationale/matched/missing/resembles breakdown, so the fix is genuine consistency + status clarity with S effort. Impact revised from high to medium because the drawer is always opened from the card directly behind the overlay, which already shows the complete breakdown and all states one close-click away.

<a id="f11"></a>
### 11. Show the AI summary/key skills in the drawer and label skill groups; render niceToHaveSkills (currently shown nowhere in the app)

**Impact:** medium · **Effort:** S · **Found by:** detail (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/components/OfferDetail/OfferDetailDrawer.tsx:108-116 renders only offer.requiredSkills as an unlabelled chip row; offer.summary and offer.keySkills (types.ts:69-74) are not rendered in the drawer at all (the card shows them, OfferCard.tsx:134-153). niceToHaveSkills exists on OfferDto (types.ts:66) but a grep across frontend/src shows it is rendered by no component whatsoever.

**User pain:** Long descriptions (the drawer's main content) have no TL;DR — the AI summary the user already paid a worker run for is only on the card underneath the overlay. And a candidate deciding whether to apply cares about the required-vs-nice-to-have split ('is Kubernetes required or a bonus?'); that data is fetched on every offer and silently discarded, forcing a jump to the original posting to check.

**Proposal:** In the drawer, render the AI summary paragraph (with the existing 'Summary pending/unavailable' states from OfferCard.tsx:148-152) above the description, and split the chips section into two labelled groups: 'Required' (requiredSkills) and 'Nice to have' (niceToHaveSkills), reusing chip--skill styling.

**Verifier notes:** All cited lines verified exactly: the drawer (a modal overlay that occludes the card's summary) renders only unlabelled requiredSkills chips at OfferDetailDrawer.tsx:108-116, and niceToHaveSkills (types.ts:66) is rendered by no frontend component (only merged indistinguishably into the Tailor CV modal's allOfferSkills server-side). One correction: nice-to-have skills are populated only by the justjoin.it mapper (theprotocol/NoFluffJobs hardcode []), so the labelled split benefits a subset of offers — impact stays medium, effort S, frontend-only.

<a id="f12"></a>
### 12. Show the normalized comparable salary in the drawer, not just raw bands

**Impact:** low · **Effort:** S · **Found by:** detail (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/components/OfferDetail/OfferDetailDrawer.tsx:100-106 lists only raw salaryBands via formatSalaryBand. The normalized figure ('≈ 24,000 PLN/mo (est.)' + quality chip) exists on the DTO (normalizedSalary, types.ts:68) and is rendered on the card (OfferCard.tsx:167-177) but is omitted from the drawer.

**User pain:** Polish job boards mix hourly B2B, daily rates, and monthly UoP in different currencies. While comparing an offer in the drawer against others just read, the user loses the one number that makes bands comparable and must close the drawer to peek at the card underneath.

**Proposal:** Append the same normalized block used in OfferCard.tsx:167-177 (rounded comparableMonthly + qualityChipClass chip) under the bands list in the drawer; extract it into a shared snippet if desired.

**Verifier notes:** All cited lines check out: the drawer (OfferDetailDrawer.tsx:100-106) renders only raw bands while OfferCard.tsx:167-177 renders the normalized figure, and the detail endpoint already ships normalizedSalary (OfferDetailDto.offer is OfferDto, populated via OfferReadService.ToListItem), making this a small frontend-only consistency fix; the deep-linkable, page-covering drawer means the user genuinely loses the comparable number, though impact is correctly rated low.

<a id="f13"></a>
### 13. Render the fetched version history and humanize event types in the History section

**Impact:** low · **Effort:** S · **Found by:** detail (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/api/types.ts:152-157 — OfferDetailDto includes versions: OfferVersionDto[] (createdAt + changeTier), fetched on every drawer open, but the drawer destructures and renders only events (OfferDetailDrawer.tsx:129-140); grep confirms 'versions'/'changeTier' appear nowhere in any component. Events render the raw enum string {e.type} (line 135) even though titleCase is already imported (line 7).

**User pain:** An offer flagged 'Updated' in the feed (OfferCard.tsx:101) gives no answer to 'what changed — did the salary move?'. The change-tier data answering that is already delivered to the browser and thrown away; meanwhile the History list shows machine-cased event names.

**Proposal:** Merge versions into the existing History \<details>: one line per version with formatDate(createdAt) + a titleCase(changeTier) chip, and pass event types through titleCase (or a small label map) so 'History' reads like a changelog instead of raw enum values.

**Verifier notes:** All citations verified: OfferDetailDto.versions (types.ts:155) is fetched on every drawer open and never rendered (grep confirms no component uses versions/changeTier), and OfferDetailDrawer.tsx:135 shows raw PascalCase enum names (backend serializes e.Type.ToString(), e.g. "BecameUnavailable"). Two corrections: the existing titleCase only uppercases the first char so it is a no-op on PascalCase — the label-map variant of the proposal is the real fix; and ChangeTier is only Major/Minor (versions exist only for Major-tier changes), so it tells when significant content changed, not specifically whether salary moved — the pain statement slightly overstates that, hence impact stays low.

<a id="f14"></a>
### 14. Add a Retry action when the offer detail fails to load

**Impact:** low · **Effort:** S · **Found by:** detail (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/components/OfferDetail/OfferDetailDrawer.tsx:26-34 fetches once per offerId; on failure lines 59-65 replace 'Loading…' with the error text and offer only a Close button — there is no way to re-attempt the fetch, and the error state is never cleared.

**User pain:** The backend is a locally-run dotnet process the user frequently restarts (per the run-app memory). If the drawer is opened during a restart, the only recovery is Close → find the card again → reopen — for a request that would succeed one second later.

**Proposal:** Add a 'Retry' button beside Close in the offer-detail__loading block that clears error and re-invokes the fetch (extract the effect body into a loadDetail() callback so both the effect and the button share it).

**Verifier notes:** Evidence checks out exactly (OfferDetailDrawer.tsx:26-34 single fetch, :59-65 error text + Close only, error never cleared), and no retry affordance exists elsewhere — the drawer only refetches via full unmount/remount (OffersPage.tsx:418-419). Minor correction: recovery is ~2 clicks since the card typically stays in view after closing, so the pain is mild — impact low is correct; the fix also aligns the drawer with the app's existing keep-modal-open-for-retry pattern (ApplyModal, RestoreConfirmModal).

---

## Applications

<a id="f15"></a>
### 15. Fix 'View offer' from Applications: it silently no-ops for dismissed/no-longer-available offers

**Impact:** high · **Effort:** S · **Found by:** applications, cv, ia-consistency (×3)

**Today (evidence):** Both the board card link (ApplicationsPage.tsx:175-177) and the drawer's View offer button (ApplicationDrawer.tsx:71-74) navigate to /?offerId=... . The Offers deep-link handler (frontend/src/pages/Offers/OffersPage.tsx:80-93) looks the card up in the DOM of the CURRENT feed view — default view 'all' maps to { status:'active', availability:'available' } (OffersPage.tsx:39-52). If the card isn't rendered it just deletes the param (lines 90-92) with no fallback. Applied offers are exactly the ones likely to be dismissed or no_longer_available (the code's own comment at OffersPage.tsx:44 says 'a role you applied to may have closed'), so the link lands on the feed with no highlight, no card, no message. The self-fetching OfferDetailDrawer (frontend/src/components/OfferDetail/OfferDetailDrawer.tsx:20-34) already renders any offer by id, independent of the feed.

**User pain:** Prepping for tomorrow's interview, the user opens the application on the board and clicks 'View offer' to re-read the job description — and just gets dumped on the Offers feed scrolled to the top with nothing highlighted, because the offer went unavailable weeks ago. The cross-link breaks precisely for the offers the Applications board is about.

**Proposal:** In the OffersPage deep-link effect, when document.querySelector finds no card for the offerId, fall back to opening the OfferDetailDrawer for that id (setOpenDetailId(offerId)) instead of silently dropping the param. Even better for this flow: open the OfferDetailDrawer directly from the ApplicationDrawer/board (render it on the Applications page too — it only needs offerId), so 'read the offer body' never requires leaving the board.

**Verifier notes:** All citations verified: the OffersPage deep-link effect (OffersPage.tsx:80-93) silently drops ?offerId when the card isn't in the default 'all' view ({status:'active', availability:'available'}, lines 49-52), which excludes exactly the dismissed/closed offers applied offers tend to become (the code admits this at line 44); OfferDetailDrawer is feed-independent and mounted only on OffersPage, and the backend detail endpoint serves any offer by id, so the S-effort fallback works with no backend change. Impact is genuinely high because for a closed posting the external link is dead too, making the app's captured body — reachable only via this drawer — the sole remaining copy of the job text during interview prep.

<a id="f16"></a>
### 16. Confirm (or make undoable) permanent deletes of tasks, interviews, and documents

**Impact:** medium · **Effort:** S · **Found by:** applications, cv, ia-consistency (×3)

**Today (evidence):** Deleting the whole application requires window.confirm and the footer warns 'Deleting is permanent — recoverable only from a backup' (ApplicationDrawer.tsx:300-320). But the per-item ✕ buttons delete instantly with no confirmation: tasks (ApplicationDrawer.tsx:537-545 → onDelete at 270), documents (605-613 → deleteDocument at 278 — this removes the uploaded file itself), and interviews (708-716 → 288, taking recorded outcome/notes with them). The ✕ sits 0.4rem from the frequently-used Download button in the doc row (ApplicationDrawer.css:328-333).

**User pain:** One mis-click on the small ✕ next to 'Download' permanently destroys the cover letter the user attached, or erases an interview record including its outcome — with no undo, mid job hunt when that history is exactly what they're tracking. The app is inconsistent: the big delete is guarded, the equally-irreversible small ones are not.

**Proposal:** Wrap the three onDelete handlers in the same window.confirm pattern already used for the application ('Delete this document? This cannot be undone.'). Cheaper alternative for tasks/interviews if confirm fatigue is a concern: a two-click affordance (✕ turns into a 'Confirm?' button for 3 seconds).

**Verifier notes:** All cited lines verified: the application-level delete is confirm-guarded (ApplicationDrawer.tsx:308-316) while the task/document/interview ✕ buttons (537-545, 605-613, 708-716) delete instantly with no confirm or undo, and the backend RemoveDocumentAsync (ApplicationTrackingService.cs:369) physically deletes the uploaded file, making a mis-click on the ✕ 0.4rem from Download (ApplicationDrawer.css:328-333) unrecoverable without a recent on-demand backup. Reusing the existing window.confirm pattern is a small, consistent error-recoverability fix; impact medium stands.

<a id="f17"></a>
### 17. Guard the drawer against losing typed text on overlay click / Escape

**Impact:** medium · **Effort:** S · **Found by:** applications, a11y-keyboard (×2)

**Today (evidence):** The drawer closes on any overlay mousedown (ApplicationDrawer.tsx:136-141) and on Escape (98-104), gated only on `busy`. All form drafts live in local component state — a note body (NotesPanel, line 401), a task title (461), an interview (641-643), a communication summary (777-780) — and are discarded unconditionally on close. Nothing tracks dirtiness.

**User pain:** The user types a three-paragraph debrief after an interview into the Notes textarea, then clicks slightly outside the 940px dialog (or reflexively hits Escape) — the drawer closes and the entire note is gone with no confirmation and no recovery. For a surface whose main job is capturing free-text history, this is a data-loss trap.

**Proposal:** Lift a simple `dirty` flag (or a ref the panels set via callback whenever any draft field is non-empty) into ApplicationDrawer; when dirty, overlay-click and Escape trigger window.confirm('Discard the unsaved note/task?') before onClose. The explicit ✕ button can share the same guard. Apply the same one-liner to ApplyModal's note field.

**Verifier notes:** All cited lines verified: the drawer closes on overlay mousedown (ApplicationDrawer.tsx:136-141) and Escape (98-104) gated only on busy, and every panel's draft (note 401, task 461, interview 641-643, communication 777-780) is local state discarded on unmount, with no dirty tracking anywhere in the frontend (the only window.confirm at line 310 guards permanent delete); ApplyModal.tsx:99/62-68 has the same unguarded close paths around its note field. A dirty-aware confirm before close is a real data-loss-prevention win for a free-text capture surface, small in scope, and consistent with the app's existing destructive-confirm pattern — one minor nit: Escape is also gated on e.defaultPrevented, which does not weaken the claim.

<a id="f18"></a>
### 18. Allow moving an application between stages directly from the board

**Impact:** medium (finder said high; revised by verifier) · **Effort:** M · **Found by:** applications (×1)

**Today (evidence):** The only stage-move control in the app is the \<select> inside the drawer's lifecycle bar (frontend/src/components/ApplicationDrawer/ApplicationDrawer.tsx:188-202, calling moveStage from frontend/src/api/applications.ts:54-56). The board card (frontend/src/pages/Applications/ApplicationsPage.tsx:138-180) renders title/company/badges and a single onOpen handler — no stage control, no drag-and-drop. So moving a card = click card, wait for getApplication (ApplicationDrawer.tsx:80-91) to load the full detail (timeline + 5 child collections), change the select, close the drawer, and the whole board reloads via onChanged → load() (ApplicationsPage.tsx:131).

**User pain:** Stage movement is the single most frequent board action ('recruiter replied → move to Interviewing', 'sent take-home → move to Task'). Today each move costs 4 interactions plus a full detail fetch, and after a productive email-checking session with 5 replies the user does this dance 5 times. A kanban board whose cards can't move is missing its core ergonomic.

**Proposal:** Add a lightweight per-card stage mover on BoardCard: either a compact \<select> of stages (the stages list is already loaded on the page, ApplicationsPage.tsx:34) or hover-revealed left/right arrows that call moveStage(offerId, stageId) directly and then reload the board. Skip the drawer entirely. Optimistically move the card client-side before the reload so the board doesn't flicker.

**Verifier notes:** All cited lines verified: moveStage is reachable only via the drawer's select (ApplicationDrawer.tsx:193), BoardCard (ApplicationsPage.tsx:138-180) has no stage control or drag-and-drop anywhere in the frontend, and each move costs a full-detail fetch plus a whole-board reload (onChanged → load() at ApplicationsPage.tsx:131); the stages list is already on the page (line 34) and the API exists, so the fix is frontend-only. Impact revised high → medium: stage moves are the board's core action but happen in bursts a few times per week, and Applications is the secondary surface behind the Offers feed.

<a id="f19"></a>
### 19. Add a needs-attention strip above the board (upcoming interviews + overdue tasks)

**Impact:** medium (finder said high; revised by verifier) · **Effort:** M · **Found by:** applications (×1)

**Today (evidence):** The page header shows only totals: '${totalActive} active · ${board.closed.length} closed' (ApplicationsPage.tsx:58-62). Per-card badges for overdue/outstanding tasks and next interview exist (ApplicationsPage.tsx:156-172) but they live inside cards inside columns, and the board scrolls horizontally (grid-auto-flow: column + overflow-x: auto, frontend/src/pages/Applications/ApplicationsPage.css:5-13), so badges in later columns are literally off-screen. All the needed data is already in the board payload: outstandingTaskCount, overdueTaskCount, nextInterviewAt per card (frontend/src/api/types.ts:424-435).

**User pain:** The user opens Applications each morning to answer 'what needs action today?'. With 5+ stage columns they must horizontally scroll every column and visually scan every card for red badges; an interview badge on a card in the last column is invisible on load, so a scheduled phone screen can genuinely be missed.

**Proposal:** Compute client-side from the existing board DTO and render a compact strip under the header: 'Next interview: {title} @ {company} — {date}' (soonest nextInterviewAt across all cards), '{n} overdue tasks / {m} open tasks', each item clickable → setOpenOfferId(card.offerId) opening the drawer on the relevant tab. Pure frontend, zero API change. (A 'stale — no activity for N days' signal would additionally need a lastActivityAt field on ApplicationCardDto — a one-line backend DTO addition worth making.)

**Verifier notes:** All citations check out (header totals ApplicationsPage.tsx:58-62, per-card badges :156-172, overflow-x board css :5-13, DTO fields types.ts:424-435) and no aggregated attention surface exists anywhere in the frontend, so the strip is a genuine, cheap, pure-frontend win. Two corrections: the seeded default is 4 stage columns (DatabaseSeeder.cs:31), not 5+, so the off-screen-badge scenario only occurs with user-added stages or narrow windows — hence impact revised to medium — and "open drawer on relevant tab" needs a small initialTab prop added to ApplicationDrawer (tab state is internal at ApplicationDrawer.tsx:64).

<a id="f20"></a>
### 20. Turn the Unmark 409 steer-to-Withdrawn dead-end into an actionable choice

**Impact:** medium · **Effort:** S · **Found by:** applications (×1)

**Today (evidence):** OfferCard's 'Unmark' button (frontend/src/components/OfferCard/OfferCard.tsx:383-391) calls handleClearApplied, which catches the backend's 409 'steer to closing' response and only does setError(message) (OffersPage.tsx:219-227). That error renders in the page-level state-block at the top of the feed (OffersPage.tsx:383-387) — potentially off-screen when the user clicked a card mid-scroll — and offers no way to act on the guidance. The API to comply is one call away: closeApplication(offerId,'withdrawn') exists (frontend/src/api/applications.ts:58-60).

**User pain:** The user withdraws from a role and clicks 'Unmark' on the offer card. Nothing visible happens near the card; a text banner appears somewhere at the top telling them to close the application instead — but there is no button to do that, so they must find the 'Application' button, open the drawer, find the Close control, pick Withdrawn, and close. The system knows exactly what it wants them to do and makes them do it by hand.

**Proposal:** In handleClearApplied, special-case ApiError status 409: show a confirm-style prompt on the spot ('This application has history. Close it as Withdrawn instead?') that calls closeApplication(offerId,'withdrawn') on accept, or opens the ApplicationDrawer (setOpenApplicationId) on 'review first'. Keep the raw banner only for genuinely unexpected errors.

**Verifier notes:** All cited lines check out: OfferCard.tsx:383-391 fires onClearApplied with no confirmation, OffersPage.tsx:219-227 swallows the backend's 409 ApplicationHasHistory (SetOfferApplication.cs:37-39, mapped at ResultExtensions.cs:36) into a top-of-page banner (OffersPage.tsx:383-387) that can be off-screen mid-scroll, and closeApplication(offerId,'withdrawn') (applications.ts:58-60) is the exact one-call remedy; no 409-specific handling exists anywhere in the frontend. Minor correction: the 409 fires only when the application has accumulated history (tasks/notes/etc.) — a history-less unmark clears cleanly — so the dead-end is occasional, keeping impact at medium rather than high.

<a id="f21"></a>
### 21. Expose edit/reschedule for interviews and tasks — the update API already supports it

**Impact:** medium · **Effort:** M · **Found by:** applications (×1)

**Today (evidence):** The API client supports full edits: TaskUpdate has title/description/dueAt/completed (frontend/src/api/applications.ts:87-96) and InterviewUpdate has kind/scheduledAt/interviewer/outcome/notes (applications.ts:125-135). But the drawer only ever calls updateTask with {completed} (ApplicationDrawer.tsx:269) and updateInterview with {outcome} (ApplicationDrawer.tsx:287); the rest of each row is read-only with only a delete ✕ (tasks: 506-548, interviews: 699-730). Also, InterviewDto.notes and TaskDto.description exist (frontend/src/api/types.ts:463-471, 489-498) but are never captured by the add forms (tasks form: title+dueAt only, 476-491; interview form: kind+scheduledAt+interviewer only, 662-684) nor rendered in the lists.

**User pain:** Interviews get rescheduled constantly. Today the only way to change a phone screen's time is delete the interview (losing it from the record) and re-add it. Same for fixing a typo'd task title or pushing a due date. And there is nowhere to put interview prep notes or a task description even though the data model has the fields — users end up stuffing everything into the Notes tab, detached from the interview it belongs to.

**Proposal:** Add inline edit to task and interview rows (pencil icon → the row swaps to the same form controls used by the add form, Save calls updateTask/updateInterview with the changed fields). While there, add the optional notes textarea to the interview add/edit form and render i.notes in the row, and render t.description under the task title. Pure frontend — no API change.

**Verifier notes:** All cited lines check out: TaskUpdate/InterviewUpdate in applications.ts support full edits, but the drawer's only mutations are {completed} (line 269) and {outcome} (line 287); task/interview rows offer only unconfirmed delete, and TaskDto.description / InterviewDto.notes are never captured or rendered. Minor addition strengthening the finding: a saved interview outcome also becomes uneditable (line 725), so edit-in-place fixes that too; delete+re-add is genuinely destructive, making this a real recoverability/speed win for a pure-frontend change.

<a id="f22"></a>
### 22. Use the timeline entry `kind` for visual scanning and filtering

**Impact:** low · **Effort:** S · **Found by:** applications (×1)

**Today (evidence):** TimelineEntryDto carries a typed `kind` (stageChanged/closed/reopened/note/task/taskDone/document/communication/interview — frontend/src/api/types.ts:405-414, 450-455), but TimelinePanel ignores it entirely: every entry renders the same brand-colored dot + title + detail (ApplicationDrawer.tsx:370-390; single .app-drawer__timeline-dot style at ApplicationDrawer.css:412-420).

**User pain:** After a two-month process an application's timeline is 30+ visually identical rows. Answering 'when was my last outbound email?' or 'which stage changes happened after the tech interview?' means reading every line, because notes, stage moves, tasks, and communications are indistinguishable at a glance — the merged timeline's whole point.

**Proposal:** Map `kind` to a small icon or dot color per category (lifecycle = brand, note = neutral, task/taskDone = amber/green, communication = blue, interview = purple) and add a one-row chip filter above the list (All / Stage / Notes / Tasks / Comms / Interviews) that client-side filters detail.timeline by kind. Data is already delivered; ~40 lines of frontend.

**Verifier notes:** All cited code is accurate: TimelineEntryDto carries a typed kind (types.ts:405-414, 450-455) that TimelinePanel never reads (ApplicationDrawer.tsx:370-390; single brand-colored dot, ApplicationDrawer.css:412-420), and kind-based visual coding genuinely aids scanning the only merged view — notably lifecycle events (stageChanged/closed/reopened), which appear nowhere else. Correction: the chip-filter half largely duplicates the drawer's existing per-type tabs (Notes/Tasks/Documents/Interviews/Contact with counts, ApplicationDrawer.tsx:45-53, 844-858 — "last outbound email" is already one click away in Contact), so the proposal should shrink to per-kind icons/colors plus at most a lifecycle filter; impact stays low.

---

## Settings

<a id="f23"></a>
### 23. Warn that saving fit weights resets every fit score to pending, and nudge to /enrich

**Impact:** medium (finder said high; revised by verifier) · **Effort:** S · **Found by:** settings, cv (×2)

**Today (evidence):** frontend/src/pages/Settings/WeightsSection.tsx:44-49 describes weights only as 'guidance… Must sum to 100'; on success it shows just 'Weights saved.' (lines 73-77). The backend, however, invalidates ALL fits on every weights save: backend/src/Application/Settings/SettingsService.cs:41-42 calls enrichment.InvalidateAllFitsAsync(). The only 'run /enrich' nudge in the app is EnrichmentIndicator, rendered solely on the Offers page (frontend/src/pages/Offers/OffersPage.tsx:298).

**User pain:** The user tweaks the salary weight by 5 points, sees 'Weights saved.', returns to the feed — and every single offer now shows fit 'pending' with no scores to sort by. Nothing in Settings told them the save wiped all fits or that they must run /enrich in Claude Code to get them back. It looks like data loss until they connect the dots.

**Proposal:** In WeightsSection: (a) add one caution line under the description — 'Saving re-flags every offer's fit as pending until you run /enrich in Claude Code'; (b) on successful save, replace the plain 'Weights saved.' with a message that fetches getEnrichmentStatus() (api/enrichment.ts already exists) and says 'Weights saved — N fit scores are now pending. Run /enrich in Claude Code to re-score.' mirroring the EnrichmentIndicator hint (EnrichmentIndicator.tsx:66-70).

**Verifier notes:** All code claims verified: SettingsService.cs:41-42 unconditionally flips every OfferFit to Pending on weights save (EnrichmentRepository.cs:76-78), while WeightsSection.tsx:46-49,73-77 gives no warning, and the only /enrich nudge is EnrichmentIndicator at OffersPage.tsx:298; the DTO even has a per-kind pendingFits field, making the proposed message trivial. Impact revised from high to medium because the feed indicator already surfaces the pending count and /enrich hint afterwards — the gap is causal clarity at save time for an infrequent action, not an unrecoverable dead end.

<a id="f24"></a>
### 24. Show 'Next run' for the scan schedule so the user can verify their cron before/after saving

**Impact:** medium · **Effort:** M · **Found by:** settings, sources-scans (×2)

**Today (evidence):** frontend/src/pages/Settings/SettingsPage.tsx:61-71 — the cron field is a free-text input with a static example hint; the only feedback loop is submitting and reading a server error. Lines 89-91 render only lastRunUtc. ScheduleDto (frontend/src/api/types.ts:210-215) has no next-run field; the backend already has a Cronos-based ICronEvaluator (backend/src/Application/Scheduling/ICronEvaluator.cs:10-17) exposing Validate + GetPreviousOccurrence, and the scheduler computes occurrences from the same expression.

**User pain:** The user edits the cron to '0 6,13,20 * * *' intending three scans a day. Today the only way to know whether it parsed the way they meant — and in the right time zone — is to save, wait, and check 'Last scheduled run' hours later. A typo like '0 6,13,20 * *' or a wrong timezone id is discovered a day later as 'why didn't it scan?'.

**Proposal:** Small, explicit API addition: add nextRunUtc (Cronos GetNextOccurrence over the saved cron+timeZone, one new ICronEvaluator method) to GET/PUT /api/schedule and to ScheduleDto; render 'Next run: {local time}' beside 'Last scheduled run' in SettingsPage. The saved-state round-trip then instantly confirms the expression means what the user intended.

**Verifier notes:** All cited code checks out and no next-run affordance exists anywhere; one correction: the pain examples (4-field typo, bad timezone id) are actually caught at save by CronosCronEvaluator.Validate with an inline error — the real, stronger pain is valid-but-wrong input, e.g. CronosCronEvaluator.cs:54-55 silently reparses a 6-field expression as IncludeSeconds ('0 6,13,20 * * *' saves fine but fires hourly, not 3x/day) and a valid-but-wrong timezone saves silently, both only discoverable by watching lastRunUtc for a day. A nextRunUtc readout catches both instantly and Cronos has GetNextOccurrence natively, so effort is closer to S than M.

<a id="f25"></a>
### 25. After a successful restore, offer/force an app reload — the whole UI is stale

**Impact:** medium (finder said high; revised by verifier) · **Effort:** S · **Found by:** settings (×1)

**Today (evidence):** frontend/src/pages/Settings/BackupSection.tsx:69-88 — handleRestore closes the modal, clears the file input, and sets only a text message ('Restore complete… safety backup saved to …'). Nothing re-fetches. The other Settings cards on the same page (schedule SettingsPage.tsx:21-35, weights WeightsSection.tsx:20-24, pipeline stages PipelineStagesSection.tsx:20-29, normalization, enrichment) all loaded their DTOs once on mount and now display pre-restore values; every input on screen shows data that no longer exists in the DB.

**User pain:** The user restores last week's backup, sees 'Restore complete', then edits the pipeline stages list still rendered from the pre-restore database — renames/reorders operate on stage IDs that may no longer exist, producing confusing 404/errors — or they simply trust on-screen weights/schedule values that are wrong until they happen to hard-refresh.

**Proposal:** On restore success, show the report (compatibility + safetyBackupPath from RestoreReportDto, api/types.ts:345-352) in a small success dialog with a primary 'Reload app' button that calls window.location.reload() (or auto-reload after a few seconds, persisting the success message through sessionStorage so it survives the reload). This guarantees every page re-fetches restored data.

**Verifier notes:** All cited code verified: handleRestore (BackupSection.tsx:69-88) only sets a text message, and every sibling Settings card fetches once on mount and stays mounted, so post-restore the visible schedule/weights/stages are stale — no reload/refetch mechanism exists anywhere in the frontend. Minor correction: other pages self-heal on navigation (routes remount, App.tsx:52-60), so only the currently-mounted Settings page is truly stuck; combined with restore being a rare operation, impact is medium rather than high, though the fix is small and clearly worthwhile.

<a id="f26"></a>
### 26. Settings cards vanish silently when their initial load fails — the error is never rendered

**Impact:** low (finder said medium; revised by verifier) · **Effort:** S · **Found by:** settings (×1)

**Today (evidence):** frontend/src/pages/Settings/WeightsSection.tsx:26 ('if (!w) return null'), EnrichmentSection.tsx:39 ('if (!settings) return null'), NormalizationSection.tsx:19 ('if (!dto) return null') — each early-returns before the error JSX (WeightsSection.tsx:68-72 etc.), and the catch handlers (e.g. WeightsSection.tsx:23 setError('Failed to load weights.')) set state that can never be displayed because the DTO stays null forever. There is no retry path.

**User pain:** The backend hiccups (e.g. app starting, DB briefly down, restore in progress behind MaintenanceGate) while the user opens Settings: the Weights, Enrichment and Normalization cards are simply absent. The user concludes the feature doesn't exist or scrolls around confused; the only fix — a full page refresh — is never suggested.

**Proposal:** In each of the three sections, when error is set and the DTO is null, render the card shell (title + the 'Failed to load…' settings-msg--error + a 'Retry' button that re-runs the fetch) instead of returning null. ~10 lines per section, matching the existing error-banner styles.

**Verifier notes:** All three citations are exact: the null-guard precedes the error JSX, so the load-failure message set in each catch is dead code and the cards vanish silently with no retry; every other surface (OffersPage:383, ApplicationsPage:71, ScansPage:28, SourcesPage:197, CvPage:115, and sibling PipelineStagesSection:151) does render its load error, making this a real inconsistency worth the ~10-line fix. Impact revised to low because on a single-user local app started via ./start.ps1 the Settings-load failure window is rare, though the confusion when it happens is high.

<a id="f27"></a>
### 27. Stale 'saved' confirmations persist next to edited, unsaved values — clear them on change and show a dirty hint

**Impact:** low (finder said medium; revised by verifier) · **Effort:** S · **Found by:** settings (×1)

**Today (evidence):** All four save-forms set saved=true on success and only reset it at the start of the NEXT save: SettingsPage.tsx:18,41-45,98-102 (schedule), WeightsSection.tsx:18,33,37,73-77, EnrichmentSection.tsx:26,45,49,83-87, NormalizationSection.tsx:11,27,40,104-108. None of the onChange handlers (e.g. WeightsSection.tsx:58, SettingsPage.tsx:66) touch `saved`, and there is no dirty indicator anywhere on the page.

**User pain:** The user saves weights ('Weights saved.' appears), then adjusts two numbers while thinking, gets distracted, and navigates to the feed. The green 'Weights saved.' banner was still on screen the whole time, actively asserting the edited values were persisted — they weren't, and the user only discovers it when fits are scored against the old weights.

**Proposal:** In each section, clear the success message on any field change (setSaved(false) in onChange, or derive dirty by comparing current state to the last-saved DTO snapshot) and, when dirty, show a small neutral 'Unsaved changes' chip next to the Save button. Same one-line pattern in all four forms keeps behavior consistent.

**Verifier notes:** Verified in all four forms: `saved` is only reset at the start of the next save and no onChange clears it, so the green success banner persists beside edited, unpersisted values; no dirty/unsaved affordance exists anywhere in frontend/src (two cited line numbers are off by one: EnrichmentSection.tsx 46/50 not 45/49, NormalizationSection.tsx 28 not 27 — immaterial). Genuine status-clarity fix with trivial (S) effort, but Settings is an occasional surface and the failure needs save→edit→abandon, so impact is more honestly low than medium.

<a id="f28"></a>
### 28. Pipeline-stage reorder/rename failures leave the list showing an order/name the server rejected

**Impact:** low (finder said medium; revised by verifier) · **Effort:** S · **Found by:** settings (×1)

**Today (evidence):** frontend/src/pages/Settings/PipelineStagesSection.tsx:57-64 — move() swaps the array optimistically via setStages(next) BEFORE calling reorderStages; run() (lines 31-43) re-fetches listStages() only inside the try, so on failure the swapped order stays on screen alongside the error banner. Rename inputs are uncontrolled (defaultValue={stage.name}, line 93), so a failed renameStage (lines 51-55, error swallowed by .catch(() => {})) leaves the typed name in the input while the server kept the old one.

**User pain:** The user reorders 'Phone screen' above 'Recruiter chat'; the API call fails (app restarting, restore in progress). The list shows the new order plus a generic error. They move on believing the board is reordered — the Applications board still shows the old column order, and the Settings list silently disagrees with it until a refresh.

**Proposal:** In run()'s catch (line 37-39), re-fetch and setStages(await listStages()) before setting the error so the UI always reverts to server truth. Make the name inputs controlled (value from a per-row draft state initialised from stages) so that refresh also resets a failed rename.

**Verifier notes:** Verified against PipelineStagesSection.tsx: move() sets state optimistically (line 62) before the API call and run()'s catch (lines 37-39) never re-fetches, so a rejected order/name persists on screen; inputs are uncontrolled (defaultValue, line 93) so even a re-fetch would not reset a failed rename, making the controlled-input part of the fix necessary. One minor evidence imprecision (the .catch(() => {}) swallows only the rethrow — run() still shows the error banner) and stage editing is an infrequent task that only desyncs on a failed mutation, so impact is low rather than medium; the fix itself is correct, small, and improves error recoverability.

<a id="f29"></a>
### 29. Enrichment-limits Save disables silently — say which field is invalid

**Impact:** low · **Effort:** S · **Found by:** settings (×1)

**Today (evidence):** frontend/src/pages/Settings/EnrichmentSection.tsx:41 computes invalid = ORDER.some((k) => settings[k] \< 1) and line 90 sets disabled={invalid} on the Save button, but no message is ever rendered for the invalid case (the error banner at lines 78-82 is only for API failures). Clearing a number field coerces to 0 via Number('') (line 72), instantly disabling Save with zero explanation. Contrast WeightsSection, which at least shows the red 'Total: N / 100' indicator (WeightsSection.tsx:64-66).

**User pain:** The user clears 'Max key skills' to type a new value; the field snaps to 0 and the Save button greys out. Nothing on the card says why — they click the dead button a few times, then re-scan the five fields hunting for the problem.

**Proposal:** When invalid, render a small inline hint under the grid ('All values must be at least 1') and add aria-invalid/red border styling to the offending input(s) — mirroring the weights-total pattern so both cards explain their disabled Save the same way.

**Verifier notes:** Verified against EnrichmentSection.tsx (lines 41, 70-72, 78-82, 90) and WeightsSection.tsx (64-66): the invalid state only greys out Save with no rendered explanation, clearing a field coerces to 0 via Number(''), and a frontend-wide grep confirms no existing inline-validation hint or aria-invalid styling anywhere. Genuine but minor consistency/recoverability fix; impact correctly rated low, effort S.

---

## Sources & scans

<a id="f30"></a>
### 30. Fix the cry-wolf 'Scan incomplete (LayoutChanged)' shown on every multi-source scan

**Impact:** high · **Effort:** M · **Found by:** sources-scans (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/backend/src/Application/Scanning/ScanOrchestrator.cs:243-251 — the \<50% sanity guard compares this source's per-source count (context.Collected) against previous.Counts.Collected, but C:/Users/tomas/Repo/Job/backend/src/Infrastructure/Persistence/Repositories/ScanRunRepository.cs:19-29 returns the last Complete run's WHOLE-RUN total (ScanRun stores only aggregate counts). With 2+ enabled sources each source collects a fraction of the previous total, so the guard trips every run, reconciliation early-returns (offers are never marked unavailable — 'Unavailable' stays 0), and the run finishes Partial/LayoutChanged. The frontend then shows it verbatim on every scan: C:/Users/tomas/Repo/Job/frontend/src/pages/Offers/ScanBanner.tsx:30-36 renders 'Scan incomplete (LayoutChanged) — partial results shown', and C:/Users/tomas/Repo/Job/frontend/src/pages/Scans/ScansPage.tsx:61 renders a permanent 'partial' chip. Project memory confirms: 'multi-source scans always go Partial/LayoutChanged'.

**User pain:** Every single scan ends with a warning banner and a 'partial' row in history, so the user learns to ignore the app's only completeness signal — and when a source genuinely breaks (Cloudflare challenge on theprotocol) it looks identical to a healthy run. Worse, because the guard aborts reconciliation, offers that vanished from job boards stay 'available' in the feed forever, so the user reads and rates dead offers.

**Proposal:** Backend fix (small, high-value): persist per-source collected counts on ScanRun (e.g. a jsonb sourceId→collected map written in Finish) and change ReconcileAsync to compare context.Collected against the SAME source's previous count, not the run total. No frontend change needed for the fix itself; once per-source counts exist, also expose them in the scan summary DTO so the history table and banner can attribute partial outcomes to a named source.

**Verifier notes:** Verified in code: the guard (ScanOrchestrator.cs:243-251) compares a per-source count against the last Complete run's whole-run total (ScanRun stores only aggregate ScanCounts; ScanRunRepository.cs:19-30), so with 2+ sources at least one source always trips, every run ends Partial/LayoutChanged (frozen reference — only Complete runs update it), and ScanBanner.tsx:30-36 / ScansPage.tsx:61 surface the false warning on every scan. One correction: reconciliation is skipped only for sources that trip the guard, so a dominant source (>50% of the total) still marks its vanished offers unavailable — 'Unavailable stays 0' is overstated, but smaller sources' dead offers do stay available forever and the cry-wolf outcome is real.

<a id="f31"></a>
### 31. Attach the scan banner to an already-running scan instead of erroring, and stop mislabeling mid-scan failures as 'failed to start'

**Impact:** medium · **Effort:** S · **Found by:** sources-scans, async-feedback (×2)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/pages/Offers/OffersPage.tsx:179-197 — handleRunScan only ever polls the scanRunId it created; if a scheduled or other-tab scan is running, POST /api/scans/run returns the ScanInProgress error (C:/Users/tomas/Repo/Job/backend/src/Application/Scanning/ScanOrchestrator.cs:36-49) and the UI shows it as a dead-end error banner with no way to watch the live scan. The catch at OffersPage.tsx:192-194 also collapses every failure — including a poll() network hiccup minutes into a running scan — into 'Scan failed to start.'. The data to attach exists: listScans (C:/Users/tomas/Repo/Job/frontend/src/api/scans.ts:8-10) returns runs with outcome=null while unfinished (ScanEndpoints.cs:77).

**User pain:** The schedule fires 3×/day; if the user clicks 'Run scan' while one is in flight they get what reads like a failure ('A scan is already running...') and must guess when to retry. And if the backend restarts mid-poll, the banner claims the scan 'failed to start' even though it started and may have finished — the feed is then never refreshed (load() after poll is skipped).

**Proposal:** On ScanInProgress (branch on ApiError.code, which client.ts already exposes) — and optionally on page mount — call listScans(), find the run with outcome===null, and hand its id to the same poll+banner path so the button shows 'Scanning…' and the feed refreshes on completion. Split the catch so start-failures and poll-failures get distinct messages ('Lost contact with the running scan — check Scan history').

**Verifier notes:** All cited code checks out: handleRunScan (OffersPage.tsx:179-197) only polls its own run, ScanInProgress (ScanOrchestrator.cs:36-49, 409 via ResultExtensions.cs:15) surfaces as a dead-end error banner, non-ApiError poll failures mid-scan collapse to 'Scan failed to start.' and skip the feed refresh, and listScans exposes in-flight runs (outcome=null, ScanEndpoints.cs:77) that no frontend code attaches to. One nuance: an ApiError thrown mid-poll would show its own message rather than the 'failed to start' fallback, but the realistic mid-scan failures (backend restart, network drop) are TypeErrors and behave exactly as claimed.

<a id="f32"></a>
### 32. Surface the per-source 'Scan now' action that the API already supports

**Impact:** medium · **Effort:** S · **Found by:** sources-scans (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/api/scans.ts:4-6 — runScan(sourceIds) accepts a source-id list and the backend parses it (C:/Users/tomas/Repo/Job/backend/src/Web/Endpoints/ScanEndpoints.cs:17-38), but the only caller passes nothing (C:/Users/tomas/Repo/Job/frontend/src/pages/Offers/OffersPage.tsx:184 'runScan()'). The Sources page card actions are only Enable/Disable and Edit (C:/Users/tomas/Repo/Job/frontend/src/pages/Sources/SourcesPage.tsx:371-388); after saving criteria the form just closes and reloads the list (SourcesPage.tsx:141-169).

**User pain:** After editing a source's search criteria (e.g. adding a category), the only way to test it is to run a full all-sources scan from the Offers page — slow (theprotocol goes through Playwright/Chromium) and the aggregate outcome can't tell you whether YOUR edited source worked. Likewise, when a scan reports failure there is no way to re-run just the suspect source to isolate it.

**Proposal:** Add a 'Scan now' button to each source card on SourcesPage calling runScan([s.id]) and polling getScanStatus with the existing poll() helper, showing an inline per-card status line (running → collected N / incomplete reason). Optionally add a 'Save & scan' secondary submit in the edit form. Frontend only — the endpoint and client function already exist.

**Verifier notes:** All citations verified exactly (scans.ts:4-6, ScanEndpoints.cs:17-38, OffersPage.tsx:184, SourcesPage.tsx:371-388/141-169); the backend honors sourceIds end-to-end (ScanOrchestrator.ResolveSourcesAsync:328-345) and no per-source trigger exists anywhere in the frontend, so this is a genuine S-effort frontend-only win. One correction to fold into the proposal: the \<50% sanity guard compares a source's count against the previous complete run's run-wide total (ScanOrchestrator.cs:243-251 + ScanRunRepository.cs:29), so the first single-source run may falsely report Partial/LayoutChanged — the inline status UI should soften that message or the guard needs a small per-source-count fix.

<a id="f33"></a>
### 33. Make scan history live and time-precise: time-of-day, duration, and rows that stop saying 'running' forever

**Impact:** medium · **Effort:** S · **Found by:** sources-scans (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/pages/Scans/ScansPage.tsx:12-21 — a single one-shot fetch; no polling, no refresh button. Line 58 renders startedAt with formatDate, which is date-only ('2 Jul 2026', C:/Users/tomas/Repo/Job/frontend/src/lib/format.ts:42-46). ScanRunSummaryDto.finishedAt (C:/Users/tomas/Repo/Job/frontend/src/api/types.ts:203) is never displayed, and an unfinished run's null outcome renders a 'running' chip (ScansPage.tsx:61, 77-82) that never resolves without a browser reload.

**User pain:** With scans running 3×/day, the history is a stack of identical '2 Jul 2026' rows — the user cannot tell whether the 8pm scheduled run actually happened, which run was this morning's, or how long a Playwright-heavy scan took. Opening the page during a scan shows a 'running' row that appears stuck forever, indistinguishable from a crashed run.

**Proposal:** Add a formatDateTime helper (day + HH:mm) for the Started column, add a Duration column derived from finishedAt−startedAt, and while any run has outcome===null re-fetch the list on an interval using the existing poll() helper (stop once all rows are terminal). Frontend only.

**Verifier notes:** All citations verified: ScansPage.tsx:12-21 is a one-shot fetch with no refresh, formatDate (format.ts:42-46) is date-only, finishedAt (types.ts:203) is rendered nowhere in the frontend, and a null outcome yields a 'running' chip (ScansPage.tsx:61) that never resolves without a reload; the Offers-page ScanBanner polling only covers manually triggered scans, not scheduled runs or the history table. Bonus consistency point: the rest of the app already shows full date+time via toLocaleString() (ApplicationDrawer, BackupSection, SettingsPage), so the date-only Scans column is also an internal inconsistency the S-sized, frontend-only proposal fixes.

<a id="f34"></a>
### 34. Link scan results to the offers they actually found

**Impact:** medium · **Effort:** M · **Found by:** sources-scans (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/pages/Scans/ScansPage.tsx:63-66 renders Collected/New/Updated as plain numbers with no navigation; OffersPage's only deep-link is ?offerId= (C:/Users/tomas/Repo/Job/frontend/src/pages/Offers/OffersPage.tsx:79-93) and the view/source/sort state is plain useState (lines 60-62), not URL-driven. The data to answer 'what did this scan find?' already exists server-side: every upsert records an OfferObservation keyed by scanRunId (C:/Users/tomas/Repo/Job/backend/src/Application/Scanning/ScanOrchestrator.cs:228).

**User pain:** The user sees 'New: 12' in scan history (or in the completion banner) and has no way to see WHICH 12 — the Offers 'New' view is cumulative unviewed-new across all scans, so after two scans the sets blur together. Auditing a suspicious run ('why did the 6am scan add 40 offers?') means scrolling the whole feed.

**Proposal:** Small, obvious API addition: accept scanRunId on GET /api/offers (filter via the existing offer_observations table) and add it to OffersQuery (frontend/src/api/types.ts:159-168). Then make the New/Collected cells in ScansPage links to /offers?scanRunId=...&status=new, and have OffersPage read that param into its query (it already has useSearchParams wired). Show a small 'showing offers from scan of 2 Jul 06:00 — clear' chip.

**Verifier notes:** All cited code checks out (ScansPage.tsx:63-66 plain counts, OffersPage.tsx:79-93 offerId-only deep-link, ScanOrchestrator.cs:228 per-upsert OfferObservation keyed by scanRunId) and no scan-to-offers navigation exists anywhere in the frontend or backend (GET /api/offers has no scanRunId param). One correction: since an observation is recorded for every collected offer, ?scanRunId=X&status=new yields "collected in scan X and still unreviewed" rather than exactly the scan's New count — the New-cell link needs first-observed-in-this-scan semantics (or should be labeled as such), while the Collected-cell link is exact.

<a id="f35"></a>
### 35. Translate incompleteReason enum tokens into plain-language, actionable messages

**Impact:** low · **Effort:** S · **Found by:** sources-scans (×1)

**Today (evidence):** C:/Users/tomas/Repo/Job/frontend/src/pages/Offers/ScanBanner.tsx:33 interpolates the raw token — 'Scan incomplete (LayoutChanged) — partial results shown' — and C:/Users/tomas/Repo/Job/frontend/src/pages/Scans/ScansPage.tsx:67 dumps run.incompleteReason verbatim into the Note column. The backend emits the bare enum name (C:/Users/tomas/Repo/Job/backend/src/Web/Endpoints/ScanEndpoints.cs:86,107) from a closed set of four values: LoginNotCompleted, ChallengeDetected, NetworkFailure, LayoutChanged (C:/Users/tomas/Repo/Job/backend/src/Domain/Scans/ScanEnums.cs:21-27).

**User pain:** 'LayoutChanged' actually means 'a source returned suspiciously few offers, so nothing was marked unavailable' — nothing about a layout. 'ChallengeDetected' means 'the site showed an anti-bot wall; results for that source were skipped'. A user staring at the banner can neither understand what happened nor know what to do next (retry? check the site? ignore?).

**Proposal:** Add a small copy map in the frontend (keyed on the four known tokens, falling back to the raw value) used by both ScanBanner and the ScansPage Note column, each message stating what happened + consequence + suggested action, e.g. LayoutChanged → 'One source returned far fewer offers than last time; its results were kept but nothing was marked unavailable — retry, or scan that source alone.' Frontend only, ~20 lines.

**Verifier notes:** All citations verified (ScanBanner.tsx:33, ScansPage.tsx:67, ScanEndpoints.cs:86/107, ScanEnums.cs:21-27) and no translation map exists anywhere in the frontend; the token even shows on virtually every multi-source scan since the sanity guard always downgrades to Partial/LayoutChanged. One correction: LayoutChanged has a second trigger (pagination truncated at the hard page cap, SourceCollection.cs:124-126), so the copy for that token must cover both causes rather than only the "suspiciously few offers" guard.

---

## CV & tailored CVs

<a id="f36"></a>
### 36. Flag tailored CVs that are stale after a CV replacement — and stop mislabeling the source CV in the modal

**Impact:** high · **Effort:** S · **Found by:** cv (×1)

**Today (evidence):** TailoredCvDto carries sourceCvId (frontend/src/api/types.ts:365) and generation is deliberately never auto-invalidated (004 supersede guard). frontend/src/components/TailorCvModal/TailorCvModal.tsx:232-235 renders 'Source CV: {draft.sourceCv?.fileName}' — but draft.sourceCv is the CV that WOULD be used for the NEXT generation (backend TailoredCvService.cs:350 picks the newest CV), not the CV the displayed preview was generated from (tailored.sourceCvId). frontend/src/pages/TailoredCvs/TailoredCvsPage.tsx:94-163 shows 'Produced' rows with no staleness signal and never compares item.sourceCvId to the current CV list (listCvs exists at frontend/src/api/cv.ts:4-6).

**User pain:** User uploads a revised CV, then opens an offer's tailored CV produced last week. The modal says 'Source CV: cv-v2.pdf' right above a preview actually generated from the deleted cv-v1.pdf. They download the PDF and send an outdated CV to an employer without ever being told it predates their CV change.

**Proposal:** In TailorCvModal, when tailored exists and tailored.sourceCvId !== draft.sourceCv?.id, replace the misleading line with a warning chip: 'Generated from an older CV — Regenerate to use {draft.sourceCv.fileName}'. On TailoredCvsPage, fetch listCvs once alongside listTailored and badge rows whose sourceCvId is not the current effective CV ('Stale — CV changed since generation'). Frontend-only; all data already in the DTOs.

**Verifier notes:** Every citation checks out: sourceCvId (types.ts:365) is never read anywhere in the UI, TailorCvModal.tsx:232-235 unconditionally labels the NEXT-generation CV (GetDraftAsync → Newest() at TailoredCvService.cs:349-350) above a preview generated from tailored.sourceCvId, and TailoredCvsPage never compares against listCvs — so a produced tailored CV silently survives a CV replacement with a wrong-in-context label. Minor correction: the line is not strictly "mislabeled" (it accurately names the FR-003 input for the next generation) — it is contextually misleading when an older-source preview is displayed, which is precisely the case the proposed conditional warning targets; the fix is frontend-only, does not auto-invalidate (consistent with 004 ADR-4), and prevents the app's highest-stakes silent error (sending an outdated CV to an employer).

<a id="f37"></a>
### 37. Tailored CVs page: live-update pending rows, nudge /tailor-cv, and show the failure reason inline

**Impact:** medium · **Effort:** S · **Found by:** cv, async-feedback (×2)

**Today (evidence):** frontend/src/pages/TailoredCvs/TailoredCvsPage.tsx:41-45 loads the list once (refreshes only when the modal closes, :170-174); the state chip at :107-113 renders bare 'Pending'/'Failed'. The lastError field is in the list DTO (frontend/src/api/types.ts:374) but is rendered only inside the modal (TailorCvModal.tsx:284-286). The modal already live-polls pending state every 2.5s via lib/polling.ts (TailorCvModal.tsx:85-98) — the page does not, and unlike the modal ('Pending — run /tailor-cv in Claude Code', TailorCvModal.tsx:282) the page never tells the user how to make pending items progress.

**User pain:** User queues tailored CVs for 4 offers, keeps the Tailored CVs page open, and runs /tailor-cv in Claude Code. The page stays 'Pending' forever until a manual browser refresh. A 'Failed' row offers no reason and no cue that Regenerate re-queues it — the user must open each modal to find the error.

**Proposal:** Reuse poll() from lib/polling.ts to refresh listTailored while any row is pending (stop when none are); when pending rows exist show the same hint as EnrichmentIndicator ('N pending — run /tailor-cv in Claude Code'); render item.lastError as muted text under Failed chips. Frontend-only.

**Verifier notes:** All cited lines verified: the page loads once and never polls, chips are bare 'Pending'/'Failed', and lastError is present in the list payload (backend ListAsync maps LastError via ToView, TailoredCvService.cs:212-228/432-443) but rendered only in the modal — while the modal already polls at 2.5s and shows the /tailor-cv hint, so the proposal is a consistency fix reusing existing patterns. Minor evidence omission: the page also reloads after Remove (TailoredCvsPage.tsx:61), not only on modal close — immaterial.

<a id="f38"></a>
### 38. Add a 'Reset to default prompt' button in the Tailor CV modal

**Impact:** medium · **Effort:** S · **Found by:** cv (×1)

**Today (evidence):** frontend/src/components/TailorCvModal/TailorCvModal.tsx:127-141 — once promptEdited is true, skill toggles stop recomposing the prompt, and no control resets it. Reopening the modal does NOT recover the default either: the backend seeds a reopened draft from the EXISTING tailored CV's stored prompt (backend/src/Application/TailoredCvs/TailoredCvService.cs:53-55, 84-85), so after one generation with a custom prompt the pristine default is unreachable without deleting the tailored CV. The API already supports recomposition: getDraft with an explicit skills param returns the freshly built default (frontend/src/api/tailoredCv.ts:15-19; TailoredCvService.cs:76-80).

**User pain:** User experiments with the prompt, the generated CV comes out worse, and they want to return to the known-good server default. Today the only path is Remove (destroying the produced CV) then reopen — or hand-reconstructing the default text from memory.

**Proposal:** Add a 'Reset prompt' ghost button next to the prompt label that calls getDraft(offerId, { skills: selected }), then setPrompt(d.prompt) and setPromptEdited(false) so chip toggles resume recomposing. Frontend-only.

**Verifier notes:** Verified: setPromptEdited is only ever set true (TailorCvModal.tsx:40,267), chip recompose is gated on !promptEdited (line 133), and GetDraftAsync without a skills param seeds from an existing row's stored prompt (TailoredCvService.cs:82-85) while a supplied skills param always rebuilds the default (77-80) — so post-generation the default is unreachable except via destructive Remove. Caveat: getDraft drops the ?skills= query for an empty selection (tailoredCv.ts:17), so the reset must handle the zero-skills case (send the param anyway or restore the default selection) or it silently no-ops.

<a id="f39"></a>
### 39. Mark which uploaded CV is the effective one driving the profile, fit scores, and tailoring

**Impact:** medium · **Effort:** S · **Found by:** cv (×1)

**Today (evidence):** frontend/src/pages/Cv/CvPage.tsx:147-185 renders every uploaded CV identically (name + state chip + skills). The backend, however, has a hidden 'effective' notion: the profile that scores all offers comes from the newest produced-profile CV (backend/src/Application/Cv/ProfileService.cs:53-57), and Tailor CV always sources the newest-extracted CV (backend/src/Application/TailoredCvs/TailoredCvService.cs:350). CvDto exposes only fileName/state/extractedAt (frontend/src/api/types.ts:257-270); nothing in the UI distinguishes the active CV from older ones.

**User pain:** User keeps two CV variants uploaded (e.g., backend-focused and full-stack). Both show 'Profile ready' with their own skill chips. Which one is currently ranking 300 offers, and which will 'Tailor CV' attach? The UI gives no answer — the user can only guess by upload order, and the 'Derived profile' card doesn't say which CV it came from.

**Proposal:** Badge the effective CV in the list ('Active — drives profile & tailoring') and name it in the Derived profile card header. Cleanest with a tiny API addition (effectiveCvId on ProfileDto or isEffective on CvDto — one line each in ProfileService/CvEndpoints); a frontend approximation (newest produced by extractedAt) works today without it.

**Verifier notes:** Verified: CvPage renders all CVs identically, ProfileService.cs:53-57 silently picks the newest produced profile, and no effective-CV marker exists anywhere in the frontend or the wire DTOs. Two corrections: TailorCvModal.tsx:232-235 already names the source CV at generation time (so the pain is really about which CV drives profile/fit, not tailoring), and the proposed frontend-only approximation by extractedAt is unsafe — ProfileService selects by ProfileProducedAt, which CvDto does not expose, so the small API addition (isEffective/effectiveCvId) is required for a correct badge.

---

## Navigation & consistency

<a id="f40"></a>
### 40. Add status badges to the nav: pending/failed enrichment and overdue application tasks

**Impact:** medium · **Effort:** M · **Found by:** ia-consistency (×1)

**Today (evidence):** The nav is a static label list (frontend/src/App.tsx:12-20, rendered 32-45) with no counts. The data already exists cheaply: /api/enrichment/status returns pendingTotal/failedTotal (frontend/src/api/types.ts:273-281, used only by EnrichmentIndicator which renders solely on the Offers page, OffersPage.tsx:298), and the board response carries overdueTaskCount/nextInterviewAt per card (types.ts:433-434) — surfaced only as chips inside the Applications page (ApplicationsPage.tsx:158-172). No global surface shows either signal.

**User pain:** Overdue interview-prep tasks are invisible unless the user deliberately opens the Applications page — on a day spent triaging the Offers feed, a 'send follow-up' task silently goes overdue. Likewise, from any page other than Offers there is no cue that 12 offers still await /enrich.

**Proposal:** In the App shell, fetch enrichment status and the board once (reusing getEnrichmentStatus/getBoard) and render small count pills on the nav items: 'Offers · 12' (pendingTotal, red variant when failedTotal>0) and 'Applications · 2' (sum of overdueTaskCount). Refresh on route change or a slow interval; hide at zero.

**Verifier notes:** All citations verified exactly (static nav App.tsx:12-45; EnrichmentStatusDto types.ts:273-281 consumed only by EnrichmentIndicator on OffersPage.tsx:298; overdueTaskCount/nextInterviewAt types.ts:433-434 shown only as per-card chips in ApplicationsPage.tsx:158-172), and no badge/aggregate surface exists anywhere else — even the Applications subtitle shows only active/closed counts. The overdue-task pill is the stronger half (currently invisible off that page); the enrichment pill adds less since the user lives on Offers where the indicator already sits, so medium impact is right.

<a id="f41"></a>
### 41. Disambiguate 'View offer': same label opens an external site on cards but internal navigation everywhere else

**Impact:** low (finder said medium; revised by verifier) · **Effort:** S · **Found by:** ia-consistency (×1)

**Today (evidence):** On OfferCard, 'View offer ↗' is an external anchor to offer.canonicalUrl (frontend/src/components/OfferCard/OfferCard.tsx:308-315, target=_blank). The identical label 'View offer' is an internal in-app link to `/?offerId=` on the Applications board (frontend/src/pages/Applications/ApplicationsPage.tsx:175-177), the Tailored CVs page (frontend/src/pages/TailoredCvs/TailoredCvsPage.tsx:122-124), and the ApplicationDrawer header (frontend/src/components/ApplicationDrawer/ApplicationDrawer.tsx:167-174). The detail drawer meanwhile calls the external link 'View original ↗' (frontend/src/components/OfferDetail/OfferDetailDrawer.tsx:142-151). Related: the drawer tab is 'Contact' (ApplicationDrawer.tsx:52) but its content is 'Log communication'/'No communications logged' (lines 840, 845).

**User pain:** Muscle memory built on the feed ('View offer' = job-board site in a new tab) breaks on the Applications board where the same words keep you in-app — and vice versa: from Tailored CVs the user clicking 'View offer' expecting the posting gets scrolled around the feed instead. Every mis-click costs a tab switch or a back-navigation.

**Proposal:** Adopt one vocabulary: external = 'View original ↗' everywhere (rename OfferCard.tsx:314), internal = 'Show in feed' (Applications/TailoredCvs/ApplicationDrawer links). Rename the 'Contact' tab to 'Comms'/'Communications' to match its own buttons and the TimelineKind naming.

**Verifier notes:** All citations verified exactly, and the finding undercounts: TailorCvModal.tsx:202-203 is a fourth "View offer"-labeled internal navigation (handleViewOffer -> navigate('/?offerId=...')), strengthening the inconsistency claim. Impact revised medium->low because the external card label already carries a partially-disambiguating "↗" glyph and a single daily user habituates quickly — but the rename is a near-zero-cost, genuine consistency fix worth taking.

<a id="f42"></a>
### 42. Set per-route document titles

**Impact:** low · **Effort:** S · **Found by:** ia-consistency (×1)

**Today (evidence):** The tab title is the static \<title>Job Offer Matcher\</title> in frontend/index.html:8; no component ever writes document.title (grep across frontend/src finds zero occurrences). Every route (App.tsx:53-59) and every browser-history entry therefore shows the identical label.

**User pain:** With the app open in several tabs (feed in one, Applications in another, a tailored-CV preview in a third — the preview opens via target=_blank from TailoredCvsPage.tsx:128-133) all tabs read 'Job Offer Matcher', and browser history/back-forward lists are indistinguishable, so returning to 'the Applications tab' is guesswork.

**Proposal:** Add a tiny usePageTitle(title) hook (useEffect setting `document.title = `${title} — Job Offer Matcher``) called once per page component, or derive it in App from the matched NAV entry. Eight one-liners total.

**Verifier notes:** Verified: the only title in the codebase is the static one at frontend/index.html:8; no document.title/Helmet/usePageTitle anywhere in frontend/src; App.tsx:52-60 routes and the TailoredCvsPage.tsx:126-133 target=_blank preview (which guarantees multi-tab use) match the evidence. Genuine but small win — impact stays low; the NAV array at App.tsx:12-20 already provides the labels, so the derive-in-App variant is even cheaper than eight per-page one-liners.

---

## Async state & feedback

<a id="f43"></a>
### 43. Update offer status optimistically instead of a full feed refetch per triage action

**Impact:** high · **Effort:** M · **Found by:** async-feedback (×1)

**Today (evidence):** frontend/src/pages/Offers/OffersPage.tsx:199-206 — handleSetStatus awaits the POST then awaits load(), which re-runs the entire listOffers query (heavy payload: summaries, fit/affinity rationales, group members for every offer). handleMarkApplied (208-217) additionally refetches the whole application board. There is no per-card busy state — the Dismiss/Interested buttons in OfferCard.tsx:347-361 stay enabled while the request is in flight — and under sort='rank' the refetch can reorder the remaining cards.

**User pain:** Triaging is THE core loop: dismissing 30 offers in a session costs 30 x (POST + full-feed GET). Between click and refetch completion the card sits unchanged with live buttons (double-clicks fire duplicate requests), then the whole list re-renders and can reshuffle under the cursor, so the user loses their place mid-triage exactly when moving fast.

**Proposal:** For handleSetStatus, patch local state optimistically: setData(d => remove/patch the one offer per the active view's filter — dismissed leaves 'all'/'new', restore flips status), disable that card's action buttons while in flight, and roll back + show the error on failure; drop the full load(). Keep full refetch only for actions that change ordering-relevant data (apply/un-apply, where the affinity-basis meta also changes).

**Verifier notes:** All cited lines verified: handleSetStatus (OffersPage.tsx:199-206) awaits a full listOffers refetch per triage click, handleMarkApplied (208-217) adds a board refetch, and OfferCard.tsx:347-361 has no disabled/in-flight state (zero `disabled=` in the file), so double-clicks fire duplicates and unsignalled load() calls can race with a stale response winning setData; the reorder claim is stronger than stated because rank normalization in OfferReadService.cs:42-58 is relative to the result set (maxMonthly/min-max ticks), so dismissing an extremum offer rescales and can reorder remaining cards. One correction: scroll position already survives refetch (feed stays mounted, OffersPage.tsx:395-398), so the pain is card shift/reorder plus the refresh spinner, not scroll collapse — and the optimistic patch must also locally adjust meta.total/meta.new in the header.

<a id="f44"></a>
### 44. Surface per-card mutation errors near the action instead of an off-viewport banner at the page top

**Impact:** medium (finder said high; revised by verifier) · **Effort:** M · **Found by:** feed, async-feedback (×2)

**Today (evidence):** frontend/src/pages/Offers/OffersPage.tsx:199-236 — handleSetStatus, handleMarkApplied, handleClearApplied and handleSplitGroup all funnel failures into the single page-level error state, which renders as a state-block above the toolbar at OffersPage.tsx:383-387. The comment at line 224 even acknowledges the 409 'steer-to-Withdrawn' guidance for clearing an applied offer with history is delivered this way. There is no toast system, no scroll-to-error, and OfferCard's action buttons (frontend/src/components/OfferCard/OfferCard.tsx:347-401) have no per-card error slot.

**User pain:** The user is 2,000px deep in the feed, clicks 'Unmark' on an applied offer that has interview history, gets the 409 — and sees nothing. The error text (including the actionable guidance to close the application instead) renders at the top of the page, outside the viewport. From the user's chair the button silently did nothing, so they click again, still nothing, and conclude the app is broken. Same for a failed Dismiss/Interested anywhere below the fold.

**Proposal:** Add a small fixed-position toast (bottom corner, dismissible, auto-clear on next success) fed by the existing catch blocks, or render the error inline inside the affected OfferCard (pass an errorByOfferId map down). For the 409 clear-applied case specifically, include an inline 'Open application' action in the message (setOpenApplicationId is already available in OffersPage) so the steer-to-close guidance is one click away instead of a hunt.

**Verifier notes:** All claims verified (one trivial mislocation: the error block at OffersPage.tsx:383-387 renders below the toolbar, not above it — still page-top and off-viewport when scrolled). The finding is even understated: ApplyModal.tsx:90-94 delegates error display to the parent while its full-screen fixed overlay hides the page-top error entirely, so a failed 'Mark applied' shows no feedback at all; however, since failures on a loopback-only app are rare and the 409 unmark-with-history steer is occasional, impact is medium rather than high.

<a id="f45"></a>
### 45. Keep the Applications board mounted during refresh instead of blanking it on every drawer change

**Impact:** medium · **Effort:** S · **Found by:** async-feedback (×1)

**Today (evidence):** frontend/src/pages/Applications/ApplicationsPage.tsx:30-43 — load() sets loading=true, and the board render is gated on !loading at line 83 (`{!loading && !error && board && !isEmpty && ...}`), so the whole kanban is replaced by the 'Loading applications…' spinner block (lines 65-69) on every reload. The drawer calls onChanged={() => void load()} (line 131) after every mutation. Contrast with OffersPage, which deliberately keeps the feed mounted through background reloads and shows a small subtitle spinner (OffersPage.tsx:256-262, 395-397).

**User pain:** With the ApplicationDrawer open, every note added, task ticked, or stage moved makes the entire board behind the drawer flash to a full-page spinner and re-mount, losing horizontal scroll position across pipeline columns. Logging a call plus two tasks means three full board blank-outs in a row.

**Proposal:** Gate the big spinner on board === null only (first load), keep the board rendered while a background refresh runs, and reuse the OffersPage subtitle-spinner pattern ('offers-page__refresh-spinner') for the in-flight hint. One-line condition change plus the small spinner markup.

**Verifier notes:** All cited lines verified: ApplicationsPage.tsx:31/65-69/83/131 gate the whole kanban on !loading and every ApplicationDrawer mutation calls onChanged()->load() (ApplicationDrawer.tsx:106-122), unmounting the board (whose .board has overflow-x:auto, so scroll resets) while OffersPage.tsx:256-262/377/395-397 deliberately keeps its feed mounted with a subtitle spinner — the proposal just extends the codebase's own documented pattern. Only refinement: the empty-state branch (line 77) must also be regated on board !== null so a mid-refresh reload doesn't flash the empty message; still S effort.

<a id="f46"></a>
### 46. Make poll() tolerate transient fetch failures instead of failing the whole operation

**Impact:** low (finder said medium; revised by verifier) · **Effort:** S · **Found by:** async-feedback (×1)

**Today (evidence):** frontend/src/lib/polling.ts:42-49 — the loop awaits fetch() with no try/catch: a single failed status request rejects the entire poll. In OffersPage.tsx:192-196 that rejection lands in a catch-all that shows 'Scan failed to start.' even when the scan started fine and only one mid-poll status read hiccuped; the banner then reads as a failure while the scan actually completes. TailorCvModal.tsx:94-96 has the same fragility — its pending-CV poll dies silently on the first transient error ('the indicator simply stops updating').

**User pain:** During a scan the backend is busy (Playwright + upserts); one slow/failed /status response makes the banner flip to the red 'Scan failed to start.' error while offers keep arriving — the user may re-trigger the scan or distrust the results. In the tailor modal, a single blip means the 'Pending' badge never turns 'Produced' even though the worker finished, until the modal is reopened.

**Proposal:** In poll(), wrap fetch() in try/catch and tolerate up to N (e.g. 3) consecutive failures before rethrowing, resetting the counter on success. In handleRunScan, split error handling: runScan() failure → 'Scan failed to start.'; poll failure after start → 'Lost track of the scan — check Scan history.' with a link to /scans.

**Verifier notes:** Verified: polling.ts:42-48 has no try/catch so one rejection kills the poll; OffersPage.tsx:183-196's single catch misattributes mid-poll (and even post-scan load()) failures as 'Scan failed to start.' while ScanBanner shows the error over the real status, and TailorCvModal.tsx:94-96 swallows the error with no restart (state stays 'pending' so the effect never re-fires); no retry mechanism exists anywhere in the frontend. Two corrections: a slow response cannot trigger it (fetch has no timeout — only actual failures reject), and an HTTP-level mid-poll error shows the ApiError message rather than the fallback text; on loopback these triggers (backend restart, sleep/wake, dev-proxy blip) are rare, so impact is low rather than medium, though the S-sized fix is still worthwhile.

<a id="f47"></a>
### 47. Stop swallowing failures in CV delete and enrichment re-run

**Impact:** low (finder said medium; revised by verifier) · **Effort:** S · **Found by:** async-feedback (×1)

**Today (evidence):** frontend/src/pages/Cv/CvPage.tsx:75-83 — handleDelete is try/finally with no catch: if deleteCv fails, the rejection is unhandled, no error is shown, and the CV list silently stays unchanged. Same pattern in frontend/src/components/EnrichmentIndicator/EnrichmentIndicator.tsx:31-38 — rerun() has try/finally with no catch, so a failed 'Re-run failed'/'Re-run all' click shows nothing and leaves the counts untouched. Both files already have an error-display convention right next door (CvPage setError at lines 69, 98; the indicator has chips it could flash).

**User pain:** The user clicks 'Remove' on a CV (or 'Re-run failed' while a restore has the MaintenanceGate closed and writes are 409ing) — the button un-disables and nothing happens. No message, no state change; they retry a few times and assume the app is broken, when a one-line message ('Re-run rejected: maintenance in progress') would explain it.

**Proposal:** Add catch blocks mirroring the sibling handlers: CvPage.handleDelete → setError(e instanceof ApiError ? e.message : 'Failed to remove the CV.'); EnrichmentIndicator.rerun → keep a local error string and render it beside the actions (same muted text-sm style as the /enrich hint).

**Verifier notes:** Both cited handlers really are try/finally with no catch and no global error surface exists (no toast/boundary/unhandledrejection handler), while sibling handlers in the same files already catch-and-display — so the fix is a cheap, genuine consistency/recoverability win. One correction: the maintenance example is wrong — POST /api/enrichment/rerun defers via MaintenanceGate.WaitWhileActiveAsync (EnrichmentService.cs:184) instead of 409ing, so realistic silent failures are backend-down/500/delete errors, not restore 409s; combined with the low frequency of these actions, impact is low rather than medium.

---

## Accessibility & keyboard

<a id="f48"></a>
### 48. Add keyboard triage to the Offers feed (j/k + single-key actions)

**Impact:** high · **Effort:** M · **Found by:** a11y-keyboard (×1)

**Today (evidence):** frontend/src/pages/Offers/OffersPage.tsx:395-416 renders the feed as plain \<article> cards with zero page-level key handling (a grep for keydown/onKeyDown across src/**/*.tsx shows handlers exist ONLY inside the five dialogs). frontend/src/components/OfferCard/OfferCard.tsx:308-402 exposes every triage action as a separate small button — title/Details (86-95, 317-325), View offer link (309-315), Tailor CV (326-332), Interested (347-353), Dismiss (354-360), Mark applied (394-400) — so one card contains 6-8 tab stops. The Applications board already ships a focusable-card pattern (ApplicationsPage.css:66-70 .board-card:focus-within ring) that the feed lacks.

**User pain:** The primary daily task is triaging dozens of new offers: read summary/fit, then Dismiss / Interested / open Details / Mark applied. Keyboard-only, reaching the Dismiss button of the 20th card means tabbing through ~120 focus stops; in practice the user is forced onto the mouse for every single card, hundreds of precise small-button clicks per scan.

**Proposal:** Add a roving 'current card' to OffersPage: ArrowDown/ArrowUp (or j/k) moves selection (scrollIntoView + a visible ring reusing the offer-card--highlight / board-card focus-within styling), and single keys act on the selected card — Enter/o opens the Details drawer (setOpenDetailId), i = Interested, x = Dismiss/Restore, a = opens ApplyModal, t = Tailor CV. Ignore keys when a dialog is open or an input has focus (document.activeElement check). Pure frontend; handlers already exist as handleSetStatus/onOpenDetail/etc.

**Verifier notes:** All evidence verified: no keyboard handling exists outside modal Escape/focus-traps (plus one Settings input Enter handler the finding's census missed), OfferCard exposes ~7 tab stops per card at the cited lines, and no j/k/roving-focus capability exists anywhere in the frontend. Minor correction: the 'a' (ApplyModal) and 't' (TailorCvModal) shortcuts need the modal-open state lifted out of OfferCard (OfferCard.tsx:62-64) or a trigger prop, since only status/detail handlers live in OffersPage — still frontend-only, effort M holds.

<a id="f49"></a>
### 49. Give OfferDetailDrawer/ApplicationDrawer initial focus + Tab trap, and lock body scroll under overlays

**Impact:** medium (finder said high; revised by verifier) · **Effort:** S · **Found by:** detail, a11y-keyboard (×2)

**Today (evidence):** OfferDetailDrawer.tsx:36-47 and ApplicationDrawer.tsx:93-104 only save previouslyFocused and listen for Escape — unlike ApplyModal.tsx:53-86 and RestoreConfirmModal.tsx:24-55 they never move focus into the dialog and have no Tab trap, despite declaring aria-modal="true" (OfferDetailDrawer.tsx:54, ApplicationDrawer.tsx:145). TailorCvModal.tsx:101-125 has the trap but no initial focus and no focus-restore effect at all. No file sets body overflow/scroll-lock (grep for overflow|body.style found only internal scroll containers, e.g. OfferDetailDrawer.css:6-7 where .offer-detail itself is the max-height 88vh scroll area).

**User pain:** Open an offer's Details with Enter: focus stays on the card's title button behind the dim overlay. PageDown/arrows scroll the BACKGROUND feed, not the description the drawer exists to show (US2) — the user must click inside the drawer before they can scroll it with the keyboard. Tab walks hidden feed buttons, and Enter can activate a control underneath the overlay (e.g. Dismiss the wrong offer). TailorCvModal's trap is inert until a click lands inside it, and closing it drops focus at \<body>.

**Proposal:** Extract the ApplyModal focus logic into one shared hook (e.g. useModalDialog(dialogRef): save/restore previous focus, focus the dialog element (tabIndex={-1}) or first control on open, two-end Tab trap, Escape) and use it in all five dialogs — this also fixes TailorCvModal's missing restore. In the same hook set document.body.style.overflow='hidden' while mounted so background scroll is impossible.

**Verifier notes:** All citations verified: both drawers declare aria-modal but never move focus in or trap Tab (OfferDetailDrawer.tsx:36-47/54, ApplicationDrawer.tsx:93-104/145), TailorCvModal has a trap but no initial focus and no focus-restore, ApplyModal/RestoreConfirmModal have the full pattern, and no scroll lock exists anywhere; the OfferCard title-button scenario (Enter opens drawer, keyboard scrolls the background feed, Tab/Enter reach hidden controls) is real. Downgraded from high to medium impact because mouse-driven use works fine today (drawer scrolls on hover, Escape/overlay-click close), so the win is keyboard flows plus consistency across five dialogs.

<a id="f50"></a>
### 50. Make ApplyModal a real \<form> so Enter saves 'Mark applied'

**Impact:** medium · **Effort:** S · **Found by:** a11y-keyboard (×1)

**Today (evidence):** ApplyModal.tsx:98-146 renders the date input and note textarea inside a plain div; Save is type="button" (line 142) with an onClick, so there is no implicit form submission. Focus lands in the date field on open (lines 53-57), and the tab order is date → note → Cancel → Save (lines 113-145). Contrast: every sub-form inside ApplicationDrawer already uses \<form onSubmit> (e.g. NotesPanel at ApplicationDrawer.tsx:404-431).

**User pain:** Marking an offer applied is the most frequent dialog in the app and usually needs zero edits (date defaults to today). Today the fastest keyboard path is open → Enter (nothing happens) → Tab, Tab, Tab (passing Cancel), Enter — or grab the mouse. Every application costs 4 extra keystrokes or a click, dozens of times a week.

**Proposal:** Wrap the fields in \<form onSubmit={(e)=>{e.preventDefault(); void handleSave()}}>, make Save type="submit" and keep Cancel type="button". Enter in the date field then saves immediately; add Ctrl+Enter submission on the note textarea for the with-note path.

**Verifier notes:** Verified line-by-line: ApplyModal.tsx has no form and no Enter handling (its keydown handler at 63-86 covers only Escape and Tab-trap), Save is type="button" at line 142, focus/tab order match the claim, and ApplicationDrawer.tsx indeed uses form onSubmit in all five sub-forms (404, 465, 646, 740, 783). The fix is small, standard, and hits the app's most frequent dialog; TailorCvModal.tsx clones the same buttons-only pattern and would benefit from the same treatment.

<a id="f51"></a>
### 51. Add a global :focus-visible style for buttons, links and selects

**Impact:** low (finder said medium; revised by verifier) · **Effort:** S · **Found by:** a11y-keyboard (×1)

**Today (evidence):** base.css defines .btn (lines 61-109) with hover states but no focus styling; the whole app has exactly four focus rules (grep over *.css): ThemeToggle.css:24-27, TailorCvModal.css:40-43, SettingsPage.css:54-57, ApplyModal.css:62-66. Everything else — every .btn, feed action, segmented control (OffersPage.tsx:301-334), offer-card title button (OfferCard.css:43-51), nav links — relies on the UA default ring, and ApplicationsPage.css:85-87 outright removes it (.board-card__main:focus-visible { outline: none; }) leaving only a 1px border-color change on the parent (lines 66-70).

**User pain:** Keyboard use (which findings 1-3 make the fast path) depends on always knowing where focus is. The UA default ring fights the rounded custom buttons and is invisible on the board cards where it was suppressed; the user tabs into the page and loses the cursor, having to press Tab repeatedly and watch for movement.

**Proposal:** Add one tokens-based rule in base.css — .btn:focus-visible, a:focus-visible, select:focus-visible, .segmented__btn:focus-visible { outline: 2px solid var(--c-brand-500); outline-offset: 2px; } — matching the existing ThemeToggle treatment, and replace ApplicationsPage.css:85-87's outline:none with the same ring (or delete the suppression).

**Verifier notes:** All cited code verified: only four positive focus rules exist app-wide, ApplicationsPage.css:85-87 does suppress the ring leaving a hover-identical parent tint, and no global :focus-visible rule exists; the ThemeToggle ring is the exact pattern to generalize. Confirmed, but impact revised to low standalone — Chrome's default ring is visible on the unstyled buttons, so the only hard defect is the board-card suppression; it rises to medium only if the companion keyboard-navigation findings (1-3) are adopted.

<a id="f52"></a>
### 52. Finish or drop the half-implemented ARIA tabs in ApplicationDrawer

**Impact:** low · **Effort:** S · **Found by:** a11y-keyboard (×1)

**Today (evidence):** ApplicationDrawer.tsx:235-253 renders role="tablist" and role="tab" with aria-selected, but there is no aria-controls, the panel div (line 255) has no role="tabpanel"/id, all six tabs stay in the Tab order (no roving tabindex), and no ArrowLeft/ArrowRight handling exists.

**User pain:** Half-ARIA is worse than none: the roles promise arrow-key switching that doesn't work, and a keyboard user moving from Stage select to the Notes tab must Tab through up to six tab buttons plus land in the wrong panel content order. Switching Timeline → Notes → Tasks is the core loop of updating an application after a recruiter call.

**Proposal:** Implement the standard pattern: roving tabindex (active tab tabindex=0, others -1), ArrowLeft/ArrowRight/Home/End on the tablist to move and select, id/aria-controls pairing with role="tabpanel" on the panel div. Alternatively (5-minute version) drop the tablist/tab roles and keep plain buttons; either resolves the broken semantics.

**Verifier notes:** All code claims verified in ApplicationDrawer.tsx: role="tablist"/"tab" with aria-selected (lines 235-241) but no aria-controls, no role="tabpanel"/id on the panel div (line 255), no roving tabindex (no tabIndex anywhere in frontend/src), and no arrow-key handling (only Escape, lines 98-104); nothing elsewhere in the frontend provides the capability. One small correction: the panel div directly follows the tablist in DOM order, so the pain is the missing tab-panel association and six extra Tab stops, not wrong content order; impact stays low but the S-effort fix (or dropping the roles) removes genuinely broken semantics.

<a id="f53"></a>
### 53. Fix light-mode contrast of the dismissed/rough tokens that carry real data

**Impact:** low · **Effort:** S · **Found by:** a11y-keyboard (×1)

**Today (evidence):** tokens.css:44-45 sets --c-dismissed-fg:#9ca3af on --c-dismissed-bg:#f1f2f5 — about 2.3:1 at 12px semibold (chips are --fs-xs, base.css:112-122). This pair is not only a 'dimmed' decoration: it renders the Withdrawn outcome on the Applications board (ApplicationsPage.tsx:19) and the closed-outcome chip in the drawer (ApplicationDrawer.tsx:213). Likewise --c-quality-rough:#9ca3af (tokens.css:69) on --c-paper-100 (base.css:175-178) marks salary-normalization honesty at ~2.3:1. The dark-theme equivalents (tokens.css:164-165) pass at ~4.7:1 — only light mode fails.

**User pain:** Scanning the Closed section to recall which applications were Withdrawn vs No-response, or checking whether a salary estimate is 'rough' before trusting the ≈PLN/mo figure, the user squints at near-invisible 12px grey-on-grey text in light mode — information chips that exist precisely to be read at a glance.

**Proposal:** Darken the two light-mode foregrounds in tokens.css: --c-dismissed-fg → #6b7280 (the already-used unavailable-fg value, ~4.7:1 on #f1f2f5) and --c-quality-rough → #6b7280. Two token edits; dark theme and all component CSS untouched.

**Verifier notes:** All cited code and ratios verified (~2.27:1 light, ~4.65:1 dark; the chips carry Withdrawn/closed-outcome and salary-quality data, and the drawer chip at ApplicationDrawer.tsx:213 shows every closed outcome, not just Withdrawn); the dark block's own "WCAG-AA verified" comment plus three-of-four passing outcome chips make this a consistency fix, not taste. One correction: the proposed #6b7280 yields ~4.3:1 on #f1f2f5 (not ~4.7:1) and still misses AA — use #5b6276 (existing --c-ink-500/--c-pending-fg) which clears 4.5:1 on both backgrounds.
