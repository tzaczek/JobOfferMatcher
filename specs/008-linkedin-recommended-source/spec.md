# Feature Specification: LinkedIn Recommended Jobs Source

**Feature Branch**: `008-linkedin-recommended-source`

**Created**: 2026-07-15

**Status**: Draft

**Input**: User description: "add new jobs source from linkedin. They should come from recommendations for me and my logged in account. An example address the should come from is: https://www.linkedin.com/jobs/collections/recommended/?currentJobId=4428922336&discover=recommended&discoveryOrigin=JOBS_HOME_JYMBII and https://www.linkedin.com/jobs/search-results/?currentJobId=4435527757&...&keywords=Senior%20.NET%20Software%20Engineer&...&geoId=90009828&distance=50.0&f_TPR=r1296000"

## Clarifications

### Session 2026-07-15

- Q: How should the tracker authenticate to LinkedIn to collect recommended jobs? → A: Interactive manual login in an app-controlled browser; the app persists and reuses that session for later scans and never stores the LinkedIn password (confirms the spec default; matches `IInteractiveBrowserSession` + Principle IV).
- Q: How should the manual LinkedIn login be triggered and surfaced? → A: Auto-launch a login browser window mid-scan when a login is needed; the (attended) scan pauses until the user completes login.
- Q: Should the persisted LinkedIn session be included in the backup/restore archive? → A: Exclude it — after a restore the user logs in once more; keeps live auth tokens out of the unencrypted 003 archive (Principle IV).
- Q: When a *scheduled/unattended* scan finds the LinkedIn session invalid, what happens (so the background scheduler never hangs)? → A: Only manual/attended scans auto-launch the login window and wait; a scheduled scan reuses the persisted session and, if invalid, records "login required" and skips LinkedIn without opening a window.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Personalized LinkedIn recommendations flow into my feed (Priority: P1)

As the single user of the tracker, I want the jobs LinkedIn recommends *to me* — the personalized
"Recommended" collection tied to my logged-in LinkedIn account (the "Jobs You May Be Interested In"
feed at `linkedin.com/jobs/collections/recommended`) — to be collected into my offers feed alongside
the offers from my other sources, so I triage everything in one place instead of browsing LinkedIn
separately.

**Why this priority**: This is the headline request. LinkedIn's recommendations are personalized to
the user and unavailable to any anonymous/public fetch, so they are a distinct, high-signal source
that no existing source covers. Delivering just this — recommended jobs appearing in the feed —
is a complete, valuable MVP on its own.

**Independent Test**: With the user logged in to LinkedIn, run a scan of the LinkedIn source and
confirm that personalized recommended jobs appear in the offers feed, are deduplicated against prior
scans, and are enriched/scored by the same pipeline as offers from other sources — without the user
having browsed LinkedIn manually.

**Acceptance Scenarios**:

1. **Given** a valid logged-in LinkedIn session and the LinkedIn source enabled, **When** a scan
   runs, **Then** the user's personalized recommended jobs are collected into the feed with title,
   company, location, work mode (where shown), and a link back to the original LinkedIn posting.
2. **Given** LinkedIn recommended jobs already collected in a previous scan, **When** the next scan
   runs, **Then** postings already seen are not duplicated (identity match on LinkedIn's own job id)
   and only genuinely new or changed postings are added/updated.
3. **Given** a newly collected LinkedIn offer, **When** the enrichment pipeline runs, **Then** the
   offer receives the same summary/key-skills, CV fit, and affinity treatment as offers from any
   other source, with no special-casing.
4. **Given** the LinkedIn source is disabled, **When** a scan runs, **Then** no LinkedIn collection
   is attempted and other sources are unaffected.

---

### User Story 2 - Configure and run LinkedIn keyword searches (Priority: P2)

As the user, I want to save a LinkedIn search — keywords, location, distance, and a recency window
(the form behind `linkedin.com/jobs/search-results/?keywords=Senior .NET Software Engineer&geoId=…&distance=50&f_TPR=…`)
— as a source and have its matching results collected into my feed, so targeted searches I care
about (e.g. "Senior .NET Software Engineer" near me, posted recently) are captured automatically,
not just whatever LinkedIn chooses to recommend.

**Why this priority**: The user provided a keyword-search URL as a second example, so it is in
scope, but it is secondary to the personalized recommendations that are the primary ask. It reuses
the same authenticated LinkedIn access and the same downstream pipeline established by US1.

**Independent Test**: Configure a LinkedIn keyword search (keywords + location + distance +
recency), run a scan, and confirm the matching postings appear in the feed, deduplicated and
enriched like every other offer.

**Acceptance Scenarios**:

1. **Given** a configured LinkedIn keyword search with keywords, location, distance, and recency,
   **When** a scan runs, **Then** postings matching that search are collected into the feed.
2. **Given** a job that appears in both the recommended feed and a keyword search, **When** both are
   collected, **Then** it results in a single offer (identity dedup), not two.
3. **Given** more than one LinkedIn source configuration (the recommended feed plus one or more saved
   searches), **When** a scan runs, **Then** each is collected independently and one failing does not
   abort the others.

---

### User Story 3 - Log in once, stay logged in, degrade gracefully (Priority: P3)

As the user, I want to log in to LinkedIn myself, once, and have the tracker reuse that session for
later scans without asking again. When the session eventually expires or LinkedIn interrupts with a
security checkpoint, a scan I run myself should open a login window so I can sign in and let the scan
continue, while an unattended scheduled scan should instead just tell me clearly that a login is
required — never crash, never silently return nothing, and never hang the background scheduler
waiting for me. My LinkedIn password must never be stored by the app.

**Why this priority**: This is the resilience/trust layer around US1–US2. The core value (recommended
jobs in the feed) can be demonstrated with a single fresh login (US1); this story makes repeated,
unattended use durable and keeps personal credentials safe (Principle IV). It is separable and
independently testable, so it is prioritized after the value-delivering slices.

**Independent Test**: Establish a session by logging in once; run several scans and confirm no
re-login is requested; then invalidate the session (log out / let it expire) and confirm the next
scan reports a clear "login required" outcome, preserves previously collected offers, and lets the
user re-authenticate.

**Acceptance Scenarios**:

1. **Given** the user has logged in to LinkedIn once via the app, **When** subsequent scans run,
   **Then** they reuse the existing session with no further credential entry, until it expires.
2. **Given** no valid LinkedIn session (first use, expiry, logout, or a LinkedIn security
   checkpoint), **When** the user runs a **manual** scan, **Then** a login window auto-launches
   mid-scan and, once the user completes login, the scan continues collecting — never crashing.
3. **Given** no valid LinkedIn session, **When** a **scheduled/unattended** scan runs, **Then** it
   records an incomplete "login required" outcome and skips LinkedIn without opening a window or
   blocking the scheduler; the user re-authenticates on their next manual scan, never entering their
   LinkedIn password into the app.
4. **Given** a session-invalid or blocked scan, **When** it completes, **Then** LinkedIn offers
   collected by earlier successful scans remain in the feed untouched.

---

### Edge Cases

- **Session expires or user is logged out mid-scan** → on a manual scan the login window
  auto-launches so the user can sign back in and the scan continues; on a scheduled scan it degrades
  to a clear "login required" incomplete outcome. Offers already collected in that scan are kept.
- **LinkedIn security checkpoint / 2FA / CAPTCHA appears** → treated as part of login: on a manual
  scan the user resolves it in the auto-launched controlled browser; on a scheduled scan it records
  "login required". The app never attempts to defeat or automate around it.
- **LinkedIn changes its page layout** → collection degrades to a Partial/LayoutChanged outcome
  (never a crash), consistent with existing sources.
- **Recommended feed is empty or returns fewer jobs than usual** → a valid scan with zero/few new
  offers, not an error.
- **A job appears in both the recommended feed and a keyword search** → a single offer via identity
  dedup on LinkedIn's job id.
- **A previously collected job is removed or expires on LinkedIn** → the existing offer record is
  retained (new-vs-seen is by identity existence, never by source dates — per 001).
- **A recommended job discloses no salary or has no readable body** → still collected; missing fields
  render as "not available" downstream, exactly like other sources.
- **Rate-limiting / temporary block from LinkedIn** → Partial/Incomplete outcome with polite backoff;
  no loss of prior offers.

## Requirements *(mandatory)*

### Functional Requirements

**Collection & pipeline integration**

- **FR-001**: System MUST provide a LinkedIn job source that collects the user's personalized
  "Recommended" jobs (the `jobs/collections/recommended` "Jobs You May Be Interested In" feed) using
  the user's authenticated LinkedIn session.
- **FR-002**: System MUST support collecting from a configured LinkedIn keyword search defined by
  keywords, location, distance, and a recency/date-posted window (the `jobs/search-results` form).
- **FR-003**: Collected LinkedIn offers MUST flow through the SAME downstream pipeline as existing
  sources — identity-based new-vs-seen deduplication, content-change detection, enrichment
  (summary/key-skills, CV fit), affinity, in-app offer body, tailored CV, and application tracking —
  with no LinkedIn-specific special-casing beyond collection.
- **FR-004**: Each collected LinkedIn offer MUST capture at minimum its title, company, location,
  work mode (remote / hybrid / on-site) where LinkedIn exposes it, a link back to the original
  LinkedIn posting, and the offer body (description/requirements) where available.
- **FR-005**: The system MUST use LinkedIn's own stable job identifier (the value LinkedIn exposes as
  the job id, e.g. `currentJobId`) as the offer identity, so the same posting is recognized across
  scans and across collection modes and is never duplicated.
- **FR-006**: The LinkedIn source MUST be enable/disable-able and appear in the sources list
  alongside other sources, participating in both scheduled and manually triggered scans. Scheduled
  (unattended) scans reuse the persisted session only — they MUST NOT open an interactive login
  window; if the session is invalid they record "login required" (FR-011) and skip LinkedIn without
  blocking the scheduler.
- **FR-007**: The system MUST support more than one LinkedIn collection *configuration* — the
  recommended feed plus one or more saved keyword searches — held **on the single LinkedIn source**,
  each collected as an **independent pass within one scan**. A posting appearing in several passes is
  still **one** offer (FR-005), and one pass failing MUST NOT abort the others.

**Authentication & privacy (Principle IV — NON-NEGOTIABLE)**

- **FR-008**: The system MUST collect LinkedIn offers only while the user's own LinkedIn account is
  logged in, and MUST NOT attempt to bypass, defeat, or automate around LinkedIn's login, 2FA, or
  security checkpoints.
- **FR-009**: The user MUST be able to complete the LinkedIn login manually themselves (entering
  their own credentials and passing any 2FA/checkpoint) in a browser session the app controls
  locally; the app MUST NOT store, transmit, or log the user's LinkedIn password.
- **FR-010**: A completed login MUST be reused across subsequent scans (persisted local session) so
  the user is not asked to log in on every scan, until that session expires or is revoked.
- **FR-011**: When no valid session exists (first use, expiry, logout, or a checkpoint) during a
  **manual/attended** scan, the system MUST auto-launch the interactive login window mid-scan and
  wait for the user to complete login, then continue collecting; during a **scheduled/unattended**
  scan it MUST instead record the scan as incomplete with a clear "login required" reason (no window,
  no waiting) — in neither case failing silently or crashing.
- **FR-012**: All LinkedIn session artifacts (cookies / browser profile) MUST remain local and MUST
  NOT be committed to source control nor included as plaintext credentials in exports (Principle IV).
- **FR-012a**: The persisted LinkedIn session MUST be excluded from the backup/restore archive; a
  restore does not carry the login forward, and the user re-authenticates once afterward. This keeps
  live LinkedIn auth tokens out of the (unencrypted, gitignored) 003 backup archive (Principle IV).

**Resilience & polite access (001 ADR-2)**

- **FR-013**: LinkedIn collection MUST pace its requests politely and bound how much it collects per
  scan (paged/limited, not unbounded infinite scrolling).
- **FR-014**: The system MUST tolerate LinkedIn blocks, rate-limiting, and layout changes by
  recording a Partial/Incomplete scan outcome (consistent with existing sources) rather than
  throwing an unhandled error.
- **FR-015**: A failed, blocked, or login-required LinkedIn scan MUST NOT remove or invalidate
  LinkedIn offers collected by prior successful scans.

**Configuration**

- **FR-016**: The user MUST be able to configure a keyword-search LinkedIn source's parameters
  (keywords, location, distance, recency); the recommended feed requires no query because it is
  personalized by LinkedIn.

### Key Entities *(include if feature involves data)*

- **LinkedIn Job Source**: The single configured source that collects from LinkedIn using the
  authenticated session. Its configuration holds the personalized *Recommended* feed (no query) plus
  zero or more *saved keyword searches* (keywords, location, distance, recency); **each is collected as
  an independent pass under the one source**, so a posting seen in several passes is one offer (FR-005).
  Can be enabled/disabled.
- **LinkedIn Session**: The user's authenticated LinkedIn login state, persisted locally and reused
  across scans. Holds no stored password; treated as sensitive local data, never committed.
- **Collected LinkedIn Offer**: A normalized offer produced from a LinkedIn posting — title, company,
  location, work mode, link, and body — whose identity is LinkedIn's own job id. Once collected it is
  indistinguishable downstream from an offer collected by any other source.
- **Saved Search Criteria**: For keyword-search sources, the user-editable keywords, location,
  distance, and recency window that define which LinkedIn postings are collected.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After the user logs in to LinkedIn once, a single scan surfaces their personalized
  LinkedIn recommended jobs in the offers feed, with no LinkedIn password stored anywhere by the app.
- **SC-002**: Re-running scans produces zero duplicate offers for LinkedIn postings already
  collected and unchanged.
- **SC-003**: After the first login, at least the next several scans run without asking the user to
  log in again (no re-authentication until the session actually expires or is revoked).
- **SC-004**: When the LinkedIn session is invalid or expired, no previously collected LinkedIn
  offers are lost (100% retained), and the user is either shown the login window (manual scan) or
  given a clear "login required" status (scheduled scan) — never a crash or silent empty result.
- **SC-005**: Every collected LinkedIn offer reaches the same enrichment and scoring states (summary,
  fit, affinity) available to offers from other sources — i.e. it is fully first-class in the feed.
- **SC-006**: The user can configure and run at least one keyword + location + recency LinkedIn
  search and see matching postings collected into the feed.
- **SC-007**: A blocked or rate-limited LinkedIn scan degrades to a Partial/Incomplete outcome
  without crashing and without discarding any prior LinkedIn offers.

## Assumptions

- **Login model (CONFIRMED — Clarifications 2026-07-15)**: Authentication is a *manual, interactive,
  user-driven* login in a browser the app controls locally; the session is persisted (cookies /
  browser profile) and reused across scans; the app never stores the user's LinkedIn password. This
  matches the existing `IInteractiveBrowserSession` port ("the user's Done/Continue click is the
  authoritative login-complete signal") and Principle IV. The rejected alternatives — app-stored
  credentials for automated headless login, and reusing/importing the user's everyday Chrome profile
  — are explicitly out of scope.
- **Scope**: Both example URL forms are in scope — the personalized *Recommended* feed (P1) and
  *keyword searches* (P2) — because the user supplied both. Other LinkedIn surfaces (company pages,
  "easy apply" submission, messaging, connections) are out of scope.
- **Recommendations taken as-is**: The personalized recommended feed is collected as LinkedIn ranks
  it; the app does not re-filter it by the category/level criteria used for other sources. Local
  ranking, enrichment, and fit/affinity scoring still apply after collection.
- **Bounded collection**: Each scan collects a bounded number of pages/results (polite pacing per 001
  ADR-2), not an exhaustive crawl of every recommended or matching posting.
- **No submission**: This feature only *collects and reads* LinkedIn postings. It does not apply to
  jobs, message recruiters, or take any action on the user's LinkedIn account.
- **Local-first, no external AI call**: Consistent with the locked stack, collection runs entirely on
  the user's machine and the backend makes no external AI call (enrichment stays with the existing
  local worker per 002). LinkedIn's Terms are the user's responsibility, consistent with the
  accepted source-access risk recorded for 001 (ADR-2).
- **Interactive environment available**: The app runs where the user can complete a headed,
  interactive login (this is the first activation of the previously deferred interactive-browser
  collection path).

## Dependencies

- The existing `IJobSource` collection port and the scan orchestrator/scheduler (offers are upserted
  incrementally; per-source outcomes are recorded).
- The existing interactive-browser login port (`IInteractiveBrowserSession`), until now a deferred
  stub, which this feature is the first to actually exercise.
- The existing enrichment / affinity / offer-body / tailored-CV / application-tracking pipeline,
  which LinkedIn offers join unchanged.
- A locally controllable browser capable of holding an authenticated session (the project already
  runs Chromium locally for the Cloudflare-protected theprotocol source and for CV PDF rendering).
- Backup/export coverage (Principle IX): new persisted *offer/source* data joins the existing
  backup-recoverable posture, while the LinkedIn *session* artifacts are deliberately excluded from
  the backup archive (FR-012a) and remain local-only and never committed (Principle IV).
