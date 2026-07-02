import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import type { OfferDto, OffersResponse, SourceDto } from '../../src/api/types.ts'
import { OfferCard } from '../../src/components/OfferCard/OfferCard.tsx'
import { renderWithRouter } from '../testUtils.tsx'

const listOffers = vi.fn()
const listSources = vi.fn()
const setRoleGroupOverride = vi.fn()

vi.mock('../../src/api/offers.ts', () => ({
  listOffers: (...args: unknown[]) => listOffers(...args),
  setOfferStatus: vi.fn(),
}))
vi.mock('../../src/api/scans.ts', () => ({ runScan: vi.fn(), getScanStatus: vi.fn() }))
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
vi.mock('../../src/api/sources.ts', () => ({
  listSources: (...args: unknown[]) => listSources(...args),
}))
vi.mock('../../src/api/roleGroups.ts', () => ({
  setRoleGroupOverride: (...args: unknown[]) => setRoleGroupOverride(...args),
}))

import { OffersPage } from '../../src/pages/Offers/OffersPage.tsx'

function makeOffer(overrides: Partial<OfferDto> = {}): OfferDto {
  return {
    offerId: 'o1',
    roleGroupId: null,
    title: 'Senior .NET Engineer',
    company: 'Acme Software',
    location: 'Kraków',
    workMode: 'remote',
    employmentType: 'b2b',
    seniority: 'senior',
    requiredSkills: [],
    niceToHaveSkills: [],
    salaryBands: [],
    normalizedSalary: null,
    fit: null,
    canonicalUrl: 'https://justjoin.it/o1',
    isNew: false,
    isUpdated: false,
    availability: 'available',
    firstSeenAt: '2026-06-25T08:00:00Z',
    firstSuggestedAt: '2026-06-25T08:00:00Z',
    lastSeenAt: '2026-06-26T08:00:00Z',
    userStatus: 'new',
    ...overrides,
  }
}

function makeSource(overrides: Partial<SourceDto> = {}): SourceDto {
  return {
    id: 's1',
    name: 'JustJoin.it',
    kind: 'justjoinit',
    requiresLogin: false,
    enabled: true,
    searchCriteria: {
      categories: [],
      experienceLevels: [],
      employmentTypes: [],
      workingTimes: [],
      withSalary: false,
      sortBy: null,
      orderBy: null,
      workplaceKeep: [],
    },
    ...overrides,
  }
}

const EMPTY: OffersResponse = {
  data: [],
  meta: { total: 0, new: 0, hasProducedProfile: false, pendingEnrichment: 0, failedEnrichment: 0 },
}

describe('OfferCard grouped-feed entry (T065)', () => {
  it('renders per-source member links and the "Not the same role" control, and splits on click', () => {
    const onSplitGroup = vi.fn()
    const offer = makeOffer({
      roleGroupId: 'rg-1',
      groupMembers: [
        { offerId: 'o2', sourceName: 'Pracuj.pl', canonicalUrl: 'https://pracuj.pl/o2' },
        { offerId: 'o3', sourceName: 'NoFluffJobs', canonicalUrl: 'https://nofluffjobs.com/o3' },
      ],
    })
    render(<OfferCard offer={offer} onSplitGroup={onSplitGroup} />)

    expect(screen.getByText('Also posted at')).toBeInTheDocument()

    const pracuj = screen.getByRole('link', { name: /Pracuj\.pl/ })
    expect(pracuj).toHaveAttribute('href', 'https://pracuj.pl/o2')
    expect(pracuj).toHaveAttribute('target', '_blank')
    expect(pracuj).toHaveAttribute('rel', 'noreferrer noopener')
    expect(screen.getByRole('link', { name: /NoFluffJobs/ })).toHaveAttribute(
      'href',
      'https://nofluffjobs.com/o3',
    )

    const split = screen.getByRole('button', { name: /not the same role/i })
    fireEvent.click(split)
    expect(onSplitGroup).toHaveBeenCalledWith('rg-1')
  })

  it('renders neither the member links nor the split control without group members', () => {
    const onSplitGroup = vi.fn()
    render(<OfferCard offer={makeOffer()} onSplitGroup={onSplitGroup} />)

    expect(screen.queryByText('Also posted at')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /not the same role/i })).not.toBeInTheDocument()
  })
})

describe('OffersPage source filter (T065)', () => {
  beforeEach(() => {
    listOffers.mockReset()
    listSources.mockReset()
    setRoleGroupOverride.mockReset()
  })

  it('renders an option per source from listSources plus an "All sources" option', async () => {
    listOffers.mockResolvedValue(EMPTY)
    listSources.mockResolvedValue({
      data: [
        makeSource({ id: 's1', name: 'JustJoin.it' }),
        makeSource({ id: 's2', name: 'Pracuj.pl' }),
      ],
    })

    renderWithRouter(<OffersPage />)

    expect(await screen.findByRole('option', { name: 'JustJoin.it' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Pracuj.pl' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'All sources' })).toBeInTheDocument()
  })

  it('re-queries listOffers with the chosen source id when the filter changes', async () => {
    listOffers.mockResolvedValue(EMPTY)
    listSources.mockResolvedValue({ data: [makeSource({ id: 's2', name: 'Pracuj.pl' })] })

    renderWithRouter(<OffersPage />)
    // Initial load passes no source.
    await waitFor(() =>
      expect(listOffers).toHaveBeenCalledWith(
        expect.objectContaining({ source: undefined }),
        expect.anything(),
      ),
    )

    fireEvent.change(await screen.findByLabelText('Filter by source'), { target: { value: 's2' } })

    await waitFor(() =>
      expect(listOffers).toHaveBeenLastCalledWith(
        expect.objectContaining({ source: 's2' }),
        expect.anything(),
      ),
    )
  })
})

describe('OffersPage split-group flow (T065)', () => {
  beforeEach(() => {
    listOffers.mockReset()
    listSources.mockReset()
    setRoleGroupOverride.mockReset()
  })

  it('marks a group "not same" and reloads the feed when the split control is clicked', async () => {
    listSources.mockResolvedValue({ data: [] })
    setRoleGroupOverride.mockResolvedValue(undefined)
    const grouped: OffersResponse = {
      data: [
        makeOffer({
          roleGroupId: 'rg-9',
          groupMembers: [
            { offerId: 'o2', sourceName: 'Pracuj.pl', canonicalUrl: 'https://pracuj.pl/o2' },
          ],
        }),
      ],
      meta: {
        total: 1,
        new: 0,
        hasProducedProfile: false,
        pendingEnrichment: 0,
        failedEnrichment: 0,
      },
    }
    listOffers.mockResolvedValue(grouped)

    renderWithRouter(<OffersPage />)

    const split = await screen.findByRole('button', { name: /not the same role/i })
    const callsBefore = listOffers.mock.calls.length
    fireEvent.click(split)

    await waitFor(() => expect(setRoleGroupOverride).toHaveBeenCalledWith('rg-9', 'notSame'))
    // The feed reloads after the override is applied.
    await waitFor(() => expect(listOffers.mock.calls.length).toBeGreaterThan(callsBefore))
  })
})
