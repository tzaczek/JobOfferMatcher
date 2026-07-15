import type { ScanStatusDto } from '../../api/types.ts'

interface ScanBannerProps {
  scanning: boolean
  status: ScanStatusDto | null
  error: string | null
  /**
   * The user kicked off a scan that includes a login-required source (LinkedIn, feature 008). Client-
   * driven — the backend `/scans/{id}/status` never emits `waiting_for_login`; the headed browser IS the
   * login surface, so we show an optimistic hint pointing the user to the window that opened.
   */
  awaitingLogin?: boolean
}

/** Live scan feedback: running / completed / incomplete (FR-020/036). */
export function ScanBanner({ scanning, status, error, awaitingLogin = false }: ScanBannerProps) {
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
    // A LinkedIn scan that couldn't sign in ends here (feature 008, US3) — steer the user to a manual scan.
    if (status.incompleteReason === 'LoginNotCompleted') {
      return (
        <div className="scan-banner scan-banner--warn" role="status" data-testid="login-required">
          LinkedIn login required — run a manual scan to sign in.
        </div>
      )
    }
    return (
      <div className="scan-banner scan-banner--warn" role="status">
        Scan incomplete{status.incompleteReason ? ` (${status.incompleteReason})` : ''} — partial
        results shown.
      </div>
    )
  }

  return (
    <div className="scan-banner scan-banner--running" role="status">
      <span className="spinner" aria-hidden="true" /> Scanning…
      {status ? ` collected ${status.counts.collected}` : ''}
      {awaitingLogin && (
        <span className="scan-banner__login-hint" data-testid="login-hint">
          {' '}
          A LinkedIn login window opened — finish signing in there.
        </span>
      )}
    </div>
  )
}
