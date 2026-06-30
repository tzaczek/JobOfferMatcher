// Backup & Restore API client (003 contracts/backup-api.md).
// downloadBackup uses fetch→blob (not a plain <a download>) so the UI can surface the busy state
// and a success/failure message (FR-006/016); inspect + restore are multipart uploads (mirrors cv.ts).

import { ApiError } from './client.ts'
import { api } from './client.ts'
import type { BackupInspectionDto, RestoreReportDto } from './types.ts'

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

/**
 * Create + download a complete backup (US1). Fetches the archive, reads it as a blob, and saves it —
 * so the caller can show a busy state and report completion/failure (a plain <a download> cannot).
 * Returns the saved filename (carrying the UTC timestamp) for the success message.
 */
export async function downloadBackup(): Promise<{ fileName: string }> {
  const res = await fetch('/api/backup', { headers: { Accept: 'application/zip' } })
  if (!res.ok) {
    await throwFromResponse(res)
  }
  const blob = await res.blob()
  const fileName = filenameFromDisposition(res.headers.get('Content-Disposition')) ?? 'jobs-backup.zip'
  saveBlob(blob, fileName)
  return { fileName }
}

/** Inspect an uploaded backup without restoring (US3) — returns its summary + compatibility. */
export function inspectBackup(file: File): Promise<BackupInspectionDto> {
  const form = new FormData()
  form.append('file', file)
  return api.upload<BackupInspectionDto>('/api/backup/inspect', form)
}

/** Restore from an uploaded backup (US2) — guarded, all-or-nothing. */
export function restoreBackup(file: File): Promise<RestoreReportDto> {
  const form = new FormData()
  form.append('file', file)
  return api.upload<RestoreReportDto>('/api/backup/restore', form)
}
