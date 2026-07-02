import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { OfferDto, OffersResponse } from '../../src/api/types.ts'
import { renderWithRouter } from '../testUtils.tsx'

const listOffers = vi.fn()
const getOfferDetail = vi.fn()
vi.mock('../../src/api/offers.ts', () => ({
  listOffers: (...args: unknown[]) => listOffers(...args),
  setOfferStatus: vi.fn(),
  getOfferDetail: (...args: unknown[]) => getOfferDetail(...args),
}))
vi.mock('../../src/api/scans.ts', () => ({
  runScan: vi.fn(),
  getScanStatus: vi.fn(),
}))
vi.mock('../../src/api/sources.ts', () => ({
  listSources: vi.fn().mockResolvedValue({ data: [] }),
}))
vi.mock('../../src/api/roleGroups.ts', () => ({
  setRoleGroupOverride: vi.fn(),
}))
vi.mock('../../src/api/enrichment.ts', () => ({
  getEnrichmentStatus: vi.fn().mockResolvedValue({
    pendingTotal: 0,
    pendingProfiles: 0,
    pendingSummaries: 0,
    pendingFits: 0,
    failedTotal: 0,
    hasProducedProfile: false,
    lastResultAt: null,
  }),
  triggerRerun: vi.fn(),
}))

import { OffersPage } from '../../src/pages/Offers/OffersPage.tsx'

const EMPTY: OffersResponse = {
  data: [],
  meta: { total: 0, new: 0, hasProducedProfile: false, pendingEnrichment: 0, failedEnrichment: 0 },
}

function makeOffer(overrides: Partial<OfferDto> = {}): OfferDto {
  return {
    offerId: 'o1',
    roleGroupId: null,
    title: 'Senior .NET Engineer',
    company: 'Acme',
    location: 'Kraków',
    workMode: 'remote',
    employmentType: 'b2b',
    seniority: 'senior',
    requiredSkills: [],
    niceToHaveSkills: [],
    salaryBands: [],
    normalizedSalary: null,
    fit: null,
    canonicalUrl: 'https://example.test/o1',
    isNew: false,
    isUpdated: false,
    availability: 'available',
    firstSeenAt: '2026-06-25T08:00:00Z',
    firstSuggestedAt: '2026-06-25T08:00:00Z',
    lastSeenAt: '2026-06-26T08:00:00Z',
    userStatus: 'viewed',
    ...overrides,
  }
}

describe('OffersPage states (T023a)', () => {
  beforeEach(() => {
    listOffers.mockReset()
  })

  it('shows a loading state initially', () => {
    listOffers.mockReturnValue(new Promise(() => {})) // never resolves
    renderWithRouter(<OffersPage />)
    expect(screen.getByText(/loading offers/i)).toBeInTheDocument()
  })

  it('shows an empty state when there are no offers', async () => {
    listOffers.mockResolvedValue(EMPTY)
    renderWithRouter(<OffersPage />)
    expect(await screen.findByTestId('empty-state')).toBeInTheDocument()
    expect(screen.getByTestId('empty-state').textContent).toMatch(/no offers yet/i)
  })

  it('selecting the Affinity sort requests sort=affinity (T028/FR-004)', async () => {
    listOffers.mockResolvedValue(EMPTY)
    renderWithRouter(<OffersPage />)
    await screen.findByTestId('empty-state')

    await userEvent.selectOptions(screen.getByLabelText('Sort'), 'affinity')

    await waitFor(() =>
      expect(listOffers).toHaveBeenCalledWith(
        expect.objectContaining({ sort: 'affinity' }),
        expect.anything(),
      ),
    )
  })

  it('shows the cold-start hint when the affinity basis is insufficient (T039)', async () => {
    listOffers.mockResolvedValue({
      data: [],
      meta: { ...EMPTY.meta, appliedCount: 1, hasAffinityBasis: false },
    })
    renderWithRouter(<OffersPage />)
    expect(await screen.findByTestId('affinity-hint')).toHaveTextContent(/at least 3 offers/i)
  })

  it('keeps the feed mounted during a status-change reload instead of collapsing to the loading state', async () => {
    const feed: OffersResponse = {
      data: [
        makeOffer({ offerId: 'o1', title: 'Senior .NET Engineer' }),
        makeOffer({ offerId: 'o2', title: 'Backend Developer' }),
      ],
      meta: { ...EMPTY.meta, total: 2 },
    }
    let resolveReload: (v: OffersResponse) => void = () => {}
    const reload = new Promise<OffersResponse>((resolve) => {
      resolveReload = resolve
    })
    listOffers.mockResolvedValueOnce(feed).mockReturnValueOnce(reload)

    renderWithRouter(<OffersPage />)
    await screen.findByText('Backend Developer')

    await userEvent.click(screen.getAllByRole('button', { name: 'Interested' })[0])
    await waitFor(() => expect(listOffers).toHaveBeenCalledTimes(2))

    // The reload is still in flight — the feed (and scroll position) must not collapse to the
    // full-page spinner, which previously caused the page to jump back to the top on every click.
    expect(screen.getByText('Backend Developer')).toBeInTheDocument()
    expect(screen.queryByText(/^loading offers/i)).not.toBeInTheDocument()
    expect(screen.getByTestId('offers-refreshing')).toBeInTheDocument()

    resolveReload(feed)
    await waitFor(() => expect(screen.queryByTestId('offers-refreshing')).not.toBeInTheDocument())
    expect(screen.getByText('Backend Developer')).toBeInTheDocument()
  })
})

describe('OffersPage deep link (?offerId=, reach-back from Tailored CVs)', () => {
  beforeEach(() => {
    listOffers.mockReset()
    getOfferDetail.mockReset()
    ;(HTMLElement.prototype.scrollIntoView as unknown as ReturnType<typeof vi.fn>).mockClear()
  })

  it('scrolls to and flashes the offer named in the URL, without opening a drawer', async () => {
    listOffers.mockResolvedValue({
      data: [
        makeOffer({ offerId: 'o1', title: 'Senior .NET Engineer' }),
        makeOffer({ offerId: 'o2', title: 'Backend Developer' }),
      ],
      meta: { ...EMPTY.meta, total: 2 },
    })
    renderWithRouter(<OffersPage />, { route: '/?offerId=o2' })

    const card = await screen
      .findByText('Backend Developer')
      .then((el) => el.closest('[data-offer-id="o2"]'))
    await waitFor(() => expect(card).toHaveClass('offer-card--highlight'))
    expect(HTMLElement.prototype.scrollIntoView).toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(getOfferDetail).not.toHaveBeenCalled()
  })

  it('the flash fades on its own after a few seconds', async () => {
    listOffers.mockResolvedValue({
      data: [makeOffer({ offerId: 'o1' })],
      meta: { ...EMPTY.meta, total: 1 },
    })
    renderWithRouter(<OffersPage />, { route: '/?offerId=o1' })

    const card = await screen.findByTestId('offer-card')
    await waitFor(() => expect(card).toHaveClass('offer-card--highlight'))
    await waitFor(() => expect(card).not.toHaveClass('offer-card--highlight'), { timeout: 4000 })
  })

  it('does not scroll or highlight anything without an offerId in the URL', async () => {
    listOffers.mockResolvedValue({ data: [makeOffer()], meta: { ...EMPTY.meta, total: 1 } })
    renderWithRouter(<OffersPage />)

    await screen.findByTestId('offer-card')
    expect(screen.queryByTestId('offer-card')).not.toHaveClass('offer-card--highlight')
    expect(HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled()
  })
})
