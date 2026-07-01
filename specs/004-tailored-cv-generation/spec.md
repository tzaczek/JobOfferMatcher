# Feature Specification: Tailored CV per Job Offer

**Feature Branch**: `004-tailored-cv-generation` (spec directory; no git branch created — repo has no `before_specify` hook)

**Created**: 2026-06-30

**Status**: Draft

**Input**: User description: "I want cv to be custom for some job offers. In folder cv_versions is file Notes.md which describes how to create it. Add new button with job offer which opens modal where specific job offer skills are pointed out which will be used to create custom cv. There should also be a text box which shows exactly prompt which model will use. I want to have an option to modify it, regenerate cv, download it. It should be stored, available from job offer level and dedicated page."

## Clarifications

### Session 2026-06-30

- Q: What should the tailored CV draw its actual content (employers, dates, skills, summary) from? → A: The CV document already uploaded/known in feature 002 — the single source of truth; the worker reads that CV. The `cv_versions` files supply only the format/layout recipe, not content.
- Q: In what format should a finished tailored CV be downloadable? → A: PDF only — a polished, print-ready A4 PDF; the underlying HTML is an internal rendering intermediate, not a separate download.
- Q: How many tailored CVs should be retained per offer? → A: One per offer (latest-only) — regenerating overwrites the prior tailored CV; no per-offer history or named variants in this version.
- Q: What does the editable "prompt" text box actually contain (FR-003 "exactly the prompt the model will use")? → A: The editable box holds the tailoring instructions plus the emphasised-skills selection; the source CV content is shown as a visible, previewable input attached to the request (nothing hidden), not inlined into the editable box.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Generate a CV tailored to one specific offer (Priority: P1)

As a job seeker, when I find an offer worth applying to, I want to generate a CV that is tailored to **that** offer — emphasising the skills the posting cares about — so my application reflects the role instead of using one generic CV for everything.

**Why this priority**: This is the core value of the feature and the smallest slice that delivers it. From a single offer the user can produce a tailored CV and see the result. Everything else (editing the prompt, downloading, storage, the dedicated page) builds on having this generation loop working.

**Independent Test**: Open an offer, use the new "tailored CV" action, confirm the modal highlights the offer's relevant skills and shows the exact prompt that will be used, trigger generation, and confirm a CV is produced that visibly emphasises those skills while containing only information already present in the user's existing CV.

**Acceptance Scenarios**:

1. **Given** an offer with extracted key skills, **When** I open the tailored-CV action for it, **Then** I see a workspace that points out the offer's relevant skills (those that will be emphasised) and shows the exact prompt that will be used.
2. **Given** the open workspace, **When** I trigger generation, **Then** a CV tailored to the offer is produced and shown to me, emphasising the offer's skills.
3. **Given** a produced tailored CV, **When** I compare it against my existing CV, **Then** it contains no employers, dates, roles, qualifications, or skills that are not already present in my source CV (re-emphasised and reordered, never fabricated).
4. **Given** generation has been requested but the worker has not yet produced the CV, **When** I view the offer or workspace, **Then** the tailored CV shows as "pending" rather than blank or a placeholder.

---

### User Story 2 - See, edit, and regenerate from the exact prompt (Priority: P1)

As a job seeker who knows my own story best, I want to see the **exact** prompt the model will use, edit it freely, and regenerate, so I stay in control of how my CV is tailored rather than trusting a hidden, fixed prompt.

**Why this priority**: Transparency and control over the prompt is an explicit, headline requirement. It is what makes the tailoring trustworthy and reusable, and it is independently valuable once the generation loop (US1) exists.

**Independent Test**: In the workspace, confirm the full prompt is shown in an editable text box, change its wording (e.g. add an instruction to foreground leadership experience), regenerate, and confirm the new CV reflects the edited instruction; confirm the prompt shown is identical to the prompt actually used.

**Acceptance Scenarios**:

1. **Given** the workspace, **When** I read the prompt, **Then** the complete prompt that will be sent for generation is visible to me with no hidden or omitted parts.
2. **Given** the visible prompt, **When** I edit its text and regenerate, **Then** a new CV is produced using my edited prompt and replaces the previously generated one as the current version.
3. **Given** I toggle which of the offer's skills to emphasise, **When** the prompt updates, **Then** the change is reflected in the visible prompt text before I generate.
4. **Given** a prompt I have edited, **When** the CV is generated, **Then** the prompt stored alongside the CV is exactly the prompt that was used.

---

### User Story 3 - Download the tailored CV as a polished document (Priority: P2)

As a job seeker, I want to download the tailored CV as a polished, print-ready document, so I can attach it to an application or send it to a recruiter.

**Why this priority**: The tailored CV is only useful once the user can get it out of the app in a sendable form. It depends on a CV having been generated (US1) but is otherwise an independent, demonstrable slice.

**Independent Test**: From a produced tailored CV, download it and confirm the downloaded document is a polished, A4 print-correct CV (correct page count, intact layout) that matches the on-screen result.

**Acceptance Scenarios**:

1. **Given** a produced tailored CV, **When** I download it, **Then** I receive a polished, print-ready document that preserves the intended layout.
2. **Given** a tailored CV whose generation is still pending or has failed, **When** I view the download action, **Then** download is unavailable until a CV has been produced.

---

### User Story 4 - Store tailored CVs and reach them from the offer and a dedicated page (Priority: P2)

As a job seeker applying to many roles over time, I want each tailored CV stored and reachable both from its job offer and from one dedicated page that lists them all, so I can find, reopen, regenerate, or re-download any of them later without starting over.

**Why this priority**: Persistence and discoverability turn a one-shot generator into a durable part of the workflow. It depends on generation (US1) but is an independent slice that can be demonstrated on its own.

**Independent Test**: Generate tailored CVs for two different offers, confirm each offer indicates that a tailored CV exists and lets the user reopen it, and confirm a dedicated page lists both tailored CVs, each linking back to its offer and offering view / regenerate / download.

**Acceptance Scenarios**:

1. **Given** an offer for which I generated a tailored CV, **When** I view that offer, **Then** it indicates a tailored CV exists and lets me reopen it (view, re-edit the prompt, regenerate, download).
2. **Given** several tailored CVs across different offers, **When** I open the dedicated tailored-CV page, **Then** I see all of them listed, each linking back to its source offer with access to view, regenerate, and download.
3. **Given** a stored tailored CV, **When** I reopen it, **Then** I see the generated CV together with the exact prompt and emphasised skills that produced it.
4. **Given** an offer that later becomes unavailable/delisted, **When** I open the dedicated page, **Then** its previously generated tailored CV is still listed and accessible.

### Edge Cases

- **No CV on file**: if the user has no existing/master CV for the app to draw from, tailored generation cannot run; the workspace shows a clear state directing the user to add a CV first, rather than fabricating content.
- **Offer not yet enriched**: if an offer has no extracted key skills yet, the workspace still opens — emphasised skills show as pending/empty, and the user can still write or edit the prompt manually and generate.
- **Worker has not run**: a requested generation stays "pending" indefinitely; the rest of the app (feed, enrichment, fit, backup/restore) continues to work normally.
- **Generation fails**: when the worker cannot produce a valid CV after the bounded retry limit, the item is marked "failed" (a distinct terminal state from "pending"), shown as an error state, never left permanently pending and never silently producing an empty CV.
- **Regenerate replaces the current CV**: regenerating supersedes the prior generated document for that offer; the current (latest) version is what the user sees and downloads.
- **Download before produced**: downloading is unavailable while a CV is pending or failed.
- **Very long skill list**: the user can deselect skills so only the most relevant ones are emphasised, keeping the CV focused and within a sensible length.
- **Offer deleted/unavailable after generation**: the tailored CV persists and stays reachable from the dedicated page even if its source offer is no longer available.
- **No fabrication under prompt edits**: the only content source is the user's real CV; the generation instructions forbid inventing experience, and edits to the prompt do not introduce new factual claims about the user's history.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to initiate creation of a tailored CV from an individual offer via a dedicated action at the offer level; tailored-CV generation is **opt-in per offer** (the system does not auto-generate tailored CVs for every offer).
- **FR-002**: The tailored-CV workspace MUST point out the offer's relevant skills — the skills that will be emphasised in the CV — drawn from the offer's extracted key skills and matched/missing breakdown, and MUST let the user include or exclude specific skills from emphasis.
- **FR-003**: The workspace MUST display the **exact, complete** request that will drive generation, with no hidden or omitted parts: the editable prompt text box holds the full tailoring instructions and the emphasised-skills selection, and the source CV content is shown as a visible, previewable input attached to the request. The CV content is NOT inlined into the editable box and is NEVER a hidden input.
- **FR-004**: Users MUST be able to edit the prompt text before generating, and the visible prompt MUST update to reflect changes to the emphasised-skills selection. (Editing applies to the instruction text; the attached CV content is read-only here — it is changed via the feature-002 CV upload, not this box.)
- **FR-005**: The system MUST generate the tailored CV using the local Claude Code worker under the user's own plan; the application backend MUST NOT call any paid/external AI API and MUST NOT transmit CV or offer data to an external AI service (consistent with the 002 worker model, FR-012/SC-005, and Constitution Principle IV).
- **FR-006**: A generated tailored CV MUST be derived **only** from the user's uploaded CV (the CV document managed by feature 002) — re-emphasising, reordering, and rephrasing it toward the offer — and MUST NOT fabricate employers, dates, role titles, qualifications, or skills that are not present in that CV (Constitution Principle III; the source CV's "no information invented" rule).
- **FR-007**: Generation MUST follow the documented CV format/layout recipe maintained in `cv_versions` — which supplies **layout only, not content** (the print-correct A4 layout that renders to a downloadable document) — so output is consistent and sendable.
- **FR-008**: Users MUST be able to regenerate the CV — re-running with the current (possibly edited) prompt and skill selection — and see the updated result, which supersedes the prior generated document for that offer.
- **FR-009**: Users MUST be able to download a produced tailored CV as a polished, print-ready **PDF** (A4) that preserves the intended layout; the HTML used to render it is an internal intermediate, not a separate download. Download MUST be unavailable while the CV is pending or failed.
- **FR-010**: The system MUST persist each tailored CV tied to its source offer, storing the generated document, the exact prompt used, the emphasised-skills selection, and a generated-at timestamp.
- **FR-011**: From an offer that has a tailored CV, users MUST be able to see that one exists and reopen it to view, re-edit the prompt, regenerate, and download.
- **FR-012**: The system MUST provide a dedicated page that lists all tailored CVs across offers, each linking back to its source offer and offering view, regenerate, and download.
- **FR-013**: The system MUST pre-populate a default prompt from the offer's emphasised skills, the CV-creation recipe, and the user's uploaded CV (feature 002), so a user can generate without writing a prompt from scratch.
- **FR-014**: A requested-but-not-yet-produced tailored CV MUST be shown as "pending", and a generation that cannot produce a valid result after a bounded retry limit MUST be shown as "failed" (a terminal state distinct from "pending"), reusing the 002 worker pending/failed semantics.
- **FR-015**: The system MUST expose a **local-only** mechanism for the worker to (a) retrieve pending tailored-CV requests together with the inputs needed to produce each one (the prompt and the source CV content), and (b) write generated CVs back into storage.
- **FR-016**: Tailored CVs, the prompts used, and the generated documents MUST be stored **locally** and MUST NOT be committed to source control; generated CV files MUST live within the gitignored CV data area (Constitution Principle IV).
- **FR-017**: Tailored-CV documents and their records MUST be included in the existing on-demand backup and restore scope (feature 003), so they are recoverable alongside the database and the existing CV files.
- **FR-018**: A tailored CV MUST persist and remain accessible from the dedicated page even if its source offer later becomes unavailable/delisted or is removed from the feed.
- **FR-019**: The system MUST keep existing behaviour unchanged — offer collection, deduplication, availability/reconciliation, enrichment, fit scoring, salary normalisation, export, and backup/restore — adding tailored-CV generation alongside them without altering them.

### Key Entities *(include if feature involves data)*

- **Tailored CV**: per offer (opt-in) — the generated CV document, the exact prompt used to produce it, the emphasised-skills selection, a reference to the source offer, a generated-at timestamp, and a processing state (pending / produced / failed) with an attempt counter driving the bounded-retry-then-failed behaviour. Latest-version-per-offer (regeneration replaces the prior document).
- **Tailored-CV prompt**: the editable instruction text (plus the emphasised-skills selection) shown to and modifiable by the user, assembled by default from the CV-creation recipe and the offer's emphasised skills; the source CV content is attached to the request as a visible, read-only input rather than inlined into this editable text. The prompt is stored with the generated CV exactly as used.
- **Source CV content (existing)**: the user's uploaded CV from feature 002 — the read-only source of truth the tailored CV draws from; this feature never adds information not present here, and never edits this CV.
- **Emphasised-skills selection**: the subset of the offer's skills the user chose to foreground, derived from the offer's extracted key skills and matched/missing breakdown.
- **Tailored-CV generation work item**: a pending unit of worker work — an offer plus the prompt and source content needed to generate its tailored CV.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From any offer, a user can produce a tailored CV through a single, discoverable action followed by one generation step (open → generate), without leaving the offer's context.
- **SC-002**: 100% of generated tailored CVs contain only information present in the user's uploaded CV (feature 002) — no fabricated employers, dates, roles, qualifications, or skills (verifiable by comparison against that CV).
- **SC-003**: 100% of the time, what the user sees — the editable prompt together with the visibly-attached emphasised-skills and CV inputs — is exactly what drives generation; the worker receives no hidden additions or modifications.
- **SC-004**: After a user edits the prompt and regenerates, the resulting CV reflects the edit, and the previously generated document is no longer presented as the current version.
- **SC-005**: A produced tailored CV can be downloaded as a print-correct **PDF** (A4, correct page count, intact layout) that matches the on-screen result.
- **SC-006**: 100% of generated tailored CVs are retrievable later both from their source offer and from the dedicated tailored-CV page.
- **SC-007**: 0 CV or offer records are transmitted from the application backend to any external AI service — all tailored-CV generation runs under the local worker (mirrors 002 SC-005).
- **SC-008**: Tailored CVs and their prompts survive a backup → restore cycle intact (covered by feature 003's backup/restore).

## Assumptions

- **Generation engine**: tailored-CV generation reuses the 002 "Claude-as-worker" model — the local Claude Code worker under the user's own plan generates the CV; the backend owns the queue and writes-back but makes no external AI call (Constitution Principle IV; supersedes no prior decision, extends 002).
- **Output format** *(confirmed — Clarifications 2026-06-30)*: the download deliverable is a polished, print-ready PDF (A4) rendered from the documented `cv_versions` layout; the HTML is an internal rendering intermediate and is **not** a separate download. The `cv_versions/Notes.md` recipe provides the format/layout guidance (two-column A4 → PDF), not the content.
- **Content source** *(confirmed — Clarifications 2026-06-30)*: the tailored CV draws its content from the user's **uploaded CV managed by feature 002** (the single source of truth); the worker reads that CV. The `cv_versions` files contribute layout only. This feature does not edit or replace the uploaded CV; it produces tailored renderings of it.
- **Versioning** *(confirmed — Clarifications 2026-06-30)*: latest-only, **one tailored CV per offer** — regenerating overwrites the prior tailored CV for that offer. Per-offer history and named variants are out of scope for this version.
- **Emphasised skills** come from the offer's enrichment (key skills) and fit (matched/missing) produced by feature 002; if those are still pending for an offer, the workspace opens with skills shown as pending and the user can still edit the prompt and generate.
- **Worker semantics reused**: pending / produced / failed states, bounded retries, and the local-only get-pending / write-back queue mirror feature 002.
- **Storage location**: generated CV files live within the gitignored CV data area (the same area feature 003 backs up), so they are local-only, never committed, and recoverable.
- **Stack retained**: React (Vite, TypeScript) front end + .NET 10 (ASP.NET Core) back end + PostgreSQL (EF Core, append-only migrations), layered `Domain → Application → Infrastructure → Web`.
- **NON-GOALS**: calling a paid external AI API from the backend; a full WYSIWYG editor for the rendered CV (the editing surface is the prompt, not the rendered document); downloading the raw HTML source (HTML is an internal rendering step only — PDF is the only download); multi-version history or named variants of tailored CVs per offer; auto-generating tailored CVs for every offer; editing the user's uploaded CV content through this feature (that is done via the feature-002 CV upload).
