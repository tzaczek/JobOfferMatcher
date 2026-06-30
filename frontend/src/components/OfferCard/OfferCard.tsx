import { useState } from 'react'
import type { ApplicationInput, OfferDto, UserStatus } from '../../api/types.ts'
import { statusChipClass, qualityChipClass, enrichmentStatusClass, fitColorVar } from '../../theme/index.ts'
import { formatDate, formatDateUtc, formatSalaryBand, formatWorkMode, titleCase } from '../../lib/format.ts'
import { ApplyModal } from '../ApplyModal/ApplyModal.tsx'
import './OfferCard.css'

interface OfferCardProps {
  offer: OfferDto
  onSetStatus?: (offerId: string, status: Exclude<UserStatus, 'new'>) => void
  /** Mark applied / edit the applied date+note. */
  onMarkApplied?: (offerId: string, input: ApplicationInput) => Promise<void> | void
  /** Clear the applied flag (un-apply). */
  onClearApplied?: (offerId: string) => Promise<void> | void
  /** Split a wrongly-merged cross-source role group (US4). Called with the role group id. */
  onSplitGroup?: (roleGroupId: string) => void
}

export function OfferCard({ offer, onSetStatus, onMarkApplied, onClearApplied, onSplitGroup }: OfferCardProps) {
  const hasSalary = offer.salaryBands.length > 0
  const groupMembers = offer.groupMembers ?? []
  const roleGroupId = offer.roleGroupId
  const enrichmentState = offer.enrichmentState ?? 'pending'
  const keySkills = offer.keySkills ?? []

  const [showApplyModal, setShowApplyModal] = useState(false)
  const [savingApply, setSavingApply] = useState(false)

  async function handleSaveApplication(input: ApplicationInput) {
    if (!onMarkApplied) return
    setSavingApply(true)
    try {
      await onMarkApplied(offer.offerId, input)
      setShowApplyModal(false)
    } finally {
      setSavingApply(false)
    }
  }

  return (
    <article className="card offer-card" data-testid="offer-card">
      <div className="offer-card__head">
        <div className="offer-card__title-block">
          <h3 className="offer-card__title">{offer.title}</h3>
          <div className="offer-card__company">{offer.company}</div>
        </div>
        <div className="offer-card__badges">
          {offer.isNew && <span className="chip chip--new">New</span>}
          {offer.isUpdated && <span className="chip chip--updated">Updated</span>}
          {offer.availability === 'no_longer_available' && (
            <span className="chip chip--unavailable">No longer available</span>
          )}
          {offer.userStatus !== 'new' && (
            <span className={statusChipClass(offer.userStatus)}>{titleCase(offer.userStatus)}</span>
          )}
          {offer.applied && (
            <span className="chip chip--applied" title={offer.applicationNote ?? undefined}>
              Applied{offer.appliedAt ? ` · ${formatDateUtc(offer.appliedAt)}` : ''}
            </span>
          )}
        </div>
      </div>

      <div className="offer-card__meta">
        {offer.location && <span>{offer.location}</span>}
        <span>{formatWorkMode(offer.workMode)}</span>
        {offer.seniority && <span>{titleCase(offer.seniority)}</span>}
        {offer.employmentType && <span>{offer.employmentType.toUpperCase()}</span>}
        {offer.publishedAt && <span>Published {formatDate(offer.publishedAt)}</span>}
      </div>

      <div className="offer-card__enrichment" data-testid="offer-enrichment">
        {enrichmentState === 'produced' && offer.summary ? (
          <>
            <p className="offer-card__summary">{offer.summary}</p>
            {keySkills.length > 0 && (
              <div className="offer-card__chips offer-card__key-skills">
                {keySkills.map((s) => (
                  <span key={s} className="chip chip--skill">
                    {s}
                  </span>
                ))}
              </div>
            )}
          </>
        ) : enrichmentState === 'failed' ? (
          <span className={enrichmentStatusClass('failed')}>Summary unavailable</span>
        ) : (
          <span className={enrichmentStatusClass('pending')}>Summary pending</span>
        )}
      </div>

      <div className="offer-card__salary">
        {hasSalary ? (
          <ul className="offer-card__bands">
            {offer.salaryBands.map((band, i) => (
              <li key={i} className="offer-card__band">
                {formatSalaryBand(band)}
              </li>
            ))}
          </ul>
        ) : (
          <span className="muted">Salary not disclosed</span>
        )}
        {offer.normalizedSalary && (
          <div className="offer-card__normalized">
            <span className="muted text-sm">
              ≈ {Math.round(offer.normalizedSalary.comparableMonthly.amount).toLocaleString()}{' '}
              {offer.normalizedSalary.comparableMonthly.currency}/mo (est.)
            </span>
            <span className={qualityChipClass(offer.normalizedSalary.quality)}>
              {offer.normalizedSalary.quality}
            </span>
          </div>
        )}
      </div>

      {offer.fit && (
        <div className="offer-card__fit" data-testid="offer-fit">
          {offer.fit.state === 'produced' && offer.fit.score != null ? (
            <>
              <span className="offer-card__fit-score" style={{ color: fitColorVar(offer.fit.score) }}>
                {offer.fit.score}
                <span className="offer-card__fit-max">/100 fit</span>
              </span>
              <div className="offer-card__fit-lists">
                {offer.fit.rationale && <p className="offer-card__fit-rationale">{offer.fit.rationale}</p>}
                {(offer.fit.matched ?? []).length > 0 && (
                  <div className="offer-card__chips">
                    {(offer.fit.matched ?? []).map((m) => (
                      <span key={m} className="chip chip--skill">
                        {m}
                      </span>
                    ))}
                  </div>
                )}
                {(offer.fit.missing ?? []).length > 0 && (
                  <div className="offer-card__chips">
                    {(offer.fit.missing ?? []).map((m) => (
                      <span key={m} className="chip chip--missing">
                        missing: {m}
                      </span>
                    ))}
                  </div>
                )}
              </div>
            </>
          ) : offer.fit.state === 'failed' ? (
            <span className={enrichmentStatusClass('failed')}>Fit unavailable</span>
          ) : (
            <span className={enrichmentStatusClass('pending')}>Fit pending</span>
          )}
        </div>
      )}

      {offer.requiredSkills.length > 0 && (
        <div className="offer-card__skills">
          {offer.requiredSkills.map((skill) => (
            <span key={skill} className="chip chip--skill">
              {skill}
            </span>
          ))}
        </div>
      )}

      {groupMembers.length > 0 && (
        <div className="offer-card__group">
          <span className="offer-card__group-label">Also posted at</span>
          <ul className="offer-card__group-list">
            {groupMembers.map((member) => (
              <li key={member.offerId}>
                <a
                  className="offer-card__group-link"
                  href={member.canonicalUrl}
                  target="_blank"
                  rel="noreferrer noopener"
                >
                  {member.sourceName} ↗
                </a>
              </li>
            ))}
          </ul>
          {onSplitGroup && roleGroupId && (
            <button
              type="button"
              className="btn btn--ghost btn--sm offer-card__group-split"
              onClick={() => onSplitGroup(roleGroupId)}
            >
              Not the same role
            </button>
          )}
        </div>
      )}

      {offer.applied && offer.applicationNote && (
        <p className="offer-card__application-note">“{offer.applicationNote}”</p>
      )}

      <div className="offer-card__actions">
        <a className="btn btn--primary btn--sm" href={offer.canonicalUrl} target="_blank" rel="noreferrer noopener">
          View offer ↗
        </a>
        <div className="offer-card__status-actions">
          {onSetStatus &&
            (offer.userStatus === 'dismissed' ? (
              // Restore lifts the offer back into the default feed. It can't return to "new"
              // (SC-002 — a dismissed offer never re-appears as new), so it becomes "viewed".
              <button
                type="button"
                className="btn btn--ghost btn--sm"
                onClick={() => onSetStatus(offer.offerId, 'viewed')}
              >
                Restore
              </button>
            ) : (
              <>
                <button
                  type="button"
                  className="btn btn--ghost btn--sm"
                  onClick={() => onSetStatus(offer.offerId, 'interested')}
                >
                  Interested
                </button>
                <button
                  type="button"
                  className="btn btn--ghost btn--sm"
                  onClick={() => onSetStatus(offer.offerId, 'dismissed')}
                >
                  Dismiss
                </button>
              </>
            ))}
          {onMarkApplied &&
            (offer.applied ? (
              <>
                <button type="button" className="btn btn--ghost btn--sm" onClick={() => setShowApplyModal(true)}>
                  Edit application
                </button>
                {onClearApplied && (
                  <button
                    type="button"
                    className="btn btn--ghost btn--sm"
                    onClick={() => onClearApplied(offer.offerId)}
                  >
                    Unmark
                  </button>
                )}
              </>
            ) : (
              <button type="button" className="btn btn--ghost btn--sm" onClick={() => setShowApplyModal(true)}>
                Mark applied
              </button>
            ))}
        </div>
      </div>

      {showApplyModal && (
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
    </article>
  )
}
