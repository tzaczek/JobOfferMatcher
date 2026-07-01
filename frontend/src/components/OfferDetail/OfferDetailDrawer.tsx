import { useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { OfferDetailDto } from '../../api/types.ts'
import { getOfferDetail } from '../../api/offers.ts'
import { ApiError } from '../../api/client.ts'
import { fitColorVar } from '../../theme/index.ts'
import { formatSalaryBand, formatWorkMode, titleCase } from '../../lib/format.ts'
import './OfferDetailDrawer.css'

interface Props {
  offerId: string
  onClose: () => void
}

/**
 * Read the full offer in-app (US2): renders the server-**sanitised** `descriptionHtml` (safe to inject),
 * the facts + fit + affinity + version/event history, and always the external link. A null body shows a
 * clear "description not available" state (FR-011/013/014/015). Reuses the `.modal*` portal + focus idiom.
 */
export function OfferDetailDrawer({ offerId, onClose }: Props) {
  const [detail, setDetail] = useState<OfferDetailDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let active = true
    getOfferDetail(offerId)
      .then((d) => active && setDetail(d))
      .catch((e) => active && setError(e instanceof ApiError ? e.message : 'Failed to load the offer.'))
    return () => {
      active = false
    }
  }, [offerId])

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    return () => previouslyFocused?.focus?.()
  }, [])

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape' && !e.defaultPrevented) onClose()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  return createPortal(
    <div className="modal-overlay" onMouseDown={onClose}>
      <div
        className="modal offer-detail"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        ref={dialogRef}
        onMouseDown={(e) => e.stopPropagation()}
      >
        {detail === null ? (
          <div className="offer-detail__loading">
            <p className="muted">{error ?? 'Loading…'}</p>
            <button type="button" className="btn btn--ghost btn--sm" onClick={onClose}>
              Close
            </button>
          </div>
        ) : (
          <>
            <header className="offer-detail__head">
              <div className="offer-detail__head-titles">
                <h2 className="offer-detail__title" id={titleId}>
                  {detail.offer.title}
                </h2>
                <p className="offer-detail__company">{detail.offer.company}</p>
              </div>
              <button type="button" className="offer-detail__icon-btn" onClick={onClose} aria-label="Close offer detail">
                ✕
              </button>
            </header>

            <div className="offer-detail__facts">
              {detail.offer.location && <span>{detail.offer.location}</span>}
              <span>{formatWorkMode(detail.offer.workMode)}</span>
              {detail.offer.seniority && <span>{titleCase(detail.offer.seniority)}</span>}
              {detail.offer.employmentType && <span>{detail.offer.employmentType.toUpperCase()}</span>}
            </div>

            <div className="offer-detail__signals">
              {detail.offer.fit?.state === 'produced' && detail.offer.fit.score != null && (
                <span className="offer-detail__signal" style={{ color: fitColorVar(detail.offer.fit.score) }}>
                  Fit {detail.offer.fit.score}/100
                </span>
              )}
              {detail.offer.affinity?.state === 'produced' && detail.offer.affinity.score != null && (
                <span className="offer-detail__signal" style={{ color: fitColorVar(detail.offer.affinity.score) }}>
                  Affinity {detail.offer.affinity.score}/100
                </span>
              )}
            </div>

            {detail.offer.salaryBands.length > 0 && (
              <ul className="offer-detail__bands">
                {detail.offer.salaryBands.map((band, i) => (
                  <li key={i}>{formatSalaryBand(band)}</li>
                ))}
              </ul>
            )}

            {detail.offer.requiredSkills.length > 0 && (
              <div className="offer-detail__chips">
                {detail.offer.requiredSkills.map((s) => (
                  <span key={s} className="chip chip--skill">
                    {s}
                  </span>
                ))}
              </div>
            )}

            <section className="offer-detail__body" data-testid="offer-detail-body">
              {detail.descriptionHtml ? (
                // Safe: the backend already sanitised this HTML with Ganss.Xss (FR-015).
                <div className="offer-detail__description" dangerouslySetInnerHTML={{ __html: detail.descriptionHtml }} />
              ) : (
                <p className="muted" data-testid="offer-detail-unavailable">
                  Description not available — open the original posting for the full text.
                </p>
              )}
            </section>

            {detail.events.length > 0 && (
              <details className="offer-detail__history">
                <summary>History ({detail.events.length})</summary>
                <ul className="offer-detail__events">
                  {detail.events.map((e, i) => (
                    <li key={i}>
                      <span className="muted text-sm">{new Date(e.occurredAt).toLocaleString()}</span> {e.type}
                    </li>
                  ))}
                </ul>
              </details>
            )}

            <footer className="offer-detail__foot">
              <a
                className="btn btn--primary btn--sm"
                href={detail.offer.canonicalUrl}
                target="_blank"
                rel="noreferrer noopener"
                data-testid="offer-detail-external"
              >
                View original ↗
              </a>
            </footer>
          </>
        )}
      </div>
    </div>,
    document.body,
  )
}
