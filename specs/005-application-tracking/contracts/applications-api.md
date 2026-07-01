# Contract — Application Tracking REST API

**Feature**: `005-application-tracking` | **Date**: 2026-07-01 | Base: `/api` · Access: **UI** (same
model as `/api/offers/*` — single-user localhost; **not** the loopback-only worker/backup channel).
Errors use the existing `{ error: { code, message } }` envelope; `Result` maps via `ToHttp` (404
not-found, 409 conflict, 400 validation, 204 success-no-content). All timestamps ISO-8601 UTC.

An application is keyed by its **offer id** (satellite). It exists once the offer is applied.

---

## Pipeline stage configuration (FR-019)

| Method & path | Body | Result |
|---------------|------|--------|
| `GET /applications/stages` | — | `PipelineStageDto[]` ordered by position |
| `POST /applications/stages` | `{ name }` | 201 `PipelineStageDto` (appended at end) |
| `PUT /applications/stages/{id}` | `{ name }` | 204 (rename) |
| `PUT /applications/stages/order` | `{ orderedIds: string[] }` | 204 (reorder — full ordered id list) |
| `DELETE /applications/stages/{id}` | `?reassignTo={otherStageId}` | 204; **409 `StageInUse`** if the stage still holds applications and no valid `reassignTo` is given (spec edge case — never orphan) |

`PipelineStageDto { id, name, position }`.

## The pipeline board (FR-004 / FR-009 / SC-002)

`GET /applications` → `ApplicationBoardDto`:

```
{ stages: [ { id, name, position, applications: ApplicationCardDto[] } ],
  closed:  [ ApplicationCardDto ] }          // closed grouped/tagged by outcome
ApplicationCardDto {
  offerId, title, company, stageId, status,          // status: 'active' | 'closed'
  outcome?,                                            // 'accepted'|'rejected'|'withdrawn'|'noResponse'
  appliedAt?, outstandingTaskCount, overdueTaskCount, nextInterviewAt?
}
```

## An application (detail + timeline, FR-002/FR-003/FR-007)

| Method & path | Body | Result |
|---------------|------|--------|
| `GET /applications/{offerId}` | — | `ApplicationDetailDto` (404 if the offer isn't applied) |
| `POST /applications/{offerId}/stage` | `{ stageId }` | 204 (move; 409 if closed / 400 if stage unknown) |
| `POST /applications/{offerId}/close` | `{ outcome }` | 204 (409 if already closed / 400 bad outcome) |
| `POST /applications/{offerId}/reopen` | — | 204 (409 if active) |
| `DELETE /applications/{offerId}` | `?confirm=true` | 204 — **permanent** delete of the application subtree (R8); **409 `ConfirmationRequired`** without `confirm=true`. Recoverable only from a prior backup. |

```
ApplicationDetailDto {
  offerId, title, company, stageId, status, outcome?, appliedAt?, closedAt?,
  timeline: TimelineEntryDto[],                      // merged, chronological (FR-007)
  notes: NoteDto[], tasks: TaskDto[], documents: DocumentDto[],
  communications: CommunicationDto[], interviews: InterviewDto[]
}
TimelineEntryDto { occurredAt, kind, title, detail? }   // kind: stageChanged|closed|reopened|note|task|taskDone|document|communication|interview
```

## Notes (FR-006 — append-only journal)

| Method & path | Body | Result |
|---------------|------|--------|
| `POST /applications/{offerId}/notes` | `{ body }` | 201 `NoteDto` |

`NoteDto { id, body, createdAt }`. No edit/delete (append-only; earlier entries never rewritten).

## Tasks (FR-008 — overdue surfaced)

| Method & path | Body | Result |
|---------------|------|--------|
| `POST /applications/{offerId}/tasks` | `{ title, description?, dueAt? }` | 201 `TaskDto` |
| `PUT /applications/{offerId}/tasks/{taskId}` | `{ title?, description?, dueAt?, completed? }` | 204 |
| `DELETE /applications/{offerId}/tasks/{taskId}` | — | 204 |

`TaskDto { id, title, description?, dueAt?, completedAt?, overdue }` (`overdue` derived server-side).

## Documents (FR-010 — any type, ~50 MB, local)

| Method & path | Body | Result |
|---------------|------|--------|
| `POST /applications/{offerId}/documents` | multipart file | 201 `DocumentDto`; **413/400 `FileTooLarge`** over ~50 MB |
| `GET /applications/{offerId}/documents/{docId}/download` | — | file stream (`Content-Disposition`), blob-download on the client |
| `DELETE /applications/{offerId}/documents/{docId}` | — | 204 (deliberate/confirmed in UI) |

`DocumentDto { id, originalFileName, contentType?, sizeBytes, addedAt }`.

## Communications & interviews (FR-011)

| Method & path | Body | Result |
|---------------|------|--------|
| `POST /applications/{offerId}/communications` | `{ occurredAt, direction, channel, summary }` | 201 `CommunicationDto` |
| `POST /applications/{offerId}/interviews` | `{ kind, scheduledAt?, interviewer?, notes? }` | 201 `InterviewDto` |
| `PUT /applications/{offerId}/interviews/{id}` | `{ kind?, scheduledAt?, interviewer?, outcome?, notes? }` | 204 (record outcome / edit) |
| `DELETE /applications/{offerId}/interviews/{id}` | — | 204 |

`CommunicationDto { id, occurredAt, direction, channel, summary }`;
`InterviewDto { id, kind, scheduledAt?, interviewer?, outcome?, notes?, upcoming }` (`upcoming` derived).

## Existing offer endpoints — changed behavior (additive)

- `PUT /offers/{id}/application` (mark applied) — unchanged request; now **also creates** the
  `JobApplication` at the first stage if absent, and seeds the first journal note from the note if the
  journal is empty (R8). Existing behavior otherwise preserved (FR-016).
- `DELETE /offers/{id}/application` (clear applied) — now returns **409 `ApplicationHasHistory`** when the
  application has accumulated interview data, steering the UI to *close* (Withdrawn) instead of erasing
  (R8 / FR-013). Clearing an application with no interview data still succeeds (removes the empty application).

## Export (FR-018)

`GET /export/offers.{json,csv}` (existing) now includes per offer: `applicationStage`,
`applicationStatus`, `applicationOutcome`, and a compact `interviews` list (JSON) / joined column (CSV).
