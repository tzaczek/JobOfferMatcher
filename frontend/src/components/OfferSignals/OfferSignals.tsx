import type { AffinityDto, FitDto } from '../../api/types.ts'
import { enrichmentStatusClass, fitColorVar } from '../../theme/index.ts'
import './OfferSignals.css'

/**
 * The fit + affinity breakdown, extracted so the feed card and the detail drawer render from ONE
 * source (finding #10 — the drawer previously showed only bare scores). Every lifecycle state is
 * preserved verbatim from the old inline `OfferCard` blocks.
 *
 * `compact` (the card): collapses the rationale behind a `details/summary` and — for fit — hides the
 * matched/missing chips, because the card renders one deduped skills row instead (finding #7). Full
 * mode (the drawer, the default) shows the rationale expanded and every chip.
 */

interface FitBreakdownProps {
  fit: FitDto
  compact?: boolean
}

export function FitBreakdown({ fit, compact }: FitBreakdownProps) {
  return (
    <div className="offer-card__fit" data-testid="offer-fit">
      {fit.state === 'produced' && fit.score != null ? (
        <>
          <span className="offer-card__fit-score" style={{ color: fitColorVar(fit.score) }}>
            {fit.score}
            <span className="offer-card__fit-max">/100 fit</span>
          </span>
          <div className="offer-card__fit-lists">
            {fit.rationale &&
              (compact ? (
                <details className="offer-signals__why">
                  <summary>Why this fit</summary>
                  <p className="offer-card__fit-rationale">{fit.rationale}</p>
                </details>
              ) : (
                <p className="offer-card__fit-rationale">{fit.rationale}</p>
              ))}
            {!compact && (fit.matched ?? []).length > 0 && (
              <div className="offer-card__chips">
                {(fit.matched ?? []).map((m) => (
                  <span key={m} className="chip chip--skill">
                    {m}
                  </span>
                ))}
              </div>
            )}
            {!compact && (fit.missing ?? []).length > 0 && (
              <div className="offer-card__chips">
                {(fit.missing ?? []).map((m) => (
                  <span key={m} className="chip chip--missing">
                    missing: {m}
                  </span>
                ))}
              </div>
            )}
          </div>
        </>
      ) : fit.state === 'failed' ? (
        <span className={enrichmentStatusClass('failed')}>Fit unavailable</span>
      ) : (
        <span className={enrichmentStatusClass('pending')}>Fit pending</span>
      )}
    </div>
  )
}

interface AffinityBreakdownProps {
  affinity: AffinityDto
  compact?: boolean
}

export function AffinityBreakdown({ affinity, compact }: AffinityBreakdownProps) {
  return (
    <div className="offer-card__affinity" data-testid="offer-affinity">
      {affinity.state === 'produced' && affinity.score != null ? (
        <>
          <span
            className="offer-card__affinity-score"
            style={{ color: fitColorVar(affinity.score) }}
          >
            {affinity.score}
            <span className="offer-card__affinity-max">/100 affinity</span>
          </span>
          <div className="offer-card__affinity-lists">
            {affinity.rationale &&
              (compact ? (
                <details className="offer-signals__why">
                  <summary>Why this affinity</summary>
                  <p className="offer-card__fit-rationale">{affinity.rationale}</p>
                </details>
              ) : (
                <p className="offer-card__fit-rationale">{affinity.rationale}</p>
              ))}
            {(affinity.resembles ?? []).length > 0 && (
              <div className="offer-card__chips">
                {(affinity.resembles ?? []).map((r) => (
                  <span key={r} className="chip chip--skill">
                    {r}
                  </span>
                ))}
              </div>
            )}
          </div>
        </>
      ) : affinity.state === 'failed' ? (
        <span className={enrichmentStatusClass('failed')}>Affinity unavailable</span>
      ) : affinity.state === 'insufficient' ? (
        // Cold start — a distinct state, NOT "pending" (FR-007): affinity needs ≥ 3 applied offers.
        <span
          className="offer-card__affinity-insufficient muted text-sm"
          data-testid="affinity-insufficient"
        >
          Affinity — not enough application history yet
        </span>
      ) : (
        <span className={enrichmentStatusClass('pending')}>Affinity pending</span>
      )}
    </div>
  )
}
