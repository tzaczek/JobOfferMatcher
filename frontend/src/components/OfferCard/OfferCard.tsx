import { useMemo, useState } from 'react'
import type { ApplicationInput, OfferDto, TailoredCvState, UserStatus } from '../../api/types.ts'
import { statusChipClass, qualityChipClass, enrichmentStatusClass } from '../../theme/index.ts'
import {
  formatDate,
  formatDateUtc,
  formatSalaryBand,
  formatWorkMode,
  titleCase,
} from '../../lib/format.ts'
import { ApplyModal } from '../ApplyModal/ApplyModal.tsx'
import { TailorCvModal } from '../TailorCvModal/TailorCvModal.tsx'
import { AffinityBreakdown, FitBreakdown } from '../OfferSignals/OfferSignals.tsx'
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
  /** Lifecycle of this offer's tailored CV, when one exists (drives the indicator + the reopen label). */
  tailoredState?: TailoredCvState
  /** Refresh the tailored-CV lookup after a generate/regenerate/remove. */
  onTailoredChanged?: () => void
  /** Current pipeline-stage name when this applied offer has an application (US1). */
  applicationStageName?: string
  /** Open the application detail drawer for this offer (shown when applied). */
  onOpenApplication?: (offerId: string) => void
  /** Open the offer-detail drawer (full body + facts) — US2. */
  onOpenDetail?: (offerId: string) => void
  /** Briefly flashes the card — used to draw the eye after a deep-link scroll-to (e.g. from Tailored CVs). */
  highlighted?: boolean
  /** Hide the per-card affinity cold-start line — the page-level hint already covers it (finding #7). */
  suppressAffinityInsufficient?: boolean
}

export function OfferCard({
  offer,
  onSetStatus,
  onMarkApplied,
  onClearApplied,
  onSplitGroup,
  tailoredState,
  onTailoredChanged,
  applicationStageName,
  onOpenApplication,
  onOpenDetail,
  highlighted,
  suppressAffinityInsufficient,
}: OfferCardProps) {
  const hasSalary = offer.salaryBands.length > 0
  const groupMembers = offer.groupMembers ?? []
  const roleGroupId = offer.roleGroupId
  const enrichmentState = offer.enrichmentState ?? 'pending'

  // One deduped skills row (finding #7): missing first (red), then matched/key/required (neutral),
  // case-insensitively first-seen-wins — so a skill from multiple sources shows exactly once.
  const skillRow = useMemo(() => {
    const seen = new Set<string>()
    const row: { label: string; missing: boolean }[] = []
    const add = (skill: string, missing: boolean) => {
      const key = skill.toLowerCase()
      if (seen.has(key)) return
      seen.add(key)
      row.push({ label: skill, missing })
    }
    ;(offer.fit?.missing ?? []).forEach((s) => add(s, true))
    ;(offer.fit?.matched ?? []).forEach((s) => add(s, false))
    ;(offer.keySkills ?? []).forEach((s) => add(s, false))
    offer.requiredSkills.forEach((s) => add(s, false))
    return row
  }, [offer.fit, offer.keySkills, offer.requiredSkills])

  const showAffinity =
    offer.affinity &&
    !(suppressAffinityInsufficient && offer.affinity.state === 'insufficient')

  const [showApplyModal, setShowApplyModal] = useState(false)
  const [savingApply, setSavingApply] = useState(false)
  const [showTailorModal, setShowTailorModal] = useState(false)

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
    <article
      className={highlighted ? 'card offer-card offer-card--highlight' : 'card offer-card'}
      data-testid="offer-card"
      data-offer-id={offer.offerId}
    >
      <div className="offer-card__head">
        <div className="offer-card__title-block">
          {onOpenDetail ? (
            <button
              type="button"
              className="offer-card__title offer-card__title-btn"
              onClick={() => onOpenDetail(offer.offerId)}
              data-testid="open-offer-detail"
            >
              {offer.title}
            </button>
          ) : (
            <h3 className="offer-card__title">{offer.title}</h3>
          )}
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
          {offer.applied && applicationStageName && (
            <span className="chip chip--interested" data-testid="application-stage-chip">
              {applicationStageName}
            </span>
          )}
          {tailoredState && (
            <span className={enrichmentStatusClass(tailoredState)} data-testid="tailored-indicator">
              Tailored CV{tailoredState === 'produced' ? '' : ` · ${tailoredState}`}
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
          <p className="offer-card__summary">{offer.summary}</p>
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

      {offer.fit && <FitBreakdown fit={offer.fit} compact />}

      {showAffinity && offer.affinity && <AffinityBreakdown affinity={offer.affinity} compact />}

      {skillRow.length > 0 && (
        <div className="offer-card__skills" data-testid="offer-skills">
          {skillRow.map((s) => (
            <span key={s.label} className={s.missing ? 'chip chip--missing' : 'chip chip--skill'}>
              {s.missing ? `missing: ${s.label}` : s.label}
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
        <a
          className="btn btn--primary btn--sm"
          href={offer.canonicalUrl}
          target="_blank"
          rel="noreferrer noopener"
        >
          View offer ↗
        </a>
        {onOpenDetail && (
          <button
            type="button"
            className="btn btn--ghost btn--sm"
            onClick={() => onOpenDetail(offer.offerId)}
          >
            Details
          </button>
        )}
        <button
          type="button"
          className="btn btn--ghost btn--sm"
          onClick={() => setShowTailorModal(true)}
        >
          {tailoredState ? 'Tailored CV' : 'Tailor CV'}
        </button>
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
                {onOpenApplication && (
                  <button
                    type="button"
                    className="btn btn--ghost btn--sm"
                    onClick={() => onOpenApplication(offer.offerId)}
                    data-testid="open-application"
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
                    onClick={() => onClearApplied(offer.offerId)}
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

      {showTailorModal && (
        <TailorCvModal
          offerId={offer.offerId}
          offerTitle={offer.title}
          onClose={() => setShowTailorModal(false)}
          onChanged={onTailoredChanged}
        />
      )}
    </article>
  )
}
