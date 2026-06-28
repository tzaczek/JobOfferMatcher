// Typed fetch wrapper. Maps the backend's `{ error: { code, message } }` envelope
// (contracts/rest-api.md) into a thrown ApiError so callers branch on code, not status.

export class ApiError extends Error {
  readonly code: string
  readonly status: number

  constructor(code: string, message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.status = status
  }
}

interface ErrorEnvelope {
  error?: { code?: string; message?: string }
}

async function parseError(response: Response): Promise<ApiError> {
  let code = 'Unknown'
  let message = response.statusText || 'Request failed'
  try {
    const body = (await response.json()) as ErrorEnvelope
    if (body.error) {
      code = body.error.code ?? code
      message = body.error.message ?? message
    }
  } catch {
    // Non-JSON error body — keep the status text.
  }
  return new ApiError(code, message, response.status)
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    headers: { Accept: 'application/json', ...init?.headers },
    ...init,
  })

  if (!response.ok) {
    throw await parseError(response)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => request<T>(path, { method: 'GET', signal }),

  post: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>(path, {
      method: 'POST',
      headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
    }),

  put: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>(path, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal,
    }),

  del: <T>(path: string, signal?: AbortSignal) => request<T>(path, { method: 'DELETE', signal }),

  /** Multipart upload (CV PDF) — let the browser set the boundary. */
  upload: <T>(path: string, form: FormData, signal?: AbortSignal) =>
    request<T>(path, { method: 'POST', body: form, signal }),
}
