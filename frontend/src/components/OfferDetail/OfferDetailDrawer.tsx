import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type {
  ApplicationInput,
  OfferDetailDto,
  TailoredCvState,
  UserStatus,
} from '../../api/types.ts'
import { getOfferDetail } from '../../api/offers.ts'
import { ApiError } from '../../api/client.ts'
import { enrichmentStatusClass, statusChipClass } from '../../theme/index.ts'
import { formatDateUtc, formatSalaryBand, formatWorkMode, titleCase } from '../../lib/format.ts'
import { AffinityBreakdown, FitBreakdown } from '../OfferSignals/OfferSignals.tsx'
import { ApplyModal } from '../ApplyModal/ApplyModal.tsx'
import { TailorCvModal } from '../TailorCvModal/TailorCvModal.tsx'
import './OfferDetailDrawer.css'

interface Props {
  offerId: string
  onClose: () => void
  /** Triage callbacks — the same set `OfferCard` takes, so the drawer is a full decision surface (#8). */
  onSetStatus?: (offerId: string, status: Exclude<UserStatus, 'new'>) => void | Promise<void>
  onMarkApplied?: (offerId: string, input: ApplicationInput) => Promise<void> | void
  onClearApplied?: (offerId: string) => Promise<void> | void
  onOpenApplication?: (offerId: string) => void
  /** Lifecycle of this offer's tailored CV (drives the badge + the reopen label). */
  tailoredState?: TailoredCvState
  onTailoredChanged?: () => void
  /** The visible feed order + a navigate callback → prev/next inside the drawer (#9). */
  offerIds?: string[]
  onNavigate?: (offerId: string) => void
}

/**
 * Read the full offer in-app AND act on it (US2): renders the server-**sanitised** `descriptionHtml`,
 * the facts + full fit/affinity breakdown (#10) + version/event history, the card's triage actions in
 * the footer (#8), and prev/next navigation over the visible feed (#9). A null body shows a clear
 * "description not available" state. Reuses the `.modal*` portal + focus idiom.
 */
export function OfferDetailDrawer({
  offerId,
  onClose,
  onSetStatus,
  onMarkApplied,
  onClearApplied,
  onOpenApplication,
  tailoredState,
  onTailoredChanged,
  offerIds,
  onNavigate,
}: Props) {
  const [detail, setDetail] = useState<OfferDetailDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [showApplyModal, setShowApplyModal] = useState(false)
  const [savingApply, setSavingApply] = useState(false)
  const [showTailorModal, setShowTailorModal] = useState(false)
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)

  const loadDetail = useCallback(
    async (signal?: AbortSignal) => {
      try {
        const d = await getOfferDetail(offerId, signal)
        setDetail(d)
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setError(e instanceof ApiError ? e.message : 'Failed to load the offer.')
      }
    },
    [offerId],
  )

  // Reset first so the previous offer's body never lingers while the next one loads (#9).
  useEffect(() => {
    setDetail(null)
    setError(null)
    const controller = new AbortController()
    void loadDetail(controller.signal)
    return () => controller.abort()
  }, [loadDetail])

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    return () => previouslyFocused?.focus?.()
  }, [])

  // Prev/next over the visible feed — recomputed every render since the feed can refetch while open (#9).
  const index = offerIds ? offerIds.indexOf(offerId) : -1
  const hasPrev = index > 0
  const hasNext = !!offerIds && index >= 0 && index < offerIds.length - 1
  const goPrev = useCallback(() => {
    if (offerIds && index > 0) onNavigate?.(offerIds[index - 1])
  }, [offerIds, index, onNavigate])
  const goNext = useCallback(() => {
    if (offerIds && index >= 0 && index < offerIds.length - 1) onNavigate?.(offerIds[index + 1])
  }, [offerIds, index, onNavigate])

  const nestedModalOpen = showApplyModal || showTailorModal

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      // A nested modal owns the keyboard — don't close or navigate the drawer underneath it (#8).
      if (nestedModalOpen || e.defaultPrevented) return
      if (e.key === 'Escape') onClose()
      else if (e.key === 'ArrowLeft') goPrev()
      else if (e.key === 'ArrowRight') goNext()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [nestedModalOpen, onClose, goPrev, goNext])

  // Run a parent triage callback, then re-fetch so the drawer's badges reflect the new state (#8).
  const runAction = useCallback(
    async (fn?: () => void | Promise<void>) => {
      await fn?.()
      await loadDetail()
    },
    [loadDetail],
  )

  async function handleSaveApplication(input: ApplicationInput) {
    if (!onMarkApplied || !detail) return
    setSavingApply(true)
    try {
      await onMarkApplied(detail.offer.offerId, input)
      setShowApplyModal(false)
      await loadDetail()
    } finally {
      setSavingApply(false)
    }
  }

  const offer = detail?.offer

  return createPortal(
    <>
      <div className="modal-overlay" onMouseDown={onClose}>
        <div
          className="modal offer-detail"
          role="dialog"
          aria-modal="true"
          aria-labelledby={titleId}
          ref={dialogRef}
          onMouseDown={(e) => e.stopPropagation()}
        >
          {!offer ? (
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
                    {offer.title}
                  </h2>
                  <p className="offer-detail__company">{offer.company}</p>
                  <div className="offer-detail__badges">
                    {offer.userStatus !== 'new' && (
                      <span className={statusChipClass(offer.userStatus)}>
                        {titleCase(offer.userStatus)}
                      </span>
                    )}
                    {offer.applied && (
                      <span
                        className="chip chip--applied"
                        title={offer.applicationNote ?? undefined}
                      >
                        Applied{offer.appliedAt ? ` · ${formatDateUtc(offer.appliedAt)}` : ''}
                      </span>
                    )}
                    {tailoredState && (
                      <span
                        className={enrichmentStatusClass(tailoredState)}
                        data-testid="offer-detail-tailored"
                      >
                        Tailored CV{tailoredState === 'produced' ? '' : ` · ${tailoredState}`}
                      </span>
                    )}
                  </div>
                </div>
                <div className="offer-detail__head-controls">
                  {offerIds && offerIds.length > 1 && (
                    <div className="offer-detail__nav" data-testid="offer-detail-nav">
                      <button
                        type="button"
                        className="offer-detail__icon-btn"
                        onClick={goPrev}
                        disabled={!hasPrev}
                        aria-label="Previous offer"
                      >
                        ‹
                      </button>
                      <span className="offer-detail__nav-count muted text-sm">
                        {index + 1} of {offerIds.length}
                      </span>
                      <button
                        type="button"
                        className="offer-detail__icon-btn"
                        onClick={goNext}
                        disabled={!hasNext}
                        aria-label="Next offer"
                      >
                        ›
                      </button>
                    </div>
                  )}
                  <button
                    type="button"
                    className="offer-detail__icon-btn"
                    onClick={onClose}
                    aria-label="Close offer detail"
                  >
                    ✕
                  </button>
                </div>
              </header>

              <div className="offer-detail__facts">
                {offer.location && <span>{offer.location}</span>}
                <span>{formatWorkMode(offer.workMode)}</span>
                {offer.seniority && <span>{titleCase(offer.seniority)}</span>}
                {offer.employmentType && <span>{offer.employmentType.toUpperCase()}</span>}
              </div>

              {(offer.fit || offer.affinity) && (
                <div className="offer-detail__signals">
                  {offer.fit && <FitBreakdown fit={offer.fit} />}
                  {offer.affinity && <AffinityBreakdown affinity={offer.affinity} />}
                </div>
              )}

              {offer.salaryBands.length > 0 && (
                <ul className="offer-detail__bands">
                  {offer.salaryBands.map((band, i) => (
                    <li key={i}>{formatSalaryBand(band)}</li>
                  ))}
                </ul>
              )}

              {offer.requiredSkills.length > 0 && (
                <div className="offer-detail__chips">
                  {offer.requiredSkills.map((s) => (
                    <span key={s} className="chip chip--skill">
                      {s}
                    </span>
                  ))}
                </div>
              )}

              <section className="offer-detail__body" data-testid="offer-detail-body">
                {detail.descriptionHtml ? (
                  // Safe: the backend already sanitised this HTML with Ganss.Xss (FR-015).
                  <div
                    className="offer-detail__description"
                    dangerouslySetInnerHTML={{ __html: detail.descriptionHtml }}
                  />
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
                        <span className="muted text-sm">
                          {new Date(e.occurredAt).toLocaleString()}
                        </span>{' '}
                        {e.type}
                      </li>
                    ))}
                  </ul>
                </details>
              )}

              <footer className="offer-detail__foot">
                <a
                  className="btn btn--primary btn--sm"
                  href={offer.canonicalUrl}
                  target="_blank"
                  rel="noreferrer noopener"
                  data-testid="offer-detail-external"
                >
                  View original ↗
                </a>
                <button
                  type="button"
                  className="btn btn--ghost btn--sm"
                  onClick={() => setShowTailorModal(true)}
                >
                  {tailoredState ? 'Tailored CV' : 'Tailor CV'}
                </button>
                {onSetStatus &&
                  (offer.userStatus === 'dismissed' ? (
                    <button
                      type="button"
                      className="btn btn--ghost btn--sm"
                      onClick={() => runAction(() => onSetStatus(offer.offerId, 'viewed'))}
                    >
                      Restore
                    </button>
                  ) : (
                    <>
                      <button
                        type="button"
                        className="btn btn--ghost btn--sm"
                        onClick={() => runAction(() => onSetStatus(offer.offerId, 'interested'))}
                      >
                        Interested
                      </button>
                      <button
                        type="button"
                        className="btn btn--ghost btn--sm"
                        onClick={() => runAction(() => onSetStatus(offer.offerId, 'dismissed'))}
                      >
                        Dismiss
                      </button>
                    </>
                  ))}
                {onMarkApplied &&
                  (offer.applied ? (
                    <>
                      {onOpenApplication && (
                        <button
                          type="button"
                          className="btn btn--ghost btn--sm"
                          onClick={() => onOpenApplication(offer.offerId)}
                        >
                          Application
                        </button>
                      )}
                      <button
                        type="button"
                        className="btn btn--ghost btn--sm"
                        onClick={() => setShowApplyModal(true)}
                      >
                        Edit application
                      </button>
                      {onClearApplied && (
                        <button
                          type="button"
                          className="btn btn--ghost btn--sm"
                          onClick={() => runAction(() => onClearApplied(offer.offerId))}
                        >
                          Unmark
                        </button>
                      )}
                    </>
                  ) : (
                    <button
                      type="button"
                      className="btn btn--ghost btn--sm"
                      onClick={() => setShowApplyModal(true)}
                    >
                      Mark applied
                    </button>
                  ))}
              </footer>
            </>
          )}
        </div>
      </div>

      {showApplyModal && offer && (
        <ApplyModal
          offerTitle={offer.title}
          isEditing={offer.applied}
          initialAppliedAt={offer.appliedAt}
          initialNote={offer.applicationNote}
          onSave={handleSaveApplication}
          onClose={() => setShowApplyModal(false)}
          saving={savingApply}
        />
      )}

      {showTailorModal && offer && (
        <TailorCvModal
          offerId={offer.offerId}
          offerTitle={offer.title}
          onClose={() => setShowTailorModal(false)}
          onChanged={onTailoredChanged}
        />
      )}
    </>,
    document.body,
  )
}
