import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { OfferCard } from '../../src/components/OfferCard/OfferCard.tsx'
import type { OfferDto } from '../../src/api/types.ts'

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
    affinity: { state: 'insufficient' },
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

describe('Affinity cold start (T040, FR-006)', () => {
  it('below 3 applied offers, affinity shows an honest "insufficient" message — not a number, not "pending"', () => {
    render(<OfferCard offer={makeOffer()} />)

    expect(screen.getByTestId('affinity-insufficient')).toBeInTheDocument()
    expect(screen.getByText(/not enough application history yet/i)).toBeInTheDocument()
    expect(screen.queryByText('/100 affinity')).not.toBeInTheDocument()
    expect(screen.queryByText('Affinity pending')).not.toBeInTheDocument()
  })

  it('suppresses the per-card cold-start line when the feed-level hint already covers it (finding #7)', () => {
    render(<OfferCard offer={makeOffer()} suppressAffinityInsufficient />)

    // The whole affinity block is hidden — the page-level hint speaks for the whole feed.
    expect(screen.queryByTestId('affinity-insufficient')).not.toBeInTheDocument()
    expect(screen.queryByTestId('offer-affinity')).not.toBeInTheDocument()
  })
})
