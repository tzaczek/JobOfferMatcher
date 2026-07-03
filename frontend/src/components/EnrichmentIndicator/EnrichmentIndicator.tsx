import { useCallback, useEffect, useRef, useState } from 'react'
import type { EnrichmentStatusDto } from '../../api/types.ts'
import { getEnrichmentStatus, triggerRerun } from '../../api/enrichment.ts'
import { ApiError } from '../../api/client.ts'
import { enrichmentStatusClass } from '../../theme/index.ts'
import { poll } from '../../lib/polling.ts'
import './EnrichmentIndicator.css'

interface EnrichmentIndicatorProps {
  /** Bumping this re-fetches status — the parent bumps it after a scan completes (finding #1). */
  refreshKey?: number
  /** Fired ONCE when a pending queue drains to zero, so the parent reloads the feed to show produced outputs. */
  onProduced?: () => void
}

/**
 * Global pending/failed enrichment indicator + manual re-run (FR-001).
 * The re-run only re-arms app state — the user then runs the Claude <code>/enrich</code> worker, which
 * is the sole producer. It now stays live: it re-fetches after a scan and, while work is pending, polls
 * every ~5s until the queue drains (then reloads the feed once — never per tick, to avoid mid-triage
 * reshuffle).
 */
export function EnrichmentIndicator({ refreshKey, onProduced }: EnrichmentIndicatorProps) {
  const [status, setStatus] = useState<EnrichmentStatusDto | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Keep the latest onProduced without re-arming the poll effect on every parent render.
  const onProducedRef = useRef(onProduced)
  useEffect(() => {
    onProducedRef.current = onProduced
  }, [onProduced])

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      setStatus(await getEnrichmentStatus(signal))
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return
      // Non-fatal: the indicator simply stays hidden if status can't be read.
    }
  }, [])

  // (Re)fetch on mount and whenever the parent bumps refreshKey (e.g. a scan just finished).
  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load, refreshKey])

  // While there is pending work, poll ~5s until it drains, then reload the feed ONCE. Gate on a stable
  // boolean (not the changing count) so onTick updates don't restart the poll every 5s.
  const hasPending = (status?.pendingTotal ?? 0) > 0
  useEffect(() => {
    if (!hasPending) return
    const controller = new AbortController()
    poll<EnrichmentStatusDto>({
      fetch: (signal) => getEnrichmentStatus(signal),
      done: (s) => s.pendingTotal === 0,
      onTick: setStatus,
      intervalMs: 5000,
      signal: controller.signal,
    })
      .then(() => onProducedRef.current?.())
      .catch(() => {
        // Aborted on unmount / when pending returns to 0 — stop quietly.
      })
    return () => controller.abort()
  }, [hasPending])

  async function rerun(scope: 'failed' | 'all') {
    setBusy(true)
    setError(null)
    try {
      setStatus(await triggerRerun(scope))
    } catch (e) {
      // A failed re-run must say so, not appear to do nothing (finding #1).
      setError(e instanceof ApiError ? e.message : 'Re-run failed — please try again.')
    } finally {
      setBusy(false)
    }
  }

  if (!status) return null

  const { pendingTotal, failedTotal } = status
  const upToDate = pendingTotal === 0 && failedTotal === 0

  return (
    <div className="enrichment-indicator" data-testid="enrichment-indicator">
      <div className="enrichment-indicator__chips">
        {upToDate ? (
          <span className={enrichmentStatusClass('produced')}>AI enrichment up to date</span>
        ) : (
          <>
            {pendingTotal > 0 && (
              <span className={enrichmentStatusClass('pending')} data-testid="pending-count">
                {pendingTotal} pending
              </span>
            )}
            {failedTotal > 0 && (
              <span className={enrichmentStatusClass('failed')} data-testid="failed-count">
                {failedTotal} failed
              </span>
            )}
          </>
        )}
      </div>

      {pendingTotal > 0 && (
        <span className="enrichment-indicator__hint muted text-sm">
          Run <code>/enrich</code> in Claude Code to process them.
        </span>
      )}

      <div className="enrichment-indicator__actions">
        {failedTotal > 0 && (
          <button
            type="button"
            className="btn btn--ghost btn--sm"
            onClick={() => rerun('failed')}
            disabled={busy}
          >
            Re-run failed
          </button>
        )}
        <button
          type="button"
          className="btn btn--ghost btn--sm"
          onClick={() => rerun('all')}
          disabled={busy}
        >
          Re-run all
        </button>
      </div>

      {error && (
        <span className="enrichment-indicator__error" role="alert" data-testid="rerun-error">
          {error}
        </span>
      )}
    </div>
  )
}
