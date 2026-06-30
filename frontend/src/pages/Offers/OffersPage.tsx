import { useCallback, useEffect, useState } from 'react'
import type {
  ApplicationInput,
  OffersQuery,
  OffersResponse,
  ScanStatusDto,
  SortKey,
  SourceDto,
  UserStatus,
} from '../../api/types.ts'
import { clearOfferApplied, listOffers, markOfferApplied, setOfferStatus } from '../../api/offers.ts'
import { getScanStatus, runScan } from '../../api/scans.ts'
import { listSources } from '../../api/sources.ts'
import { setRoleGroupOverride } from '../../api/roleGroups.ts'
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
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to mark the offer applied.')
      // Re-throw so the modal stays open on failure (it only closes on a resolved save).
      throw e
    }
  }

  async function handleClearApplied(offerId: string) {
    try {
      await clearOfferApplied(offerId)
      await load()
    } catch (e) {
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
              <option value="published">Recently published</option>
              <option value="recency">Most recent</option>
            </select>
          </label>
        </div>
      </div>

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
            />
          ))}
        </div>
      )}
    </section>
  )
}
