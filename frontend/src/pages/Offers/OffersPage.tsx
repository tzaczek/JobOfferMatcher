import { useCallback, useEffect, useState } from 'react'
import type {
  ApplicationInput,
  OffersQuery,
  OffersResponse,
  ScanStatusDto,
  SortKey,
  SourceDto,
  TailoredCvState,
  UserStatus,
} from '../../api/types.ts'
import { clearOfferApplied, listOffers, markOfferApplied, setOfferStatus } from '../../api/offers.ts'
import { getScanStatus, runScan } from '../../api/scans.ts'
import { listSources } from '../../api/sources.ts'
import { setRoleGroupOverride } from '../../api/roleGroups.ts'
import { listTailored } from '../../api/tailoredCv.ts'
import { getBoard } from '../../api/applications.ts'
import type { PipelineStageDto } from '../../api/types.ts'
import { ApplicationDrawer } from '../../components/ApplicationDrawer/ApplicationDrawer.tsx'
import { OfferDetailDrawer } from '../../components/OfferDetail/OfferDetailDrawer.tsx'
import { exportUrl } from '../../api/export.ts'
import { ApiError } from '../../api/client.ts'
import { poll } from '../../lib/polling.ts'
import { OfferCard } from '../../components/OfferCard/OfferCard.tsx'
import { EnrichmentIndicator } from '../../components/EnrichmentIndicator/EnrichmentIndicator.tsx'
import { ScanBanner } from './ScanBanner.tsx'
import './OffersPage.css'

/** The mutually-exclusive feed views surfaced by the segmented control. */
type FeedView = 'all' | 'new' | 'applied' | 'dismissed'

/** Map a feed view to the offers query it represents. */
function viewQuery(view: FeedView): Pick<OffersQuery, 'status' | 'applied' | 'availability'> {
  switch (view) {
    case 'new':
      return { status: 'new', availability: 'available' }
    case 'applied':
      // Show every application regardless of availability — a role you applied to may have closed.
      return { status: 'all', applied: true, availability: 'all' }
    case 'dismissed':
      // The review-and-restore bin; show all regardless of availability so nothing silently vanishes.
      return { status: 'dismissed', availability: 'all' }
    default:
      // The default feed hides dismissed offers (`active`); dismissing removes a card from here.
      return { status: 'active', availability: 'available' }
  }
}

export function OffersPage() {
  const [data, setData] = useState<OffersResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [view, setView] = useState<FeedView>('all')
  const [sort, setSort] = useState<SortKey>('rank')
  const [source, setSource] = useState('')
  const [sources, setSources] = useState<SourceDto[]>([])

  const [scanStatus, setScanStatus] = useState<ScanStatusDto | null>(null)
  const [scanning, setScanning] = useState(false)
  const [scanError, setScanError] = useState<string | null>(null)

  const [tailoredByOffer, setTailoredByOffer] = useState<Record<string, TailoredCvState>>({})
  const [stageByOffer, setStageByOffer] = useState<Record<string, string>>({})
  const [appStages, setAppStages] = useState<PipelineStageDto[]>([])
  const [openApplicationId, setOpenApplicationId] = useState<string | null>(null)
  const [openDetailId, setOpenDetailId] = useState<string | null>(null)

  const loadTailored = useCallback(async (signal?: AbortSignal) => {
    try {
      const { data } = await listTailored(signal)
      setTailoredByOffer(Object.fromEntries(data.map((t) => [t.offerId, t.state])))
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return
      // Non-fatal: the per-offer indicator simply stays hidden if the lookup can't be read.
    }
  }, [])

  const loadApplications = useCallback(async (signal?: AbortSignal) => {
    try {
      const board = await getBoard(signal)
      const map: Record<string, string> = {}
      for (const stage of board.stages) for (const card of stage.applications) map[card.offerId] = stage.name
      for (const card of board.closed) map[card.offerId] = 'Closed'
      setStageByOffer(map)
      setAppStages(board.stages.map((s) => ({ id: s.id, name: s.name, position: s.position })))
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return
      // Non-fatal: the per-offer stage chip simply stays hidden if the board can't be read.
    }
  }, [])

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true)
      setError(null)
      try {
        const result = await listOffers(
          { ...viewQuery(view), source: source || undefined, sort },
          signal,
        )
        setData(result)
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setError(e instanceof ApiError ? e.message : 'Failed to load offers.')
      } finally {
        // A superseded (aborted) request must not flip the spinner off under the live one.
        if (!signal?.aborted) setLoading(false)
      }
    },
    [view, source, sort],
  )

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  // Per-offer tailored-CV presence/state for the card indicators (refreshed after a generate/remove).
  useEffect(() => {
    const controller = new AbortController()
    void loadTailored(controller.signal)
    return () => controller.abort()
  }, [loadTailored])

  // Per-offer application stage for the card chip (refreshed after apply/clear or a drawer change).
  useEffect(() => {
    const controller = new AbortController()
    void loadApplications(controller.signal)
    return () => controller.abort()
  }, [loadApplications])

  // Populate the source filter once; failure is non-fatal (filter stays "All sources").
  useEffect(() => {
    const controller = new AbortController()
    listSources(controller.signal)
      .then((r) => setSources(r.data))
      .catch((e) => {
        if (e instanceof DOMException && e.name === 'AbortError') return
      })
    return () => controller.abort()
  }, [])

  async function handleRunScan() {
    setScanning(true)
    setScanError(null)
    setScanStatus(null)
    try {
      const { scanRunId } = await runScan()
      await poll<ScanStatusDto>({
        fetch: (signal) => getScanStatus(scanRunId, signal),
        done: (s) => s.state === 'completed' || s.state === 'incomplete',
        onTick: setScanStatus,
        intervalMs: 1200,
      })
      await load()
    } catch (e) {
      setScanError(e instanceof ApiError ? e.message : 'Scan failed to start.')
    } finally {
      setScanning(false)
    }
  }

  async function handleSetStatus(offerId: string, next: Exclude<UserStatus, 'new'>) {
    try {
      await setOfferStatus(offerId, next)
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to update the offer.')
    }
  }

  async function handleMarkApplied(offerId: string, input: ApplicationInput) {
    try {
      await markOfferApplied(offerId, input)
      await Promise.all([load(), loadApplications()])
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to mark the offer applied.')
      // Re-throw so the modal stays open on failure (it only closes on a resolved save).
      throw e
    }
  }

  async function handleClearApplied(offerId: string) {
    try {
      await clearOfferApplied(offerId)
      await Promise.all([load(), loadApplications()])
    } catch (e) {
      // A cleared-with-history offer is steered to closing (409) — surface the guidance, don't erase.
      setError(e instanceof ApiError ? e.message : 'Failed to update the offer.')
    }
  }

  async function handleSplitGroup(roleGroupId: string) {
    try {
      await setRoleGroupOverride(roleGroupId, 'notSame')
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to update grouping.')
    }
  }

  const emptyMessage =
    view === 'new'
      ? 'No new offers — you are all caught up.'
      : view === 'applied'
        ? "No applications yet — mark an offer applied and it'll show up here."
        : view === 'dismissed'
          ? 'No dismissed offers — nothing to review.'
          : 'No offers yet — run a scan to collect them.'

  return (
    <section className="offers-page">
      <div className="offers-page__header">
        <div>
          <h1 className="page-title">Offers</h1>
          <p className="page-subtitle">
            {data ? `${data.meta.total} offers · ${data.meta.new} new` : 'Your ranked feed of collected offers.'}
          </p>
        </div>
        <div className="offers-page__header-actions">
          <div className="offers-page__export" role="group" aria-label="Export offers">
            <span className="offers-page__export-label muted text-sm">Export</span>
            <a className="btn btn--ghost btn--sm" href={exportUrl('json')} download aria-label="Export offers as JSON">
              JSON
            </a>
            <a className="btn btn--ghost btn--sm" href={exportUrl('csv')} download aria-label="Export offers as CSV">
              CSV
            </a>
          </div>
          <button type="button" className="btn btn--primary" onClick={handleRunScan} disabled={scanning}>
            {scanning ? 'Scanning…' : 'Run scan'}
          </button>
        </div>
      </div>

      <ScanBanner scanning={scanning} status={scanStatus} error={scanError} />

      <EnrichmentIndicator />

      <div className="offers-page__toolbar">
        <div className="segmented" role="group" aria-label="Feed filter">
          <button
            type="button"
            className={view === 'all' ? 'segmented__btn segmented__btn--active' : 'segmented__btn'}
            onClick={() => setView('all')}
          >
            All
          </button>
          <button
            type="button"
            className={view === 'new' ? 'segmented__btn segmented__btn--active' : 'segmented__btn'}
            onClick={() => setView('new')}
          >
            New
          </button>
          <button
            type="button"
            className={view === 'applied' ? 'segmented__btn segmented__btn--active' : 'segmented__btn'}
            onClick={() => setView('applied')}
          >
            Applied
          </button>
          <button
            type="button"
            className={view === 'dismissed' ? 'segmented__btn segmented__btn--active' : 'segmented__btn'}
            onClick={() => setView('dismissed')}
          >
            Dismissed
          </button>
        </div>
        <div className="offers-page__controls">
          <label className="offers-page__select" htmlFor="offers-source-filter">
            Source
            <select
              id="offers-source-filter"
              value={source}
              onChange={(e) => setSource(e.target.value)}
              aria-label="Filter by source"
            >
              <option value="">All sources</option>
              {sources.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </label>
          <label className="offers-page__select" htmlFor="offers-sort">
            Sort
            <select id="offers-sort" value={sort} onChange={(e) => setSort(e.target.value as SortKey)}>
              <option value="rank">Best match</option>
              <option value="salary">Salary</option>
              <option value="fit">Fit</option>
              <option value="affinity">Affinity</option>
              <option value="published">Recently published</option>
              <option value="recency">Most recent</option>
            </select>
          </label>
        </div>
      </div>

      {data && data.meta.hasAffinityBasis === false && (
        <p className="offers-page__affinity-hint muted text-sm" data-testid="affinity-hint">
          Apply to at least 3 offers to unlock the affinity signal ({data.meta.appliedCount ?? 0}/3 so far).
        </p>
      )}

      {loading && (
        <div className="state-block">
          <span className="spinner" aria-label="Loading" /> Loading offers…
        </div>
      )}

      {!loading && error && (
        <div className="state-block" role="alert">
          {error}
        </div>
      )}

      {!loading && !error && data && data.data.length === 0 && (
        <div className="state-block" data-testid="empty-state">
          {emptyMessage}
        </div>
      )}

      {!loading && !error && data && data.data.length > 0 && (
        <div className="offers-page__feed">
          {data.data.map((offer) => (
            <OfferCard
              key={offer.offerId}
              offer={offer}
              onSetStatus={handleSetStatus}
              onMarkApplied={handleMarkApplied}
              onClearApplied={handleClearApplied}
              onSplitGroup={handleSplitGroup}
              tailoredState={tailoredByOffer[offer.offerId]}
              onTailoredChanged={loadTailored}
              applicationStageName={stageByOffer[offer.offerId]}
              onOpenApplication={setOpenApplicationId}
              onOpenDetail={setOpenDetailId}
            />
          ))}
        </div>
      )}

      {openDetailId && <OfferDetailDrawer offerId={openDetailId} onClose={() => setOpenDetailId(null)} />}

      {openApplicationId && (
        <ApplicationDrawer
          offerId={openApplicationId}
          stages={appStages}
          onClose={() => setOpenApplicationId(null)}
          onChanged={() => {
            void load()
            void loadApplications()
          }}
        />
      )}
    </section>
  )
}
