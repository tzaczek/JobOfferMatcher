import { useCallback, useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import type {
  ApplicationInput,
  OfferDto,
  OffersQuery,
  OffersResponse,
  ScanRunSummaryDto,
  ScanStatusDto,
  SortKey,
  SourceDto,
  TailoredCvState,
  UserStatus,
} from '../../api/types.ts'
import {
  clearOfferApplied,
  listOffers,
  markOfferApplied,
  setOfferStatus,
} from '../../api/offers.ts'
import { getScanStatus, listScans, runScan } from '../../api/scans.ts'
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
import { formatRelativeTime, outcomeClass } from '../../lib/format.ts'
import { OfferCard } from '../../components/OfferCard/OfferCard.tsx'
import { DismissedStub } from '../../components/OfferCard/DismissedStub.tsx'
import { EnrichmentIndicator } from '../../components/EnrichmentIndicator/EnrichmentIndicator.tsx'
import { ScanBanner } from './ScanBanner.tsx'
import './OffersPage.css'

/** The mutually-exclusive feed views surfaced by the segmented control. */
type FeedView = 'all' | 'new' | 'applied' | 'dismissed'

/** Allow-lists so a hand-edited/garbage URL param can't break the view or sort (finding #3). */
const FEED_VIEWS = ['all', 'new', 'applied', 'dismissed'] as const
const SORT_KEYS: readonly SortKey[] = ['rank', 'salary', 'fit', 'affinity', 'published', 'recency']
const WORK_MODES = ['remote', 'hybrid', 'office'] as const

/** Read a URL param only if it is one of the allowed values; otherwise fall back. */
function readEnumParam<T extends string>(
  params: URLSearchParams,
  key: string,
  allow: readonly T[],
  fallback: T,
): T {
  const v = params.get(key)
  return v && (allow as readonly string[]).includes(v) ? (v as T) : fallback
}

/** Order-independent comparison so the URL-sync effect converges without reorder churn. */
function paramsEqual(a: URLSearchParams, b: URLSearchParams): boolean {
  const norm = (p: URLSearchParams) =>
    [...p.entries()]
      .sort()
      .map(([k, v]) => `${k}=${v}`)
      .join('&')
  return norm(a) === norm(b)
}

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
  // Read the URL once, above the state block, so the toolbar can lazy-init from it (finding #3).
  const [searchParams, setSearchParams] = useSearchParams()

  const [data, setData] = useState<OffersResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [view, setView] = useState<FeedView>(() => readEnumParam(searchParams, 'view', FEED_VIEWS, 'all'))
  const [sort, setSort] = useState<SortKey>(() => readEnumParam(searchParams, 'sort', SORT_KEYS, 'rank'))
  const [source, setSource] = useState(() => searchParams.get('source') ?? '')
  const [workMode, setWorkMode] = useState<string>(() =>
    readEnumParam(searchParams, 'workMode', WORK_MODES, ''),
  )
  // `searchInput` is the immediate text box; `q` is its 300ms-debounced value (the applied filter).
  const [searchInput, setSearchInput] = useState(() => searchParams.get('q') ?? '')
  const [q, setQ] = useState(() => searchParams.get('q') ?? '')
  const [sources, setSources] = useState<SourceDto[]>([])

  const [scanStatus, setScanStatus] = useState<ScanStatusDto | null>(null)
  const [scanning, setScanning] = useState(false)
  const [scanError, setScanError] = useState<string | null>(null)
  const [lastScan, setLastScan] = useState<ScanRunSummaryDto | null>(null)
  const [enrichmentRefresh, setEnrichmentRefresh] = useState(0)

  const [tailoredByOffer, setTailoredByOffer] = useState<Record<string, TailoredCvState>>({})
  const [stageByOffer, setStageByOffer] = useState<Record<string, string>>({})
  const [appStages, setAppStages] = useState<PipelineStageDto[]>([])
  const [openApplicationId, setOpenApplicationId] = useState<string | null>(null)
  const [openDetailId, setOpenDetailId] = useState<string | null>(null)
  const [highlightOfferId, setHighlightOfferId] = useState<string | null>(null)

  // Offers in their ~6s dismiss-undo window: the slot shows a stub instead of the card (finding #6).
  const [dismissStubs, setDismissStubs] = useState<Record<string, OfferDto>>({})
  const dismissTimersRef = useRef<Record<string, ReturnType<typeof setTimeout>>>({})

  // Debounce the search box: 300ms after the last keystroke, apply it as `q` (and re-fetch).
  useEffect(() => {
    const timer = setTimeout(() => setQ(searchInput), 300)
    return () => clearTimeout(timer)
  }, [searchInput])

  // Deep-link support: `?offerId=` (e.g. from the Tailored CVs page) scrolls the matching card into
  // view and flashes it — once the feed has loaded, so the card actually exists in the DOM. It does
  // NOT open the drawer (which would just hide the card the link is meant to surface).
  useEffect(() => {
    const offerId = searchParams.get('offerId')
    if (!offerId || !data) return

    const card = document.querySelector<HTMLElement>(`[data-offer-id="${offerId}"]`)
    if (card) {
      card.scrollIntoView({ behavior: 'smooth', block: 'center' })
      setHighlightOfferId(offerId)
    }

    const next = new URLSearchParams(searchParams)
    next.delete('offerId')
    setSearchParams(next, { replace: true })
  }, [searchParams, data, setSearchParams])

  // Runs independently of the effect above so clearing the URL param doesn't cut the flash short.
  useEffect(() => {
    if (!highlightOfferId) return
    const timer = setTimeout(() => setHighlightOfferId(null), 2500)
    return () => clearTimeout(timer)
  }, [highlightOfferId])

  // Mirror the toolbar selections into the URL (replace mode → no history spam; a clean `/` at
  // defaults; preserve an incoming `?offerId=` so the deep link still works). Finding #3.
  useEffect(() => {
    const next = new URLSearchParams()
    const offerId = searchParams.get('offerId')
    if (offerId) next.set('offerId', offerId)
    if (view !== 'all') next.set('view', view)
    if (sort !== 'rank') next.set('sort', sort)
    if (source) next.set('source', source)
    if (q) next.set('q', q)
    if (workMode) next.set('workMode', workMode)
    if (!paramsEqual(next, searchParams)) setSearchParams(next, { replace: true })
  }, [view, sort, source, q, workMode, searchParams, setSearchParams])

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
      for (const stage of board.stages)
        for (const card of stage.applications) map[card.offerId] = stage.name
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
          {
            ...viewQuery(view),
            source: source || undefined,
            workMode: workMode || undefined,
            q: q || undefined,
            sort,
          },
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
    [view, source, workMode, q, sort],
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

  const refreshLastScan = useCallback(async (signal?: AbortSignal) => {
    try {
      const { data } = await listScans(signal)
      setLastScan(data[0] ?? null)
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return
      // Non-fatal: the freshness line simply stays hidden.
    }
  }, [])

  // Poll one scan run to a terminal state, then refresh the feed, freshness, and enrichment status.
  const pollScanRun = useCallback(
    async (scanRunId: string) => {
      await poll<ScanStatusDto>({
        fetch: (signal) => getScanStatus(scanRunId, signal),
        done: (s) => s.state === 'completed' || s.state === 'incomplete',
        onTick: setScanStatus,
        intervalMs: 1200,
      })
      await load()
      await refreshLastScan()
      // A scan eagerly creates pending enrichment rows — re-fetch status so the banner isn't stale.
      setEnrichmentRefresh((n) => n + 1)
    },
    [load, refreshLastScan],
  )

  // Freshness on mount + resume an in-flight scan (scheduler or another tab). Mount-only via a ref so
  // a filter change doesn't re-trigger a resume. Finding #5.
  const pollScanRunRef = useRef(pollScanRun)
  useEffect(() => {
    pollScanRunRef.current = pollScanRun
  }, [pollScanRun])
  useEffect(() => {
    const controller = new AbortController()
    ;(async () => {
      try {
        const { data: runs } = await listScans(controller.signal)
        if (controller.signal.aborted) return
        const latest = runs[0] ?? null
        setLastScan(latest)
        if (latest && latest.finishedAt === null) {
          setScanning(true)
          try {
            await pollScanRunRef.current(latest.scanRunId)
          } finally {
            if (!controller.signal.aborted) setScanning(false)
          }
        }
      } catch {
        // Non-fatal (abort or network) — the freshness line just stays hidden.
      }
    })()
    return () => controller.abort()
  }, [])

  async function handleRunScan() {
    setScanning(true)
    setScanError(null)
    setScanStatus(null)
    try {
      const { scanRunId } = await runScan()
      await pollScanRun(scanRunId)
    } catch (e) {
      setScanError(e instanceof ApiError ? e.message : 'Scan failed to start.')
    } finally {
      setScanning(false)
    }
  }

  async function handleSetStatus(offerId: string, next: Exclude<UserStatus, 'new'>) {
    // Dismiss gets an inline undo window instead of an immediate full-feed round-trip (finding #6).
    if (next === 'dismissed') {
      void handleDismiss(offerId)
      return
    }
    try {
      await setOfferStatus(offerId, next)
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to update the offer.')
    }
  }

  // Optimistically collapse the card to a "Dismissed — Undo" stub in place (no reshuffle, no load()),
  // then commit (drop it from the feed) after ~6s unless undone (finding #6).
  async function handleDismiss(offerId: string) {
    const offer = dataRef.current?.data.find((o) => o.offerId === offerId)
    if (!offer || dismissStubs[offerId]) return
    setDismissStubs((prev) => ({ ...prev, [offerId]: offer }))
    try {
      await setOfferStatus(offerId, 'dismissed')
    } catch (e) {
      setDismissStubs((prev) => {
        const n = { ...prev }
        delete n[offerId]
        return n
      })
      setError(e instanceof ApiError ? e.message : 'Failed to dismiss the offer.')
      return
    }
    dismissTimersRef.current[offerId] = setTimeout(() => commitDismiss(offerId), 6000)
  }

  function commitDismiss(offerId: string) {
    delete dismissTimersRef.current[offerId]
    setDismissStubs((prev) => {
      const n = { ...prev }
      delete n[offerId]
      return n
    })
    // The undo window has passed — drop the card from the (default) feed it was dismissed out of.
    setData((prev) =>
      prev
        ? {
            ...prev,
            meta: { ...prev.meta, total: Math.max(0, prev.meta.total - 1) },
            data: prev.data.filter((o) => o.offerId !== offerId),
          }
        : prev,
    )
  }

  async function handleUndoDismiss(offerId: string) {
    const timer = dismissTimersRef.current[offerId]
    if (timer) {
      clearTimeout(timer)
      delete dismissTimersRef.current[offerId]
    }
    setDismissStubs((prev) => {
      const n = { ...prev }
      delete n[offerId]
      return n
    })
    try {
      await setOfferStatus(offerId, 'viewed')
      // Flip the card back in place (it was optimistically dismissed) — no reshuffle.
      setData((prev) =>
        prev
          ? {
              ...prev,
              data: prev.data.map((o) =>
                o.offerId === offerId ? { ...o, userStatus: 'viewed' as UserStatus } : o,
              ),
            }
          : prev,
      )
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to restore the offer.')
    }
  }

  // Drop any pending undo windows when the feed is refiltered (it's about to reload) and on unmount —
  // no orphaned timers or stubs linger (plan risk note).
  useEffect(() => {
    const timers = dismissTimersRef.current
    return () => {
      for (const t of Object.values(timers)) clearTimeout(t)
      dismissTimersRef.current = {}
    }
  }, [])
  useEffect(() => {
    for (const t of Object.values(dismissTimersRef.current)) clearTimeout(t)
    dismissTimersRef.current = {}
    setDismissStubs({})
  }, [view, sort, source, q, workMode])

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

  // Latest feed snapshot for imperative reads (markOfferViewed) without stale closures.
  const dataRef = useRef(data)
  useEffect(() => {
    dataRef.current = data
  }, [data])

  // Opening an offer (directly or via prev/next) marks a `new` offer `viewed` and drops the "N new"
  // count — optimistically, and ONCE per offer (ref-guarded) so a background refetch can't re-fire it
  // (FR-010, finding #4). Non-fatal: the optimistic update stands and a later refetch reconciles.
  const markedViewedRef = useRef<Set<string>>(new Set())
  const markOfferViewed = useCallback((offerId: string) => {
    if (markedViewedRef.current.has(offerId)) return
    const offer = dataRef.current?.data.find((o) => o.offerId === offerId)
    if (!offer || offer.userStatus !== 'new') return
    markedViewedRef.current.add(offerId)
    setData((prev) =>
      prev
        ? {
            ...prev,
            meta: { ...prev.meta, new: Math.max(0, prev.meta.new - 1) },
            data: prev.data.map((o) =>
              o.offerId === offerId ? { ...o, userStatus: 'viewed' as UserStatus } : o,
            ),
          }
        : prev,
    )
    void setOfferStatus(offerId, 'viewed').catch(() => {})
  }, [])

  const handleOpenDetail = useCallback(
    (offerId: string) => {
      setOpenDetailId(offerId)
      markOfferViewed(offerId)
    },
    [markOfferViewed],
  )

  // Clear the whole New queue in one action (finding #4): every loaded offer here is `new`.
  async function handleMarkAllReviewed() {
    const ids = (data?.data ?? []).map((o) => o.offerId)
    if (ids.length === 0) return
    try {
      await Promise.all(ids.map((id) => setOfferStatus(id, 'viewed')))
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to mark offers reviewed.')
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
            {data
              ? `${data.meta.total} offers · ${data.meta.new} new`
              : 'Your ranked feed of collected offers.'}
            {loading && data && (
              <span
                className="spinner offers-page__refresh-spinner"
                aria-label="Refreshing"
                data-testid="offers-refreshing"
              />
            )}
          </p>
          {lastScan && (
            <p className="offers-page__freshness text-sm" data-testid="offers-freshness">
              <span className="muted">Last scan: {formatRelativeTime(lastScan.startedAt)}</span>
              {lastScan.finishedAt === null ? (
                <span className="muted"> — scanning…</span>
              ) : (
                <>
                  {' — '}
                  <span className={outcomeClass(lastScan.outcome)}>
                    {lastScan.outcome ?? 'running'}
                  </span>
                  <span className="muted"> ({lastScan.counts.new} new)</span>
                </>
              )}
            </p>
          )}
        </div>
        <div className="offers-page__header-actions">
          <div className="offers-page__export" role="group" aria-label="Export offers">
            <span className="offers-page__export-label muted text-sm">Export</span>
            <a
              className="btn btn--ghost btn--sm"
              href={exportUrl('json')}
              download
              aria-label="Export offers as JSON"
            >
              JSON
            </a>
            <a
              className="btn btn--ghost btn--sm"
              href={exportUrl('csv')}
              download
              aria-label="Export offers as CSV"
            >
              CSV
            </a>
          </div>
          <button
            type="button"
            className="btn btn--primary"
            onClick={handleRunScan}
            disabled={scanning}
          >
            {scanning ? 'Scanning…' : 'Run scan'}
          </button>
        </div>
      </div>

      <ScanBanner scanning={scanning} status={scanStatus} error={scanError} />

      <EnrichmentIndicator
        refreshKey={enrichmentRefresh}
        onProduced={() => {
          // Refresh when enrichment finishes — but never reshuffle under an open detail view or an
          // in-flight dismiss/undo (plan risk note); the next natural load reconciles those cases.
          if (openDetailId || Object.keys(dismissStubs).length > 0) return
          void load()
          void loadTailored()
        }}
      />

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
            className={
              view === 'applied' ? 'segmented__btn segmented__btn--active' : 'segmented__btn'
            }
            onClick={() => setView('applied')}
          >
            Applied
          </button>
          <button
            type="button"
            className={
              view === 'dismissed' ? 'segmented__btn segmented__btn--active' : 'segmented__btn'
            }
            onClick={() => setView('dismissed')}
          >
            Dismissed
          </button>
        </div>
        <div className="offers-page__controls">
          {view === 'new' && data && data.data.length > 0 && (
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              onClick={handleMarkAllReviewed}
              data-testid="mark-all-reviewed"
            >
              Mark all reviewed
            </button>
          )}
          <input
            type="search"
            className="offers-page__search"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="Search title or company…"
            aria-label="Search offers by title or company"
          />
          <label className="offers-page__select" htmlFor="offers-workmode-filter">
            Work mode
            <select
              id="offers-workmode-filter"
              value={workMode}
              onChange={(e) => setWorkMode(e.target.value)}
              aria-label="Filter by work mode"
            >
              <option value="">Any</option>
              <option value="remote">Remote</option>
              <option value="hybrid">Hybrid</option>
              <option value="office">Office</option>
            </select>
          </label>
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
            <select
              id="offers-sort"
              value={sort}
              onChange={(e) => setSort(e.target.value as SortKey)}
            >
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
          Apply to at least 3 offers to unlock the affinity signal ({data.meta.appliedCount ?? 0}/3
          so far).
        </p>
      )}

      {loading && !data && (
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

      {data && data.data.length > 0 && (
        // Stays mounted through background reloads (e.g. after "Interested"/"Dismiss") so the DOM —
        // and the user's scroll position — doesn't collapse and jump to the top while refetching.
        <div className="offers-page__feed">
          {data.data.map((offer) =>
            dismissStubs[offer.offerId] ? (
              <DismissedStub
                key={offer.offerId}
                offerId={offer.offerId}
                title={offer.title}
                onUndo={handleUndoDismiss}
              />
            ) : (
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
                onOpenDetail={handleOpenDetail}
                highlighted={offer.offerId === highlightOfferId}
                suppressAffinityInsufficient={data.meta.hasAffinityBasis === false}
              />
            ),
          )}
        </div>
      )}

      {openDetailId && (
        <OfferDetailDrawer
          offerId={openDetailId}
          onClose={() => setOpenDetailId(null)}
          onSetStatus={handleSetStatus}
          onMarkApplied={handleMarkApplied}
          onClearApplied={handleClearApplied}
          onOpenApplication={setOpenApplicationId}
          tailoredState={tailoredByOffer[openDetailId]}
          onTailoredChanged={loadTailored}
          offerIds={data?.data.map((o) => o.offerId) ?? []}
          onNavigate={handleOpenDetail}
        />
      )}

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
