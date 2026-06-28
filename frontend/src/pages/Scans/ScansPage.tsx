import { useEffect, useState } from 'react'
import type { ScanRunSummaryDto } from '../../api/types.ts'
import { listScans } from '../../api/scans.ts'
import { ApiError } from '../../api/client.ts'
import { formatDate } from '../../lib/format.ts'
import './ScansPage.css'

export function ScansPage() {
  const [runs, setRuns] = useState<ScanRunSummaryDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    listScans(controller.signal)
      .then((r) => setRuns(r.data))
      .catch((e) => {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setError(e instanceof ApiError ? e.message : 'Failed to load scan history.')
      })
    return () => controller.abort()
  }, [])

  return (
    <section>
      <h1 className="page-title">Scan history</h1>
      <p className="page-subtitle">Every scan that has run, on demand or scheduled.</p>

      {error && (
        <div className="state-block" role="alert">
          {error}
        </div>
      )}
      {!error && runs === null && (
        <div className="state-block">
          <span className="spinner" aria-label="Loading" /> Loading…
        </div>
      )}
      {!error && runs !== null && runs.length === 0 && (
        <div className="state-block">No scans have run yet.</div>
      )}
      {!error && runs !== null && runs.length > 0 && (
        <table className="scan-table">
          <thead>
            <tr>
              <th>Started</th>
              <th>Trigger</th>
              <th>Outcome</th>
              <th>Collected</th>
              <th>New</th>
              <th>Updated</th>
              <th>Unavailable</th>
              <th>Note</th>
            </tr>
          </thead>
          <tbody>
            {runs.map((run) => (
              <tr key={run.scanRunId}>
                <td>{formatDate(run.startedAt)}</td>
                <td>{run.trigger}</td>
                <td>
                  <span className={`chip chip--${outcomeClass(run.outcome)}`}>{run.outcome ?? 'running'}</span>
                </td>
                <td>{run.counts.collected}</td>
                <td>{run.counts.new}</td>
                <td>{run.counts.updated}</td>
                <td>{run.counts.unavailable}</td>
                <td className="muted text-sm">{run.incompleteReason ?? ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

function outcomeClass(outcome: string | null): string {
  if (outcome === 'complete') return 'interested'
  if (outcome === 'partial') return 'updated'
  if (outcome === 'failed') return 'missing'
  return 'unavailable'
}
