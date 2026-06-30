import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import type { OffersResponse } from '../../src/api/types.ts'

const listOffers = vi.fn()
vi.mock('../../src/api/offers.ts', () => ({
  listOffers: (...args: unknown[]) => listOffers(...args),
  setOfferStatus: vi.fn(),
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

describe('OffersPage states (T023a)', () => {
  beforeEach(() => {
    listOffers.mockReset()
  })

  it('shows a loading state initially', () => {
    listOffers.mockReturnValue(new Promise(() => {})) // never resolves
    render(<OffersPage />)
    expect(screen.getByText(/loading offers/i)).toBeInTheDocument()
  })

  it('shows an empty state when there are no offers', async () => {
    listOffers.mockResolvedValue(EMPTY)
    render(<OffersPage />)
    expect(await screen.findByTestId('empty-state')).toBeInTheDocument()
    expect(screen.getByTestId('empty-state').textContent).toMatch(/no offers yet/i)
  })
})
