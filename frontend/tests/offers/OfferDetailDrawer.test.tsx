import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import type { OfferDetailDto, OfferDto } from '../../src/api/types.ts'

const getOfferDetail = vi.fn()
vi.mock('../../src/api/offers.ts', () => ({
  getOfferDetail: (...args: unknown[]) => getOfferDetail(...args),
}))

import { OfferDetailDrawer } from '../../src/components/OfferDetail/OfferDetailDrawer.tsx'

function makeOffer(): OfferDto {
  return {
    offerId: 'o1',
    roleGroupId: null,
    title: 'Senior .NET Engineer',
    company: 'Acme',
    location: 'Kraków',
    workMode: 'remote',
    employmentType: 'b2b',
    seniority: 'senior',
    requiredSkills: ['C#'],
    niceToHaveSkills: [],
    salaryBands: [],
    normalizedSalary: null,
    fit: null,
    affinity: { state: 'produced', score: 74, resembles: [], rationale: null },
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

function makeDetail(descriptionHtml: string | null): OfferDetailDto {
  return { offer: makeOffer(), descriptionHtml, versions: [], events: [] }
}

describe('OfferDetailDrawer (T037)', () => {
  beforeEach(() => getOfferDetail.mockReset())

  it('renders the server-sanitised body as HTML', async () => {
    getOfferDetail.mockResolvedValue(makeDetail('<p>Full <strong>requirements</strong> here.</p>'))
    render(<OfferDetailDrawer offerId="o1" onClose={() => {}} />)

    const body = await screen.findByTestId('offer-detail-body')
    expect(body.innerHTML).toContain('<strong>requirements</strong>')
    // The always-present external link points to the original posting.
    expect(screen.getByTestId('offer-detail-external')).toHaveAttribute('href', 'https://example.test/x')
  })

  it('shows the "not available" state + external link when the body is null', async () => {
    getOfferDetail.mockResolvedValue(makeDetail(null))
    render(<OfferDetailDrawer offerId="o1" onClose={() => {}} />)

    expect(await screen.findByTestId('offer-detail-unavailable')).toBeInTheDocument()
    expect(screen.getByText(/description not available/i)).toBeInTheDocument()
    expect(screen.getByTestId('offer-detail-external')).toHaveAttribute('href', 'https://example.test/x')
  })
})
