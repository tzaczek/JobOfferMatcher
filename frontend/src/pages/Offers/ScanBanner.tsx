import type { ScanStatusDto } from '../../api/types.ts'

interface ScanBannerProps {
  scanning: boolean
  status: ScanStatusDto | null
  error: string | null
}

/** Live scan feedback: running / completed / incomplete (FR-020/036). */
export function ScanBanner({ scanning, status, error }: ScanBannerProps) {
  if (error) {
    return (
      <div className="scan-banner scan-banner--error" role="alert">
        {error}
      </div>
    )
  }

  if (!scanning && !status) return null

  if (status?.state === 'completed') {
    const c = status.counts
    return (
      <div className="scan-banner scan-banner--ok" role="status">
        Scan complete — {c.collected} collected, {c.new} new, {c.updated} updated.
      </div>
    )
  }

  if (status?.state === 'incomplete') {
    return (
      <div className="scan-banner scan-banner--warn" role="status">
        Scan incomplete{status.incompleteReason ? ` (${status.incompleteReason})` : ''} — partial results shown.
      </div>
    )
  }

  return (
    <div className="scan-banner scan-banner--running" role="status">
      <span className="spinner" aria-hidden="true" /> Scanning…
      {status ? ` collected ${status.counts.collected}` : ''}
    </div>
  )
}
