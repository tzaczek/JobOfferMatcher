import './OfferCard.css'

interface DismissedStubProps {
  offerId: string
  title: string
  /** Restore the offer (→ viewed) and flip the row back to a full card in place. */
  onUndo: (offerId: string) => void
}

/**
 * The collapsed one-line placeholder a dismissed card leaves behind for a few seconds (finding #6),
 * so the rest of the feed doesn't reshuffle and a misclick is one click to recover.
 */
export function DismissedStub({ offerId, title, onUndo }: DismissedStubProps) {
  return (
    <div
      className="card offer-card offer-card--dismissed-stub"
      data-offer-id={offerId}
      data-testid="dismissed-stub"
    >
      <span className="offer-card__dismissed-label muted text-sm">Dismissed “{title}”</span>
      <button type="button" className="btn btn--ghost btn--sm" onClick={() => onUndo(offerId)}>
        Undo
      </button>
    </div>
  )
}
