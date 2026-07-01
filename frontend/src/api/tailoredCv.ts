// Tailored-CV API client (004 contracts/tailored-cv-api.md). The whole group is loopback-only.
// getDraft/generate/get/list/delete use the typed JSON client; downloadTailoredPdf uses fetch→blob
// (mirrors backup.ts) so the UI can surface a busy/error state a plain <a download> cannot.

import { ApiError, api } from './client.ts'
import type { TailoredCvDraftDto, TailoredCvDto } from './types.ts'

export interface GenerateTailoredCvInput {
  prompt: string
  emphasisedSkills: string[]
  sourceCvId?: string
}

/** The prefilled modal contents. Pass `skills` to recompose the default prompt for a toggled selection (FR-004). */
export function getDraft(offerId: string, opts?: { skills?: string[] }, signal?: AbortSignal): Promise<TailoredCvDraftDto> {
  const skills = opts?.skills
  const query = skills && skills.length > 0 ? `?skills=${encodeURIComponent(skills.join(','))}` : ''
  return api.get<TailoredCvDraftDto>(`/api/tailored-cv/offer/${offerId}/draft${query}`, signal)
}

/** Create or regenerate (latest-only) → pending; bumps the generation version. */
export function generate(offerId: string, body: GenerateTailoredCvInput): Promise<TailoredCvDto> {
  return api.post<TailoredCvDto>(`/api/tailored-cv/offer/${offerId}`, body)
}

/** The tailored CV for one offer (reopen). Rejects with `TailoredCvNotFound` (404) when none exists. */
export function getTailored(offerId: string, signal?: AbortSignal): Promise<TailoredCvDto> {
  return api.get<TailoredCvDto>(`/api/tailored-cv/offer/${offerId}`, signal)
}

/** All tailored CVs (the dedicated page), newest first. */
export function listTailored(signal?: AbortSignal): Promise<{ data: TailoredCvDto[] }> {
  return api.get<{ data: TailoredCvDto[] }>('/api/tailored-cv', signal)
}

/** Remove the tailored CV (row + both files). Idempotent. */
export function deleteTailored(offerId: string): Promise<void> {
  return api.del<void>(`/api/tailored-cv/offer/${offerId}`)
}

/** The in-app preview URL (the produced HTML), for an `<iframe src>`. Pass the version to bust the cache after regenerate. */
export function previewUrl(offerId: string, version?: number): string {
  const v = version !== undefined ? `?v=${version}` : ''
  return `/api/tailored-cv/offer/${offerId}/preview${v}`
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

/** Download the produced tailored CV as a PDF (US3) — fetch→blob→save (mirrors backup.ts). Returns the saved name. */
export async function downloadTailoredPdf(offerId: string): Promise<{ fileName: string }> {
  const res = await fetch(`/api/tailored-cv/offer/${offerId}/download`, { headers: { Accept: 'application/pdf' } })
  if (!res.ok) {
    await throwFromResponse(res)
  }
  const blob = await res.blob()
  const fileName = filenameFromDisposition(res.headers.get('Content-Disposition')) ?? 'tailored-cv.pdf'
  saveBlob(blob, fileName)
  return { fileName }
}
