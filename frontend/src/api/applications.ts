// Applications API client (005 contracts/applications-api.md). UI-local (same model as offers.ts).
// getBoard/getApplication + stage/lifecycle/note/task/communication/interview calls use the typed JSON
// client; document upload uses api.upload (multipart) and download uses fetch→blob (mirrors tailoredCv.ts).

import { ApiError, api } from './client.ts'
import type {
  ApplicationBoardDto,
  ApplicationDetailDto,
  ApplicationOutcome,
  CommunicationDirection,
  CommunicationDto,
  DocumentDto,
  InterviewDto,
  NoteDto,
  PipelineStageDto,
  TaskDto,
} from './types.ts'

// --- Pipeline stage configuration (FR-019) ---
export function listStages(signal?: AbortSignal): Promise<PipelineStageDto[]> {
  return api.get<PipelineStageDto[]>('/api/applications/stages', signal)
}

export function createStage(name: string): Promise<PipelineStageDto> {
  return api.post<PipelineStageDto>('/api/applications/stages', { name })
}

export function renameStage(id: string, name: string): Promise<void> {
  return api.put<void>(`/api/applications/stages/${id}`, { name })
}

/** Reorder the whole pipeline by the full ordered id list. */
export function reorderStages(orderedIds: string[]): Promise<void> {
  return api.put<void>('/api/applications/stages/order', { orderedIds })
}

/** Remove a stage; if it still holds applications, `reassignTo` must name another stage (else 409 StageInUse). */
export function deleteStage(id: string, reassignTo?: string): Promise<void> {
  const query = reassignTo ? `?reassignTo=${encodeURIComponent(reassignTo)}` : ''
  return api.del<void>(`/api/applications/stages/${id}${query}`)
}

// --- Board + detail ---
export function getBoard(signal?: AbortSignal): Promise<ApplicationBoardDto> {
  return api.get<ApplicationBoardDto>('/api/applications', signal)
}

/** The application detail (404 if the offer isn't applied). */
export function getApplication(offerId: string, signal?: AbortSignal): Promise<ApplicationDetailDto> {
  return api.get<ApplicationDetailDto>(`/api/applications/${offerId}`, signal)
}

// --- Lifecycle ---
export function moveStage(offerId: string, stageId: string): Promise<void> {
  return api.post<void>(`/api/applications/${offerId}/stage`, { stageId })
}

export function closeApplication(offerId: string, outcome: ApplicationOutcome): Promise<void> {
  return api.post<void>(`/api/applications/${offerId}/close`, { outcome })
}

export function reopenApplication(offerId: string): Promise<void> {
  return api.post<void>(`/api/applications/${offerId}/reopen`)
}

/** Permanently delete the application subtree (recoverable only from a prior backup). Requires confirm. */
export function deleteApplication(offerId: string): Promise<void> {
  return api.del<void>(`/api/applications/${offerId}?confirm=true`)
}

// --- Notes (append-only) ---
export function addNote(offerId: string, body: string): Promise<NoteDto> {
  return api.post<NoteDto>(`/api/applications/${offerId}/notes`, { body })
}

// --- Tasks ---
export interface TaskInput {
  title: string
  description?: string | null
  dueAt?: string | null
}

export function addTask(offerId: string, input: TaskInput): Promise<TaskDto> {
  return api.post<TaskDto>(`/api/applications/${offerId}/tasks`, input)
}

export interface TaskUpdate {
  title?: string
  description?: string | null
  dueAt?: string | null
  completed?: boolean
}

export function updateTask(offerId: string, taskId: string, update: TaskUpdate): Promise<void> {
  return api.put<void>(`/api/applications/${offerId}/tasks/${taskId}`, update)
}

export function deleteTask(offerId: string, taskId: string): Promise<void> {
  return api.del<void>(`/api/applications/${offerId}/tasks/${taskId}`)
}

// --- Communications & interviews ---
export interface CommunicationInput {
  occurredAt: string
  direction: CommunicationDirection
  channel: string
  summary: string
}

export function addCommunication(offerId: string, input: CommunicationInput): Promise<CommunicationDto> {
  return api.post<CommunicationDto>(`/api/applications/${offerId}/communications`, input)
}

export interface InterviewInput {
  kind: string
  scheduledAt?: string | null
  interviewer?: string | null
  notes?: string | null
}

export function addInterview(offerId: string, input: InterviewInput): Promise<InterviewDto> {
  return api.post<InterviewDto>(`/api/applications/${offerId}/interviews`, input)
}

export interface InterviewUpdate {
  kind?: string
  scheduledAt?: string | null
  interviewer?: string | null
  outcome?: string | null
  notes?: string | null
}

export function updateInterview(offerId: string, interviewId: string, update: InterviewUpdate): Promise<void> {
  return api.put<void>(`/api/applications/${offerId}/interviews/${interviewId}`, update)
}

export function deleteInterview(offerId: string, interviewId: string): Promise<void> {
  return api.del<void>(`/api/applications/${offerId}/interviews/${interviewId}`)
}

// --- Documents (multipart up, blob down) ---
export function uploadDocument(offerId: string, file: File): Promise<DocumentDto> {
  const form = new FormData()
  form.append('file', file, file.name)
  return api.upload<DocumentDto>(`/api/applications/${offerId}/documents`, form)
}

export function deleteDocument(offerId: string, docId: string): Promise<void> {
  return api.del<void>(`/api/applications/${offerId}/documents/${docId}`)
}

/** Parse the `{ error: { code, message } }` envelope from a non-OK fetch Response. */
async function throwFromResponse(res: Response): Promise<never> {
  let code = 'Unknown'
  let message = res.statusText || 'Request failed'
  try {
    const body = (await res.json()) as { error?: { code?: string; message?: string } }
    if (body.error) {
      code = body.error.code ?? code
      message = body.error.message ?? message
    }
  } catch {
    // Non-JSON error body — keep the status text.
  }
  throw new ApiError(code, message, res.status)
}

/** Extract the filename from a Content-Disposition header (`attachment; filename="…"`). */
function filenameFromDisposition(header: string | null): string | null {
  if (!header) return null
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(header)
  return match ? decodeURIComponent(match[1]) : null
}

/** Trigger a browser save of an in-memory blob under the given filename. */
function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** Download an attachment — fetch→blob→save (mirrors tailoredCv.ts). Returns the saved name. */
export async function downloadDocument(offerId: string, docId: string, fallbackName = 'document'): Promise<{ fileName: string }> {
  const res = await fetch(`/api/applications/${offerId}/documents/${docId}/download`)
  if (!res.ok) {
    await throwFromResponse(res)
  }
  const blob = await res.blob()
  const fileName = filenameFromDisposition(res.headers.get('Content-Disposition')) ?? fallbackName
  saveBlob(blob, fileName)
  return { fileName }
}
