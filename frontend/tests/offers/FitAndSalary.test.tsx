import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import type { OfferDto, OffersResponse } from '../../src/api/types.ts'
import { OfferCard } from '../../src/components/OfferCard/OfferCard.tsx'

const listOffers = vi.fn()
vi.mock('../../src/api/offers.ts', () => ({
  listOffers: (...a: unknown[]) => listOffers(...a),
  setOfferStatus: vi.fn(),
}))
vi.mock('../../src/api/scans.ts', () => ({ runScan: vi.fn(), getScanStatus: vi.fn() }))

import { OffersPage } from '../../src/pages/Offers/OffersPage.tsx'

function offerWithFit(): OfferDto {
  return {
    offerId: 'o1',
    roleGroupId: null,
    title: 'Senior .NET Engineer',
    company: 'Acme',
    location: 'Kraków',
    workMode: 'remote',
    employmentType: 'b2b',
    seniority: 'senior',
    requiredSkills: ['C#', '.NET'],
    niceToHaveSkills: [],
    salaryBands: [{ min: 18000, max: 22000, currency: 'PLN', period: 'monthly', basis: 'b2b', tax: 'net' }],
    normalizedSalary: {
      comparableMonthly: { amount: 17000, currency: 'PLN' },
      quality: 'Estimated',
      assumptions: ['midpoint 18000–22000 = 20000', 'B2B→Permanent-equivalent ×0.85'],
    },
    fit: { score: 99, matched: ['C#', '.NET', 'remote'], missing: ['Kubernetes'] },
    canonicalUrl: 'https://example.test/x',
    isNew: false,
    isUpdated: false,
    availability: 'available',
    firstSeenAt: '2026-06-25T08:00:00Z',
    firstSuggestedAt: '2026-06-25T08:00:00Z',
    lastSeenAt: '2026-06-26T08:00:00Z',
    userStatus: 'viewed',
  }
}

describe('fit + normalized salary (T049a)', () => {
  it('renders the 0–100 fit score with matched and missing breakdown', () => {
    render(<OfferCard offer={offerWithFit()} />)
    expect(screen.getByText('99')).toBeInTheDocument()
    expect(screen.getByText('/100 fit')).toBeInTheDocument()
    expect(screen.getByText('missing: Kubernetes')).toBeInTheDocument()
  })

  it('renders the raw band plus the "≈ normalized (est.)" cell with a quality chip', () => {
    render(<OfferCard offer={offerWithFit()} />)
    expect(screen.getByText(/18,000–22,000 PLN net\/mo \(B2B\)/)).toBeInTheDocument()
    expect(screen.getByText(/≈ 17,000 PLN\/mo \(est\.\)/)).toBeInTheDocument()
    expect(screen.getByText('Estimated')).toBeInTheDocument()
  })
})

describe('sort controls (T049a)', () => {
  beforeEach(() => listOffers.mockReset())

  it('renders the rank/salary/fit/recency sort options', async () => {
    const empty: OffersResponse = { data: [], meta: { total: 0, new: 0, noReadableCv: false } }
    listOffers.mockResolvedValue(empty)
    render(<OffersPage />)
    await screen.findByTestId('empty-state')

    expect(screen.getByRole('option', { name: 'Best match' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Salary' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Fit' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Most recent' })).toBeInTheDocument()
  })
})
