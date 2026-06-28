import type { OfferDto, UserStatus } from '../../api/types.ts'
import { statusChipClass, qualityChipClass, fitColorVar } from '../../theme/index.ts'
import { formatSalaryBand, formatWorkMode, titleCase } from '../../lib/format.ts'
import './OfferCard.css'

interface OfferCardProps {
  offer: OfferDto
  onSetStatus?: (offerId: string, status: Exclude<UserStatus, 'new'>) => void
  /** Split a wrongly-merged cross-source role group (US4). Called with the role group id. */
  onSplitGroup?: (roleGroupId: string) => void
}

export function OfferCard({ offer, onSetStatus, onSplitGroup }: OfferCardProps) {
  const hasSalary = offer.salaryBands.length > 0
  const groupMembers = offer.groupMembers ?? []
  const roleGroupId = offer.roleGroupId

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
        </div>
      </div>

      <div className="offer-card__meta">
        {offer.location && <span>{offer.location}</span>}
        <span>{formatWorkMode(offer.workMode)}</span>
        {offer.seniority && <span>{titleCase(offer.seniority)}</span>}
        {offer.employmentType && <span>{offer.employmentType.toUpperCase()}</span>}
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
        <div className="offer-card__fit">
          <span className="offer-card__fit-score" style={{ color: fitColorVar(offer.fit.score) }}>
            {offer.fit.score}
            <span className="offer-card__fit-max">/100 fit</span>
          </span>
          <div className="offer-card__fit-lists">
            {offer.fit.matched.length > 0 && (
              <div className="offer-card__chips">
                {offer.fit.matched.map((m) => (
                  <span key={m} className="chip chip--skill">
                    {m}
                  </span>
                ))}
              </div>
            )}
            {offer.fit.missing.length > 0 && (
              <div className="offer-card__chips">
                {offer.fit.missing.map((m) => (
                  <span key={m} className="chip chip--missing">
                    missing: {m}
                  </span>
                ))}
              </div>
            )}
          </div>
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

      <div className="offer-card__actions">
        <a className="btn btn--primary btn--sm" href={offer.canonicalUrl} target="_blank" rel="noreferrer noopener">
          View offer ↗
        </a>
        {onSetStatus && (
          <div className="offer-card__status-actions">
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
          </div>
        )}
      </div>
    </article>
  )
}
