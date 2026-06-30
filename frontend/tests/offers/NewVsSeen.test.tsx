import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import type { OfferDto, OffersResponse } from '../../src/api/types.ts'
import { OfferCard } from '../../src/components/OfferCard/OfferCard.tsx'

const listOffers = vi.fn()
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
vi.mock('../../src/api/sources.ts', () => ({ listSources: vi.fn().mockResolvedValue({ data: [] }) }))
vi.mock('../../src/api/roleGroups.ts', () => ({ setRoleGroupOverride: vi.fn() }))

import { OffersPage } from '../../src/pages/Offers/OffersPage.tsx'

function makeOffer(overrides: Partial<OfferDto> = {}): OfferDto {
  return {
    offerId: 'o1',
    roleGroupId: null,
    title: 'Role',
    company: 'Co',
    location: null,
    workMode: 'remote',
    employmentType: null,
    seniority: null,
    requiredSkills: [],
    niceToHaveSkills: [],
    salaryBands: [],
    normalizedSalary: null,
    fit: null,
    canonicalUrl: 'https://example.test/x',
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

describe('new-vs-seen badges (T038b)', () => {
  it('renders the New badge only for new offers', () => {
    const { rerender } = render(<OfferCard offer={makeOffer({ isNew: true })} />)
    expect(screen.getByText('New')).toBeInTheDocument()

    rerender(<OfferCard offer={makeOffer({ isNew: false })} />)
    expect(screen.queryByText('New')).not.toBeInTheDocument()
  })

  it('renders the Updated badge for changed offers (FR-014)', () => {
    render(<OfferCard offer={makeOffer({ isUpdated: true })} />)
    expect(screen.getByText('Updated')).toBeInTheDocument()
  })
})

describe('"no new offers" state (T038b / FR-032)', () => {
  beforeEach(() => listOffers.mockReset())

  it('shows a clear caught-up message, not an error', async () => {
    const empty: OffersResponse = {
      data: [],
      meta: { total: 5, new: 0, hasProducedProfile: false, pendingEnrichment: 0, failedEnrichment: 0 },
    }
    listOffers.mockResolvedValue(empty)
    render(<OffersPage />)

    // Default status is "all"; the page mounts, then we surface the empty state.
    const block = await screen.findByTestId('empty-state')
    expect(block).toBeInTheDocument()
  })
})
