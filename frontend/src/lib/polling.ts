// Default UI transport is polling (Principle X / contracts/rest-api.md): the SPA polls a
// status endpoint until it reaches a terminal state. SignalR is an optional future upgrade.

export interface PollOptions<T> {
  /** Fetch one status snapshot. */
  fetch: (signal: AbortSignal) => Promise<T>
  /** Return true once polling should stop (terminal state reached). */
  done: (value: T) => boolean
  /** Called after every successful poll (for live UI updates). */
  onTick?: (value: T) => void
  intervalMs?: number
  signal?: AbortSignal
}

const delay = (ms: number, signal?: AbortSignal) =>
  new Promise<void>((resolve, reject) => {
    const id = setTimeout(resolve, ms)
    signal?.addEventListener(
      'abort',
      () => {
        clearTimeout(id)
        reject(new DOMException('Aborted', 'AbortError'))
      },
      { once: true },
    )
  })

/**
 * Poll until `done` returns true (or the signal aborts). Resolves with the terminal snapshot.
 */
export async function poll<T>({
  fetch,
  done,
  onTick,
  intervalMs = 1500,
  signal,
}: PollOptions<T>): Promise<T> {
  const controller = new AbortController()
  signal?.addEventListener('abort', () => controller.abort(), { once: true })

  // eslint-disable-next-line no-constant-condition
  while (true) {
    const value = await fetch(controller.signal)
    onTick?.(value)
    if (done(value)) {
      return value
    }
    await delay(intervalMs, controller.signal)
  }
}
