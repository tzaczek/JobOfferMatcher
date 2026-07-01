import { useCallback, useEffect, useState } from 'react'
import type { ApplicationCardDto, ApplicationBoardDto, ApplicationOutcome, PipelineStageDto } from '../../api/types.ts'
import { getBoard, listStages } from '../../api/applications.ts'
import { ApiError } from '../../api/client.ts'
import { formatDateUtc } from '../../lib/format.ts'
import { ApplicationDrawer } from '../../components/ApplicationDrawer/ApplicationDrawer.tsx'
import './ApplicationsPage.css'

/** Fixed outcome labels + the reused design-truth chip class for each (Principle VIII). */
const OUTCOME: Record<ApplicationOutcome, { label: string; chip: string }> = {
  accepted: { label: 'Accepted', chip: 'chip chip--produced' },
  rejected: { label: 'Rejected', chip: 'chip chip--failed' },
  withdrawn: { label: 'Withdrawn', chip: 'chip chip--dismissed' },
  noResponse: { label: 'No response', chip: 'chip chip--pending' },
}

export function ApplicationsPage() {
  const [board, setBoard] = useState<ApplicationBoardDto | null>(null)
  const [stages, setStages] = useState<PipelineStageDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [openOfferId, setOpenOfferId] = useState<string | null>(null)

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true)
    setError(null)
    try {
      const [b, s] = await Promise.all([getBoard(signal), listStages(signal)])
      setBoard(b)
      setStages(s)
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return
      setError(e instanceof ApiError ? e.message : 'Failed to load applications.')
    } finally {
      if (!signal?.aborted) setLoading(false)
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  const totalActive = board ? board.stages.reduce((n, s) => n + s.applications.length, 0) : 0
  const isEmpty = board !== null && totalActive === 0 && board.closed.length === 0

  return (
    <section className="applications-page">
      <div className="applications-page__header">
        <h1 className="page-title">Applications</h1>
        <p className="page-subtitle">
          {board ? `${totalActive} active · ${board.closed.length} closed` : 'Your interview pipeline.'}
        </p>
      </div>

      {loading && (
        <div className="state-block">
          <span className="spinner" aria-label="Loading" /> Loading applications…
        </div>
      )}

      {!loading && error && (
        <div className="state-block" role="alert">
          {error}
        </div>
      )}

      {!loading && !error && isEmpty && (
        <div className="state-block" data-testid="applications-empty">
          No applications yet — mark an offer applied and it&apos;ll show up here.
        </div>
      )}

      {!loading && !error && board && !isEmpty && (
        <>
          <div className="board" data-testid="application-board">
            {board.stages.map((stage) => (
              <div className="board__column" key={stage.id}>
                <div className="board__column-head">
                  <span className="board__column-name">{stage.name}</span>
                  <span className="board__column-count">{stage.applications.length}</span>
                </div>
                <div className="board__cards">
                  {stage.applications.map((card) => (
                    <BoardCard key={card.offerId} card={card} onOpen={() => setOpenOfferId(card.offerId)} />
                  ))}
                  {stage.applications.length === 0 && <p className="board__empty muted text-sm">—</p>}
                </div>
              </div>
            ))}
          </div>

          {board.closed.length > 0 && (
            <div className="board-closed">
              <h2 className="board-closed__title">Closed</h2>
              <div className="board-closed__cards">
                {board.closed.map((card) => (
                  <BoardCard key={card.offerId} card={card} closed onOpen={() => setOpenOfferId(card.offerId)} />
                ))}
              </div>
            </div>
          )}
        </>
      )}

      {openOfferId && (
        <ApplicationDrawer
          offerId={openOfferId}
          stages={stages}
          onClose={() => setOpenOfferId(null)}
          onChanged={() => void load()}
        />
      )}
    </section>
  )
}

function BoardCard({ card, closed, onOpen }: { card: ApplicationCardDto; closed?: boolean; onOpen: () => void }) {
  const outcome = card.outcome ? OUTCOME[card.outcome] : null
  return (
    <button type="button" className="board-card" data-testid="board-card" onClick={onOpen}>
      <span className="board-card__title">{card.title}</span>
      <span className="board-card__company muted text-sm">{card.company}</span>
      {card.appliedAt && <span className="muted text-sm">Applied {formatDateUtc(card.appliedAt)}</span>}
      <span className="board-card__badges">
        {closed && outcome && <span className={outcome.chip}>{outcome.label}</span>}
        {card.overdueTaskCount > 0 && (
          <span className="chip chip--failed" data-testid="overdue-badge">
            {card.overdueTaskCount} overdue
          </span>
        )}
        {card.overdueTaskCount === 0 && card.outstandingTaskCount > 0 && (
          <span className="chip chip--pending" data-testid="task-badge">
            {card.outstandingTaskCount} to do
          </span>
        )}
        {card.nextInterviewAt && <span className="chip chip--updated">Interview {formatDateUtc(card.nextInterviewAt)}</span>}
      </span>
    </button>
  )
}
