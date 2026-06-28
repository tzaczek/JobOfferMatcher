# Feature Specification: Job Offer Aggregation & CV-Based Matching

**Feature Branch**: `001-job-offer-matcher`

**Created**: 2026-06-28

**Status**: Draft

**Input**: User description: "I want you to create scripts which will go through job offers in https://justjoin.it/job-offers/all-locations/net?employment-type=b2b%2Cpermanent&experience-level=mid%2Csenior&workplace=hybrid%2Cremote&with-salary=yes&working-hours=full-time&orderBy=DESC&sortBy=salary and real browser so I will log into this portal manually as first step. Use my CVs to match the best job offers with best sallaries. This script should be reusable so it will run periodically at least 3 times every day, veirfy what was already suggested and show new offers. The project should be written in react and backand in .net, db as postgres. Keep it simple. Start from automatic searching for matching jobs, getting all information form offers and displaying them in UI. If not possible use real browser with manual login. I might use other websites for getting job offers as well. In the future there will be automatic CV aligning to job offers."

## Clarifications

### Session 2026-06-28

- Q: Application shape & stack — React/.NET/PostgreSQL (web app) vs the constitution's provisional XAML desktop + SQLite stack? → A: React + .NET + PostgreSQL web app, run locally for a single user; this supersedes the constitution's provisional XAML/SQLite stack (to be formally locked via a MINOR constitution amendment at `/speckit-plan`).
- Q: How should scheduled scans run, and what happens to a window missed while the machine is off? → A: A background scheduler runs scans independently of whether the UI is open; a missed window triggers a single catch-up scan at the next opportunity, then normal cadence resumes (missed windows are not replayed as back-to-back scans).
- Q: Should collection always drive a real browser, or prefer the lightest reliable method? → A: Lightest-reliable-first — fetch a source's public listing data directly when no login is required, and escalate to a real interactive browser (with manual login) only when a source requires authentication or blocks direct access.
- Q: How is the CV "fit indicator" represented? → A: A numeric fit score (0–100) per offer, shown alongside an explicit list of what matches and what does not (e.g., matched vs. missing skills, seniority fit/gap, work-mode/employment fit, salary vs. expectation); combined rank blends the score with normalized salary.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See all matching offers in one place (Priority: P1)

As a job seeker, I want the system to automatically collect job offers from my saved
job-board search and show them in one screen with all the important details, so I can
review opportunities without manually clicking through the portal page by page.

The user opens the application, starts a scan of the configured search (logging in
manually in the interactive browser session first if the source requires it), and the
system gathers every offer the search returns, extracts its details, and lists them in
the UI with a link back to the original posting.

**Why this priority**: This is the foundation and the explicitly requested starting
point ("Start from automatic searching for matching jobs, getting all information from
offers and displaying them in UI"). Without collection and display there is nothing to
match, deduplicate, or schedule. It delivers standalone value on its own: a single,
readable view of current offers.

**Independent Test**: Configure the justjoin.it search, run one scan, and confirm the
UI shows the offers from that search with title, company, salary, location, work mode,
seniority, skills, and a working link to each original posting — even if no matching,
scheduling, or deduplication exists yet.

**Acceptance Scenarios**:

1. **Given** a configured search and a fresh start, **When** the user triggers a scan,
   **Then** the offers returned by that search appear in the UI, each with its core
   details and a link to the original posting.
2. *(Deferred this iteration — the initial source justjoin.it is reachable without login; see Out of Scope.)*
   **Given** a source that requires the user to be logged in, **When** the user logs in
   manually in the interactive browser session and continues, **Then** the scan reuses
   that authenticated session and collects offers without asking for stored credentials.
3. **Given** an offer whose salary is not published, **When** it is collected, **Then**
   it still appears in the list with the salary marked as unknown rather than being
   dropped.

---

### User Story 2 - Only show me what's new across scheduled scans (Priority: P2)

As a job seeker who checks several times a day, I want the system to run on a recurring
schedule and clearly tell me which offers are new since I last looked, so I never re-read
the same posting and can focus only on what changed.

The system scans automatically at least three times per day, remembers every offer it
has already surfaced, and on each run flags which offers are new, which were updated
(e.g., reposted or salary changed), and which have disappeared.

**Why this priority**: The user explicitly wants reusable, periodic runs (≥3×/day) that
"verify what was already suggested and show new offers." This turns a one-off list into
an ongoing feed and is the second-most valuable slice, but it depends on collection (P1)
existing first.

**Independent Test**: Run a scan, note the offers, run a second scan, and confirm offers
seen in the first run are marked as already-seen while only genuinely new postings are
flagged as new; confirm the schedule fires automatically at least three times in a day.

**Acceptance Scenarios**:

1. **Given** an offer that was surfaced in a previous scan, **When** a later scan runs,
   **Then** that offer is shown as already-seen and is not presented as new again.
2. **Given** a new posting that did not exist in any previous scan, **When** a scan runs,
   **Then** it is flagged as new.
3. **Given** a configured schedule, **When** a day passes, **Then** scans have run
   automatically at least three times without manual intervention beyond any login.
4. **Given** a previously-seen offer whose salary or content changed, **When** a scan
   runs, **Then** it is flagged as updated rather than as a brand-new offer.
5. **Given** a scan that finds no new offers, **When** it completes, **Then** the UI
   clearly states there are no new offers (no error or broken empty state).

---

### User Story 3 - Rank the best-fit, best-paid offers using my CV (Priority: P3)

As a job seeker, I want each offer scored against my CV and the list ordered so the
best-matching, highest-paying offers come first, so I spend my time on the roles I am
most likely to want and qualify for.

The system derives a profile from the user's CV(s) — skills, seniority, salary
expectation, work-mode and employment preferences — scores every offer's fit, and ranks
offers by a combination of fit and salary, while still letting the user re-sort.

**Why this priority**: Matching is central to the request ("Use my CVs to match the best
job offers with best salaries") but is only useful once offers are being collected (P1)
and the user is being shown a manageable, new-only feed (P2). It sharpens, rather than
enables, the core value.

**Independent Test**: Provide a CV, run a scan, and confirm each offer shows a fit
indicator, the list is ordered by fit and salary, and the user can re-sort by salary,
fit, or recency.

**Acceptance Scenarios**:

1. **Given** a CV has been provided, **When** offers are collected, **Then** each offer
   shows a fit indicator relative to the CV and the matched reasons (e.g., shared skills,
   salary vs. expectation).
2. **Given** scored offers, **When** the user opens the list, **Then** offers are ordered
   to surface the best-matching, best-paid ones first.
3. **Given** a scored list, **When** the user chooses a different sort (salary, fit,
   recency), **Then** the list re-orders accordingly.
4. **Given** no readable CV is available, **When** offers are collected, **Then** the
   list still ranks offers by salary and recency (matching degrades gracefully).

---

### User Story 4 - Add more job sources over time (Priority: P4)

As a job seeker, I want to add other job websites as sources without disturbing the
existing setup, so all my opportunities land in one unified, deduplicated feed.

**Why this priority**: The user states they "might use other websites for getting job
offers as well." It is valuable for coverage but not required for the first working
product; the system should be built so this is additive, not a rewrite.

**Independent Test**: With justjoin.it already working, configure a second source and
confirm its offers appear in the same unified list, ranked and deduplicated alongside the
existing source, without changing how the first source behaves.

**Acceptance Scenarios**:

1. **Given** a working primary source, **When** a second source is configured, **Then**
   its offers appear in the same unified, ranked feed.
2. **Given** the same role posted on two sources, **When** both are scanned, **Then** the
   system avoids presenting it as two separate new offers.

---

### Edge Cases

- **Login required or session expires mid-scan**: prompt the user to log in manually and
  resume; do not store credentials; mark the run incomplete if login is not completed.
- **Anti-bot challenge / CAPTCHA**: surface it to the user and pause that scan rather than
  failing silently or guessing data.
- **Source layout/structure changes** so details can't be extracted: save what was
  collected, mark the run as partial with a reason, and alert the user.
- **Network failure or interruption mid-scan**: persist partial results and flag the run
  as incomplete.
- **Offer disappears between scans** (filled/withdrawn): mark it no-longer-available and
  retain its history rather than deleting it.
- **Offer re-posted or salary changed**: treat as an update to the existing offer, not a
  brand-new one.
- **Salary hidden, range-only, or in a different currency / B2B-vs-permanent basis**:
  store what is available, mark the rest unknown, and normalize for comparison on a
  best-effort basis.
- **Duplicate offer across multiple sources**: collapse into one entry where it can be
  recognized.
- **No new offers in a run**: communicate "no new offers," not an error.
- **Multiple CVs provided**: use them to build the candidate profile and score each offer
  against the best-fitting profile.
- **The user dismisses an offer**: it must not reappear as new in later scans.

## Requirements *(mandatory)*

### Functional Requirements

**Collection & Sources**

- **FR-001**: System MUST collect job offers from a configured job-board search defined by
  saved filter criteria; the initial source is justjoin.it using the user's filter (.NET,
  mid/senior, B2B + permanent, hybrid + remote, salary published, full-time, ordered by
  salary descending).
- **FR-002**: System MUST allow a source's search/filter criteria to be configured and
  edited without changing code.
- **FR-003**: System MUST be structured so additional job-offer sources can be added over
  time without redesigning existing collection, matching, or display behavior.

**Authentication & Access**

- **FR-004**: System MUST let the user authenticate to a source manually in an interactive
  browser session at the start of a scan and reuse that authenticated session for
  collection. *(Delivery deferred this iteration — the initial source justjoin.it is
  reachable without login; the pluggable source port and escalation trigger are built now,
  the real browser login adapter is scheduled later. See Out of Scope.)*
- **FR-005**: System MUST NOT store the user's job-portal login credentials.
- **FR-006**: System MUST proceed without login when a source can be scanned without it,
  and MUST prompt for manual login (then resume) only when login or a valid session is
  required. *(The login-escalation half is deferred this iteration — see FR-004 and Out of
  Scope; the no-login path is in scope and is the path the initial source uses.)*
- **FR-007**: System MUST access sources politely — paced and rate-limited (target ≤ ~1
  request/second per source, with backoff on errors) and caching where possible — to keep
  load low. Where a source's robots.txt or terms restrict automated access, this is a
  **documented, deliberately accepted risk** for personal, single-user, low-volume,
  non-redistributing use (see plan ADR-2), not an assertion of guaranteed permission; the
  user is advised to review each source's terms, and the system escalates to the
  manual-login browser path on a block (FR-040).
- **FR-040**: System MUST prefer the lightest reliable access method for each scan —
  fetching a source's public listing data directly when no login is required — and MUST
  escalate to a real interactive browser session (prompting for manual login) only when a
  source requires authentication or blocks direct access. *(The escalation browser adapter
  is deferred this iteration — the port and the on-block trigger are built now; see Out of
  Scope.)*

**Offer Extraction**

- **FR-008**: System MUST capture, for each offer, all available core details: title,
  company, salary (amount or range, currency, period, and B2B vs. permanent basis),
  location, work mode (remote/hybrid/onsite), employment type, seniority level, required
  and nice-to-have skills/technologies, job description, publication date, and the
  canonical link to the original offer.
- **FR-009**: System MUST record when each offer was first seen and last seen.
- **FR-010**: System MUST keep offers with missing or partial fields, marking absent
  fields as unknown instead of discarding the offer.

**Deduplication & New-Offer Detection**

- **FR-011**: System MUST assign each offer a stable identity per source so the same offer
  is recognized across repeated scans.
- **FR-012**: System MUST distinguish, on every scan, offers that are new (never surfaced
  before) from those already surfaced.
- **FR-013**: System MUST NOT present an already-surfaced, unchanged offer as new again.
- **FR-014**: System MUST detect when a previously-seen offer's key details changed and
  flag it as updated rather than new.
- **FR-015**: System MUST detect when a previously-seen offer is no longer available and
  mark it as such while retaining its history.
- **FR-016**: System SHOULD recognize the same role appearing across multiple sources and
  avoid presenting it as multiple separate new offers.

**Scheduling & Runs**

- **FR-017**: System MUST run scans automatically on a recurring schedule, at least three
  times per day, without manual intervention beyond any required login. The schedule MUST
  be driven by a background scheduler that runs independently of whether the user interface
  is open.
- **FR-018**: System MUST allow the user to trigger a scan on demand.
- **FR-019**: System MUST allow the recurring schedule to be configured (frequency/times).
- **FR-039**: When a scheduled scan window is missed because the machine was off or asleep,
  System MUST run a single catch-up scan at the next opportunity and then resume the normal
  cadence; it MUST NOT replay every missed window as separate back-to-back scans.
- **FR-020**: System MUST record each scan run with its timestamp, trigger type (manual or
  scheduled), source(s), result counts (collected / new / updated / unavailable / failed),
  and outcome status (complete / partial / failed).

**CV Matching & Ranking**

- **FR-021**: System MUST derive a candidate profile from the user's CV(s): skills,
  experience/seniority, salary expectation, and work-mode/employment preferences.
- **FR-022**: System MUST support more than one CV.
- **FR-023**: System MUST compute a numeric fit score (0–100) for each offer against the
  candidate profile, considering at least skill overlap, seniority fit, work-mode/employment
  fit, and salary versus expectation.
- **FR-024**: System MUST order offers to surface the best-matching, best-paid offers
  first, and MUST let the user re-sort (e.g., by salary, by fit, by recency).
- **FR-025**: System MUST show, for each offer, both what matches and what does not — e.g.,
  matched vs. missing skills, seniority fit/gap, work-mode/employment fit, and salary vs.
  expectation — so the user can judge relevance and gaps quickly.
- **FR-026**: When no readable CV is available, System MUST still rank offers by salary and
  recency (graceful degradation).

**User Interface & Interaction**

- **FR-027**: System MUST display collected offers in a UI list showing their core details
  and a link to the original offer.
- **FR-028**: System MUST visually distinguish new offers from previously-seen ones.
- **FR-029**: Users MUST be able to open the original posting from the UI.
- **FR-030**: Users MUST be able to filter and sort the displayed offers (e.g., new vs.
  all, by salary, by fit, by source, by work mode).
- **FR-031**: Users MUST be able to mark an offer's personal status (e.g., interested,
  dismissed, viewed) and have that status persist across scans, so dismissed offers do not
  reappear as new.
- **FR-032**: When a scan produces no new offers, the UI MUST clearly indicate this rather
  than showing an error or broken empty state.

**Data Integrity, Persistence & Recoverability**

- **FR-033**: System MUST persist offers, scan history, fit results, and user-assigned
  statuses across runs and restarts.
- **FR-034**: System MUST retain offer/suggestion history append-only enough to answer
  "when did this offer first appear and when was it suggested."
- **FR-035**: System MUST never fabricate, placeholder, or demo offer data as if real;
  only genuinely collected offers are stored as offers.
- **FR-036**: If a scan is interrupted (login expiry, anti-bot challenge, layout change,
  network failure), System MUST save partial results, mark the run incomplete with a
  reason, and surface it to the user.
- **FR-037**: System MUST let the user export collected offers and their statuses to a
  portable, human-readable format.
- **FR-038**: System MUST keep the user's personal data (CVs, salary expectations,
  collected offers) local and private; any future send of this data to an external service
  MUST be opt-in and off by default.

### Key Entities *(include if feature involves data)*

- **Job Offer**: a single open position collected from a source. Key attributes: source,
  stable per-source identity, title, company, salary (amount/range, currency, period,
  B2B/permanent basis), location, work mode, employment type, seniority, required and
  nice-to-have skills, description, publication date, original link, first-seen and
  last-seen timestamps, and availability status.
- **Job Source**: a configured place to collect offers from. Key attributes: name, search
  /filter criteria, whether login is required, and enabled/disabled state.
- **Candidate Profile (from CV)**: the user's matchable profile derived from one or more
  CVs. Key attributes: skills, experience/seniority, salary expectation, preferred work
  modes/locations, and employment-type preference.
- **Offer Match / Recommendation**: the relationship between an offer and the candidate
  profile. Key attributes: numeric fit score (0–100), salary comparison, combined rank,
  matched and unmatched (gap) reasons, and the user-assigned status (new / viewed /
  interested / dismissed).
- **Scan Run**: a single execution of collection. Key attributes: timestamp, trigger type,
  source(s) covered, result counts (collected / new / updated / unavailable / failed), and
  outcome status.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a scan completes, the user can see that scan's new offers in the UI
  within 2 minutes.
- **SC-002**: Across repeated scans, an unchanged offer is presented as "new" at most once
  — zero duplicate re-suggestions.
- **SC-003**: At least 95% of collected offers include the fields needed to evaluate them
  (title, company, working link) and include salary whenever the source publishes it.
- **SC-004**: Scans run automatically at least three times per day for seven consecutive
  days with no manual intervention beyond any required login.
- **SC-005**: For a typical day's results, the user can identify the new offers worth
  applying to in under 5 minutes, because new offers are clearly flagged and pre-ranked by
  salary and fit.
- **SC-006**: In at least 80% of scans that return new offers, at least one of the top 10
  ranked offers is one the user judges relevant to their CV.
- **SC-007**: The user can export the full set of collected offers and their statuses at
  any time and read it outside the application.
- **SC-008**: A second job source can be configured and start contributing offers to the
  unified feed without changing how the existing source behaves.

## Assumptions

- **Single user, local-first**: this is a personal, single-user tool; the user's data
  (CVs, salary expectations, collected offers) stays on the user's machine and is private
  by default, consistent with the project constitution.
- **Manual login is occasional**: when a source needs authentication, the user logs in
  manually in the interactive browser session at most once per session/expiry, and that
  session is reused for the scan; credentials are never stored.
- **Primary source first**: justjoin.it is the first source, and its initial search is the
  filter URL the user provided (.NET, mid/senior, B2B + permanent, hybrid + remote, salary
  published, full-time, salary descending). justjoin.it offer listings are assumed to be
  viewable; manual login is supported as a fallback if/when access requires it.
- **Salary comparison is best-effort**: salaries are normalized for ranking on a
  best-effort basis (currency, monthly basis, B2B vs. permanent); exact normalization is
  not guaranteed when sources publish incomplete figures.
- **CV is user-supplied**: the user supplies one or more CV files (PDFs already present in
  the project), from which the candidate profile is derived; multiple CVs are allowed.
- **Default schedule**: "at least three times per day" defaults to roughly morning, midday,
  and evening, and is configurable.
- **"New offers" means an in-app feed**: surfacing new offers means clearly flagging them
  in the UI list; push/email notifications are out of scope for this iteration.
- **Tech stack confirmed (Session 2026-06-28)**: the application is a local-first,
  single-user **web app** built with a **React** front end, a **.NET** back end, and a
  **PostgreSQL** database (per clarification). This supersedes the constitution's
  provisional XAML/SQLite stack, which is to be formally locked via a MINOR constitution
  amendment at the first `/speckit-plan`. The stack remains an implementation choice, not
  a functional requirement of this specification.
- **Polite, low-volume collection (accepted-risk)**: collection paces politely (≤ ~1
  req/s) and keeps volume low. Some sources' robots.txt or terms may restrict automated
  access; for this personal, single-user, local, non-redistributing tool that is a
  documented, deliberately accepted risk (plan ADR-2) — the user is advised to review each
  source's terms. It is **not** asserted as guaranteed-lawful.

## Out of Scope (this iteration)

- Automatic CV alignment/tailoring to individual offers (explicitly a future feature).
- Automatically applying to jobs or contacting recruiters.
- Push, email, or mobile notifications.
- Multi-user accounts, sharing, or hosting the tool as a service for others.
- Guaranteed-exact salary normalization across all currencies and contract bases.
- **Interactive manual-login / authenticated-session collection** (the delivery of FR-004
  and the login-escalation half of FR-006 / FR-040). The initial source (justjoin.it) is
  reachable without login, so the pluggable `IJobSource` port and the on-block escalation
  trigger are built this iteration, but the real browser login adapter (and its
  scan-session continue/cancel handshake) is deferred until a configured source actually
  requires it (Principle X). Credentials are never stored (FR-005) regardless.
