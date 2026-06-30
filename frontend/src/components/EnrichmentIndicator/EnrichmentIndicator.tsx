import { useCallback, useEffect, useState } from 'react'
import type { EnrichmentStatusDto } from '../../api/types.ts'
import { getEnrichmentStatus, triggerRerun } from '../../api/enrichment.ts'
import { enrichmentStatusClass } from '../../theme/index.ts'
import './EnrichmentIndicator.css'

/**
 * Global pending/failed enrichment indicator + manual re-run (FR-009/FR-010/FR-016/SC-007).
 * The re-run only re-arms app state — the user then runs the Claude <code>/enrich</code> worker, which
 * is the sole producer. Shown on the feed so the user always knows whether outputs are up to date.
 */
export function EnrichmentIndicator() {
  const [status, setStatus] = useState<EnrichmentStatusDto | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      setStatus(await getEnrichmentStatus(signal))
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return
      // Non-fatal: the indicator simply stays hidden if status can't be read.
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  async function rerun(scope: 'failed' | 'all') {
    setBusy(true)
    try {
      setStatus(await triggerRerun(scope))
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
    </div>
  )
}
