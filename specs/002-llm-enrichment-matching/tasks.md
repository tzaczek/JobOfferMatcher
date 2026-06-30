---
description: "Task list for feature 002 — LLM Enrichment & Matching (Claude-as-Worker)"
---

# Tasks: LLM Enrichment & Matching (Claude-as-Worker)

**Input**: Design documents from `specs/002-llm-enrichment-matching/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: INCLUDED — this project's constitution makes them load-bearing (Principle V "Real Database
in Tests", Principle VI "Green Before Done") and the plan's Testing section names them explicitly.
They are not optional here.

**Organization**: Tasks are grouped by user story. This feature is **additive** to the implemented
feature-001 codebase; the shared enrichment machinery lives in Setup + Foundational, and each user
story is a vertical slice (input-hash → `/pending` projection → `/results` handling → `/enrich`
worker handler → UI) that is independently demonstrable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US5 (maps to spec.md). Setup/Foundational/Polish carry no story label.
- All paths are repo-relative (`backend/src/…`, `frontend/src/…`, `.claude/…`).

---

## Phase 1: Setup (shared scaffolding)

**Purpose**: Low-risk, parallelizable scaffolding shared by every story.

- [X] T001 [P] Create `backend/src/Domain/Enrichment/` and add the shared enums: `EnrichmentState` (Pending/Produced/Failed) in `backend/src/Domain/Enrichment/EnrichmentState.cs` and `CvProcessingState` (Pending/Produced/Unreadable/Failed) in `backend/src/Domain/Cv/CvProcessingState.cs`
- [X] T002 [P] Add the `InputHash` value object (Algorithm/Version/Hash, `Sha256` const) in `backend/src/Domain/Enrichment/InputHash.cs`, mirroring `Domain/Offers/ContentFingerprint.cs`
- [X] T003 [P] Scaffold the worker slash command `.claude/commands/enrich.md` with the GET-pending → produce → POST-results loop skeleton per `contracts/worker-protocol.md` (per-kind handlers filled in by US2/US3/US4)
- [X] T004 [P] Add the frontend enrichment API client + types in `frontend/src/api/enrichment.ts` and `frontend/src/api/types.ts` (status/work/result DTOs) per `contracts/enrichment-api.md`
- [X] T005 [P] Add `pending`/`produced`/`failed`/`unreadable` status-chip tokens in `frontend/src/theme/tokens.css` (light+dark) + `.chip--*` rules in `frontend/src/theme/base.css`, and an `enrichmentStatusClass` helper in `frontend/src/theme/index.ts` (mirror `statusChipClass`)

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The shared enrichment machinery + the ADR-2 removals. **No user story can begin until
this phase completes.**

### ADR-2 removals (FR-005 — no non-AI fallback; must precede the read-model rework)

- [X] T006 Remove the non-AI fit producer: delete `Scorer.Score`, `FitScore`, `FitBreakdown`, `ScoringInput` from `backend/src/Domain/Matching/` (KEEP `CombinedRank`/`DegradedRank`, `ScoringWeights`, `SeniorityLevels`) and delete `backend/tests/Domain.Tests/ScorerTests.cs`
- [X] T007 Remove the keyword CV profiler: delete `CvProfileBuilder`, `ICvProfileBuilder`, `SkillCatalog`, `SkillCatalogLoader`, `SkillRef`, `CandidateProfile`, `CandidateProfileMerger`, and `backend/src/Infrastructure/Cv/skill-catalog.json`; remove the `SkillCatalog`/`ICvProfileBuilder` registrations in `backend/src/Infrastructure/DependencyInjection.cs` and the `ICvProfileBuilder` ctor param from `backend/src/Application/Cv/CvService.cs`
- [X] T008 [P] Drop FuzzySharp: remove `<PackageReference Include="FuzzySharp"/>` from `backend/src/Infrastructure/Infrastructure.csproj`, `<PackageVersion Include="FuzzySharp"…/>` from `backend/Directory.Packages.props`, and the `skill-catalog.json` `<None Update… CopyToOutputDirectory>` directive
- [X] T009 [P] Rewrite `backend/tests/Infrastructure.Tests/CvExtractionTests.cs`: keep the PdfPig degradation/readability test, drop the `CvProfileBuilder` assertion
- [X] T010 [P] Add a no-AI-package guard test asserting `Directory.Packages.props`/csprojs reference no `@anthropic-ai`/Anthropic/AI package, in `backend/tests/Infrastructure.Tests/NoAiDependencyTests.cs` (FR-012/SC-005)

### Shared Domain + value objects

- [X] T011 [P] Add the AI `CvProfile` VO (`Skills`, `Seniority`, `Summary`) in `backend/src/Domain/Cv/CvProfile.cs`
- [X] T012 [P] Add `EnrichmentSettings` VO (OfferSummaryMaxWords=60, CvSummaryMaxWords=60, MaxKeySkills=10, FitRationaleMaxWords=30, RetryLimit=3) in `backend/src/Domain/Settings/EnrichmentSettings.cs`, and add `Enrichment` + `UpdateEnrichment` to `backend/src/Domain/Settings/AppSettings.cs`
- [X] T013 Add the pure input-hash composers (`OfferEnrichmentInputs.Hash`, `CvProfileInputs.Hash`, `EffectiveProfile.Version`, `OfferFitInputs.Hash`) in `backend/src/Domain/Enrichment/EnrichmentInputs.cs`, reusing `ContentFingerprint`; unit tests in `backend/tests/Domain.Tests/EnrichmentInputsTests.cs` (determinism, description-inclusion vs ContentFingerprint, weights/profile composition)
- [X] T014 [P] Add the `OfferEnrichment` entity + state machine (CreatePending/MarkProduced/RecordFailure/Invalidate/Rearm) in `backend/src/Domain/Enrichment/OfferEnrichment.cs`; unit tests in `backend/tests/Domain.Tests/OfferEnrichmentStateTests.cs` (retry→Failed at RetryLimit; Invalidate keeps payload internally)
- [X] T015 [P] Add the `OfferFit` entity + state machine in `backend/src/Domain/Enrichment/OfferFit.cs`; unit tests in `backend/tests/Domain.Tests/OfferFitStateTests.cs`
- [X] T016 Extend `backend/src/Domain/Cv/CandidateCv.cs`: add `ProfileState`/`ProfileAttempts`/`Profile`(CvProfile)/`EnrichmentInputHash`/`ProfileProducedAt` + methods `SetExtractionGauge`/`ApplyProfile`/`MarkUnreadable`/`RecordProfileFailure`/`RearmProfile`; remove the `DerivedProfile` member; unit tests in `backend/tests/Domain.Tests/CandidateCvProfileTests.cs` (Unreadable distinct from Failed; no retries on Unreadable)

### Shared persistence

- [X] T017 EF config: add `OfferEnrichmentConfiguration` + `OfferFitConfiguration` in `backend/src/Infrastructure/Persistence/Configurations/` (PK=`OfferId`, FK→`offers` cascade, jsonb lists, index on `state`); update `CandidateCvConfiguration` (drop `derived_profile` mapping, add `profile_*` columns) + `AppSettingsConfiguration` (`enrichment` jsonb); add `DbSet<OfferEnrichment>`/`DbSet<OfferFit>` to `backend/src/Infrastructure/Persistence/AppDbContext.cs`
- [X] T018 Create the append-only migration `LlmEnrichment` (`dotnet ef migrations add LlmEnrichment`) in `backend/src/Infrastructure/Persistence/Migrations/`: create `offer_enrichment` + `offer_fit`; add `candidate_cv` columns (with defaults); add `app_settings.enrichment` (default JSON); **drop `derived_profile`**; regenerate the snapshot. Integration test (real Postgres, Testcontainers) that it applies + jsonb round-trips, in `backend/tests/Infrastructure.Tests/LlmEnrichmentMigrationTests.cs`

### Shared application/web/read-model

- [X] T019 [P] Add `IEnrichmentRepository` in `backend/src/Application/Enrichment/IEnrichmentRepository.cs` and `EnrichmentRepository` in `backend/src/Infrastructure/Persistence/Repositories/EnrichmentRepository.cs` (get/upsert satellites, **eligibility-gated** pending/failed COUNTs, ResetFailed/ResetAll, pending-CV query); register in `backend/src/Infrastructure/DependencyInjection.cs`
- [X] T020 Add the kind-agnostic `EnrichmentService` framework in `backend/src/Application/Enrichment/EnrichmentService.cs`: pending-work ordering (FR-019: profile-first, available-first, newest-first) + eligibility gating, the `SubmitResults` dispatch with the **recompute-from-live-inputs** stale guard + state machine, and the reworked effective-profile composition from `CvProfile` + preferences. Per-kind projection/validation are added by US2/US3/US4. Application tests in `backend/tests/Application.Tests/EnrichmentServiceTests.cs` (ordering, stale rejection, retry→Failed, Failed-rearm-on-input-change)
- [X] T021 Add the `EnrichmentEndpoints` group in `backend/src/Web/Endpoints/EnrichmentEndpoints.cs` (`GET /api/enrichment/pending`, `POST /api/enrichment/results`; `/status` + `/rerun` finalized in US5) with a **fail-closed loopback guard** (403 for non-loopback **and** null/unknown `RemoteIpAddress`); wire into `backend/src/Web/Endpoints/FeatureEndpoints.cs`. Guard unit test (middleware/filter level) in `backend/tests/Infrastructure.Tests/LoopbackGuardTests.cs`
- [X] T022 Add the idempotent backfill in `backend/src/Infrastructure/Persistence/DatabaseInitializer.cs`: after `MigrateAsync`, insert `Pending` `offer_enrichment` + `offer_fit` rows for every offer lacking one and compute the existing CV's `enrichment_input_hash` from the stored PDF (FR-014). Integration test in `backend/tests/Infrastructure.Tests/EnrichmentBackfillTests.cs`
- [X] T023 Read-model scaffolding: in `backend/src/Application/Offers/OfferReadModels.cs` add `Summary`/`KeySkills`/`EnrichmentState`/`FitState` to `OfferListItem`, `Rationale`/`State` to `FitView`, and replace `NoReadableCv` with `HasProducedProfile` + add `PendingEnrichment`/`FailedEnrichment` to `OfferListMeta`; in `backend/src/Infrastructure/Persistence/Repositories/OfferReadService.cs` stop calling `Scorer.Score`, load the satellites, project `produced/pending/failed` via a recomputed hash, and implement the **two-tier** default sort (produced-fit by `CombinedRank`, then pending/absent by `DegradedRank`)
- [X] T024 Rework `backend/src/Application/Cv/ProfileService.cs` (`BuildEffectiveProfileAsync`/`GetAsync`/`ProfileView` source from the new `CvProfile` + preferences; drop `CandidateProfileMerger`/`DerivedProfile`) and `backend/src/Web/Endpoints/CvEndpoints.cs` `ToCvDto` (project the AI profile: `state`/`summary`/`skills`/`seniority`/`attemptCount`)
- [X] T025 Add the enrichment-settings surface: `SettingsService.UpdateEnrichmentAsync` (validate all caps > 0, RetryLimit ≥ 1; `Error InvalidEnrichmentSettings`) in `backend/src/Application/Settings/SettingsService.cs`, and `GET`/`PUT /api/settings/enrichment` in `backend/src/Web/Endpoints/SettingsEndpoints.cs` (FR-018)

**Checkpoint**: build is green, migration applies, the queue framework + endpoints + read-model exist;
no kind produces output yet. User stories can now begin.

---

## Phase 3: User Story 1 — Publish date & "Recently published" sort (Priority: P1) ✅ DELIVERED

**Goal**: Already shipped in feature 002's first slice — offers show a publish date and sort newest-first.

**Independent Test**: `GET /api/offers/?sort=published` returns offers newest-first (date-less last), cards show "Published <date>" (quickstart US1).

- [X] T026 [US1] Regression-verify US1 after the read-model/sort refactor: confirm the two-tier default sort (T023) did **not** change the `published`/`salary`/`fit` sorts, and the card date still renders. Add/keep a read-service test asserting `OfferSort.Published` ordering in `backend/tests/Infrastructure.Tests/OfferReadServiceSortTests.cs` (no new implementation — verification only)

**Checkpoint**: US1 still works unchanged.

---

## Phase 4: User Story 2 — Offer summary + key skills (Priority: P1) 🎯 MVP

**Goal**: Each offer gets a Claude-generated summary + key-skills list; un-produced offers show "pending".

**Independent Test**: after a scan + `/enrich`, every available offer shows `enrichmentState: produced` with a summary + key skills; before, `pending`; a description change re-flips one offer to pending (quickstart US2).

- [X] T027 [US2] Implement the `offerSummary` work-item projection in `EnrichmentService.GetPendingWork` (`backend/src/Application/Enrichment/EnrichmentService.cs`): offer fields + sanitized description (existing `Ganss.Xss` `IHtmlSanitizer`) + `OfferEnrichmentInputs` hash + guidance
- [X] T028 [US2] Implement the `offerSummary` result handling in `EnrichmentService.SubmitResults`: loose validation (non-empty summary; skills array; clamp to `MaxKeySkills`) → `MarkProduced`/`RecordFailure`
- [X] T029 [US2] Add `Offer.RefreshMinorContent` in `backend/src/Domain/Offers/Offer.cs` (updates denormalized description/company/location, no `HasUnseenUpdate`/version/event) and wire the eager enrichment-invalidation + Minor refresh into `ScanOrchestrator.UpsertAsync` (`backend/src/Application/Scanning/`) on content/description change (ADR-3, FR-006/US2-AC3); must not roll back the offers upsert
- [X] T030 [US2] Worker: implement the `offerSummary` handler in `.claude/commands/enrich.md` (≤ `summaryWords` summary + ≤ `maxSkills` key skills)
- [X] T031 [P] [US2] Frontend: render summary + key skills + `pending`/`failed` states on the offer card in `frontend/src/components/OfferCard.tsx`
- [X] T032 [P] [US2] Application test for offerSummary validation/invalidation in `backend/tests/Application.Tests/OfferSummaryTests.cs`
- [X] T033 [P] [US2] Infrastructure e2e (real Postgres): scan → pending summary → `POST /results` → feed shows produced; description change → that offer pending — in `backend/tests/Infrastructure.Tests/OfferSummaryFlowTests.cs`
- [X] T034 [P] [US2] Frontend RTL test for pending/produced/failed summary rendering in `frontend/src/components/OfferCard.test.tsx`

**Checkpoint**: MVP — offers carry AI summaries/skills; SC-001 verifiable.

---

## Phase 5: User Story 3 — CV profile (recruiter-style understanding) (Priority: P2)

**Goal**: Claude reads the uploaded CV and derives skills/seniority/summary; unreadable CVs flagged without error.

**Independent Test**: upload a readable CV → `/enrich` → profile `produced` with skills/seniority/summary; an image-only/garbled CV → `unreadable` (no crash, counted separately) (quickstart US3).

- [X] T035 [US3] Update `CvService.UploadAsync` (`backend/src/Application/Cv/CvService.cs`): keep PdfPig to set the `IsReadable` gauge, compute `CvProfileInputs` hash from the buffered bytes, set `ProfileState=Pending`; stop calling the (removed) keyword builder (FR-003/FR-005)
- [X] T036 [US3] Add `ICvFileStore.GetAbsolutePath(storedFileName)` in `backend/src/Application/Cv/ICvFileStore.cs` + `backend/src/Infrastructure/Cv/LocalCvFileStore.cs`
- [X] T037 [US3] Implement the `cvProfile` work-item projection in `EnrichmentService.GetPendingWork` (emitted **first**, FR-019): absolute `document.path`, `readable` gauge, `fallbackText` **only when readable=false** (regenerated via `ICvTextExtractor`, not persisted), `inputsHash`, guidance
- [X] T038 [US3] Implement the `cvProfile` result handling in `EnrichmentService.SubmitResults`: `produced` → `ApplyProfile`; `unreadable` → `MarkUnreadable` (no retry); `error` → `RecordProfileFailure`; recompute stale guard
- [X] T039 [US3] Worker: implement the `cvProfile` handler in `.claude/commands/enrich.md` (Read the original PDF at `document.path`; fall back to `fallbackText`; emit `produced`/`unreadable`)
- [X] T040 [P] [US3] Frontend: CV-page profile state chip + skills/seniority/summary + `unreadable`/`failed` states in `frontend/src/pages/Cv/CvPage.tsx`
- [X] T041 [P] [US3] Application test (cvProfile ordered first; unreadable vs failed accounting) in `backend/tests/Application.Tests/CvProfileTests.cs`
- [X] T042 [P] [US3] Infrastructure e2e (real Postgres): upload → pending → produced profile; image-only → unreadable — in `backend/tests/Infrastructure.Tests/CvProfileFlowTests.cs`
- [X] T043 [P] [US3] Frontend RTL test for CV profile pending/produced/unreadable/failed rendering in `frontend/src/pages/Cv/CvPage.test.tsx`

**Checkpoint**: CV profile produced; SC-002 verifiable.

---

## Phase 6: User Story 4 — AI fit score with matched/missing (Priority: P2)

**Goal**: Each offer scored 0–100 vs the profile, with matched/missing + a rationale, weighted by guidance; never a non-AI fallback.

**Independent Test**: with a produced profile, `/enrich` → offers show `fit: {state:produced, score, matched, missing, rationale}`; no CV → fit absent; raise the Skills weight → all fits pending then re-scored (quickstart US4). **Depends on US3** (needs a produced profile).

- [X] T044 [US4] Implement the `offerFit` work-item projection in `EnrichmentService.GetPendingWork` — emitted **only** when a current produced CV profile exists (FR-019 profile-before-fit): offer fields + profile + weights + preferences + `OfferFitInputs` hash (using `EffectiveProfile.Version`)
- [X] T045 [US4] Implement the `offerFit` result handling in `EnrichmentService.SubmitResults`: validate score 0..100, matched/missing arrays, rationale; produce/fail/retry; recompute stale guard
- [X] T046 [US4] Implement fit invalidation hooks: `SettingsService.UpdateWeightsAsync` (weights), CV upload/profile-produced/replaced, and preferences change → bulk `UPDATE offer_fit SET state='Pending', attempts=0` + re-arm `Failed`; offer content change → that offer's fit (FR-007/SC-004). Wire into `backend/src/Application/Settings/SettingsService.cs`, `backend/src/Application/Cv/CvService.cs`/`ProfileService.cs`, and the scan path (alongside T029)
- [X] T047 [US4] Finalize the fit display gate + tier-1 ordering in `backend/src/Infrastructure/Persistence/Repositories/OfferReadService.cs`: `fit=null` only when no produced profile (not the PdfPig gauge); produced score shown only when its hash is current (builds on T023)
- [X] T048 [US4] Worker: implement the `offerFit` handler in `.claude/commands/enrich.md` (0–100 score + matched/missing + ≤ `rationaleWords` rationale, weights as guidance)
- [X] T049 [P] [US4] Frontend: fit chip (score + rationale) + matched/missing + `pending`/`failed` on the offer card (`frontend/src/components/OfferCard.tsx`); relabel the weight sliders "guidance to Claude" in `frontend/src/pages/Settings/WeightsSection.tsx` (FR-011)
- [X] T050 [P] [US4] Application test (fit emitted only with a produced profile; weights/CV/prefs change → all fits pending; stale guard) in `backend/tests/Application.Tests/OfferFitTests.cs`
- [X] T051 [P] [US4] Infrastructure e2e (real Postgres): profile produced → fit pending → produced; weight change → all fits pending — in `backend/tests/Infrastructure.Tests/OfferFitFlowTests.cs`
- [X] T052 [P] [US4] Frontend RTL test for fit produced/pending/failed/absent rendering in `frontend/src/components/OfferCard.fit.test.tsx`

**Checkpoint**: AI fit live; SC-003/SC-004 verifiable.

---

## Phase 7: User Story 5 — Freshness & on-demand re-run (Priority: P3)

**Goal**: Results refresh when inputs change, the user can re-run on demand, the pending/failed count is visible, and first-enablement backfill processes everything.

**Independent Test**: change CV/weights → `pendingFits` jumps to the full count; `POST /rerun` + `/enrich` drains pending to 0; the count is visible; a partial pass renders a clean mix (quickstart US5).

- [X] T053 [US5] Finalize `GET /api/enrichment/status` in `backend/src/Web/Endpoints/EnrichmentEndpoints.cs` + `EnrichmentService`: eligibility-gated `pendingTotal`/per-kind/`failedTotal`, `hasProducedProfile`, `lastResultAt` (FR-010/FR-016/SC-007); also surface `pendingEnrichment`/`failedEnrichment` on `OfferListMeta`
- [X] T054 [US5] Implement `POST /api/enrichment/rerun` (`scope: failed|all`) in `EnrichmentEndpoints` + `EnrichmentService.TriggerRerun`: re-arm `Failed` rows whose hash matches / force `Produced → Pending`; return new counts (FR-009)
- [X] T055 [P] [US5] Frontend: global pending/failed enrichment indicator + manual "re-run" button calling `/rerun` (feed page + `frontend/src/api/enrichment.ts`), and a new `frontend/src/pages/Settings/EnrichmentSection.tsx` (number inputs) calling `/api/settings/enrichment` (FR-018)
- [X] T056 [US5] Verify backfill end-to-end (T022): on first enablement the status count is correct (not 0) and the first `/enrich` drains to 0; confirm a partial pass renders a clean produced/pending/failed mix (FR-014/US5-AC3/AC4)
- [X] T057 [P] [US5] Application test (status-count gating; rerun re-arm; backfill idempotency) in `backend/tests/Application.Tests/EnrichmentOpsTests.cs`
- [X] T058 [P] [US5] Infrastructure e2e (real Postgres): backfill → status → `/enrich` → pendingTotal 0 — in `backend/tests/Infrastructure.Tests/EnrichmentRerunFlowTests.cs`
- [X] T059 [P] [US5] Frontend RTL test for the pending/failed indicator + re-run + enrichment settings in `frontend/src/pages/Settings/EnrichmentSection.test.tsx`

**Checkpoint**: all stories independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T060 [P] Run the full `quickstart.md` validation (all US scenarios + the Privacy & isolation checks: non-loopback/null-IP → 403; no-AI-package guard passes; SC-005 = 0 external records)
- [X] T061 [P] Confirm the feature-001 regression suite is green — collection/dedup/availability/role-grouping/salary/export unchanged (FR-013), **including** the scan *Unchanged* branch after `RefreshMinorContent` (add/adjust an integration test asserting Minor refresh doesn't set `Updated`/version/event)
- [ ] T062 [P] Visual verification (Principle VII): run `./start.ps1` and eyeball produced/pending/failed/unreadable states on the feed, offer cards, CV page, and settings — **pending manual run** (rendering is covered by RTL tests; requires the user to eyeball a live app)
- [X] T063 Update `README.md`/`docs/` for the `/enrich` worker, the enrichment settings, and the **host-process** run-mode caveat for enrichment (ADR-4)

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)**: no deps — start immediately.
- **Foundational (P2)**: depends on Setup; **blocks all stories**. Within it: removals (T006–T010) → Domain types (T011–T016) → persistence (T017–T018) → app/web/read-model (T019–T025). T013 depends on T002; T016 depends on T011; T017 depends on T014/T015/T016; T018 depends on T017; T020 depends on T013/T019; T023/T024 depend on T006/T007 (removals).
- **User Stories (P3–P7)**: all depend on Foundational. **US4 depends on US3** (fit needs a produced profile). US1/US2/US3 are mutually independent; US5 is operational polish over US2–US4 (the `/status`,`/rerun`,counts UI it finalizes were stubbed in Foundational).
- **Polish (P8)**: after all desired stories.

### Within each story
- Backend `/pending` projection + `/results` handling → worker handler → UI; tests alongside.
- Tests are real-Postgres for Infrastructure (Principle V); each story closes only when green (Principle VI).

### Parallel opportunities
- Setup: T001–T005 all [P].
- Foundational: T008/T009/T010 [P]; T011/T012 [P]; T014/T015 [P] (after T002).
- Per story, the [P] tasks are different files (UI + the three test files) and run in parallel once the story's backend tasks land.
- With multiple developers after Foundational: US2 and US3 in parallel; US4 starts when US3's profile path is in place; US5 last.

---

## Parallel Example: User Story 2

```bash
# After T027–T030 (backend + worker) land, run the US2 parallel tasks together:
Task: "Frontend offer-card summary/skills rendering (T031) in frontend/src/components/OfferCard.tsx"
Task: "Application test (T032) in backend/tests/Application.Tests/OfferSummaryTests.cs"
Task: "Infrastructure e2e (T033) in backend/tests/Infrastructure.Tests/OfferSummaryFlowTests.cs"
Task: "Frontend RTL test (T034) in frontend/src/components/OfferCard.test.tsx"
```

---

## Implementation Strategy

### MVP (first new value)
1. Setup (P1) → Foundational (P2) → **US2 (P4)**: offers gain AI summaries/skills. STOP & validate (SC-001), demo. (US1 is already delivered.)

### Incremental delivery
1. + **US3** → CV profile (SC-002).
2. + **US4** → AI fit (SC-003/SC-004) — the headline matcher value.
3. + **US5** → freshness/re-run/counts/backfill (SC-007), then Polish.

---

## Notes
- `[P]` = different files, no incomplete-task dependency. `[US#]` traces to spec.md.
- Tests included (Principle V/VI). Verify each story green before the next priority.
- Conventional Commits, one logical change each; commit after each task or logical group.
- Privacy: `/api/enrichment/*` stays fail-closed loopback; the CV binary is read by the worker via path (host-process mode, ADR-4); backend makes no external AI call (T010 guard).
- **NON-NEGOTIABLE invariant** (FR-005): never display a non-AI fallback score — un-produced items are `pending`/`failed`, never a number.
