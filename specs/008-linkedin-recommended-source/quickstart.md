# Phase 1 — Quickstart & Validation: LinkedIn Recommended Jobs Source (008)

Run-and-look validation (Principle VII) per user story. The automated suite uses a **fake
`ILinkedInClient`** (offline, deterministic); this guide covers the **real** headed-browser path you
verify by hand.

## Prerequisites

- Backend + Postgres running: `./start.ps1` (docker-compose Postgres + `dotnet run`, host mode on
  `localhost:5180`) — or the "run app for import & enrich" host-mode flow.
- **Playwright Chromium provisioned once**: `playwright install chromium` (already required by the
  theprotocol scraper + tailored-CV PDF renderer).
- A real LinkedIn account you can log in to. The app runs on your machine (a desktop session must be
  available for the **headed** login window).
- `Sources:LinkedIn:UseBrowser` = `true` (default). Set `false` to force the offline/no-Chromium
  fallback (a clean `LoginNotCompleted`).

## Setup expectations

- On startup the seeder creates one **LinkedIn** source (`InteractiveBrowser`, `requiresLogin=true`,
  `enabled=true`) with `IncludeRecommended=true` and a starter saved search ("Senior .NET Software
  Engineer"). It appears in the Sources page alongside justjoin.it / theprotocol / nofluffjobs.
- The persistent login profile is created under
  `{LocalApplicationData}/JobOfferMatcher/browser-profiles/linkedin` on first login (gitignored,
  **not** in backups).

---

## US1 — Personalized recommendations into the feed (P1)

1. Open the app → **Offers**. Trigger a **manual scan** of the LinkedIn source (Sources page → scan,
   or the feed's scan control).
2. **First time**: a headed Chromium window opens at LinkedIn login. The web UI shows "A LinkedIn
   login window opened — finish signing in there." Log in yourself (credentials + any 2FA/checkpoint).
3. **Expected**: after login the scan continues; personalized recommended jobs appear in the feed with
   title, company, location, work mode (where shown), and a link back to the LinkedIn posting. Each
   offer shows the usual pending→produced enrichment/fit/affinity (drive `/enrich` to produce).
4. **Dedup**: run the scan again → no duplicate offers for unchanged postings (identity = LinkedIn job
   id). ✅ SC-002.
5. **No password stored**: confirm nothing anywhere holds your LinkedIn password (only the browser
   profile dir has session cookies). ✅ SC-001.

## US2 — Configure & run LinkedIn keyword searches (P2)

1. Sources page → edit the LinkedIn source → add a saved search (keywords / location / geoId /
   distance / recency), e.g. "Senior .NET Software Engineer", `geoId 90009828`, `distance 50`,
   `f_TPR r1296000`.
2. Run a manual scan. **Expected**: matching postings from that search appear in the feed. ✅ SC-006.
3. **Cross-pass dedup**: a job present in *both* the recommended feed and the search yields **one**
   offer, not two. ✅ US2 AC2.
4. **Independent passes**: with two saved searches, if one pass is blocked the scan is `Partial` and
   the other pass's offers still collect. ✅ US2 AC3.

## US3 — Log in once, stay logged in, degrade gracefully (P3)

1. **Session reuse**: after US1's login, run several manual scans → no re-login is requested. ✅ SC-003.
2. **Unattended, no session**: with the profile logged out/expired, let a **scheduled** scan run (or
   simulate one). **Expected**: it records `incomplete` with `incompleteReason=LoginNotCompleted`, does
   **not** open a window, does not hang the scheduler, and previously collected LinkedIn offers remain
   in the feed. ✅ US3 AC3, FR-011, SC-004.
3. **Attended re-login**: run a **manual** scan → the login window auto-launches; complete login → the
   scan resumes collecting. ✅ US3 AC2.
4. **Checkpoint/2FA**: if LinkedIn interrupts a manual scan with a checkpoint, resolve it in the headed
   window; the scan continues. On a scheduled scan the same situation records `LoginNotCompleted`. ✅

## Backup/restore (FR-012a)

1. Create a backup (`/api/backup`), then inspect the archive: it contains the DB dump + `cv-data/` but
   **not** the LinkedIn browser profile. ✅ FR-012a.
2. Restore into a fresh instance: the LinkedIn **source config** is restored; the login is **not** —
   the next manual scan prompts you to log in again. ✅ Clarification Q3.

---

## Automated checks (green before done — Principle VI)

- `dotnet test` — new unit/Application/Infrastructure tests (see `research.md` §Testing) all green,
  and the **regression contract** stays green: `NoAiDependencyTests` (no AI SDK added) and
  `BackupTablesCompletenessTests` (no new table).
- Frontend `npm test` — the LinkedIn source-editor fields + the scan-banner login states render.
- The real `PlaywrightLinkedInClient` is validated by the manual US1–US3 steps above, not the suite
  (it needs live auth). If a run can't launch the headed browser, say so explicitly (Principle VII) —
  don't claim the live path works.
