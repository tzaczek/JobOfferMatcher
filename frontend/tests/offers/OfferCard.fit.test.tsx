import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { OfferCard } from '../../src/components/OfferCard/OfferCard.tsx'
import type { FitDto, OfferDto } from '../../src/api/types.ts'

function makeOffer(fit: FitDto | null): OfferDto {
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
    enrichmentState: 'produced',
    summary: 'A senior .NET role.',
    keySkills: [],
    fit,
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

describe('OfferCard fit states (T052)', () => {
  it('produced: renders score, rationale, matched and missing', () => {
    render(
      <OfferCard
        offer={makeOffer({
          state: 'produced',
          score: 82,
          matched: ['C#', 'EF Core'],
          missing: ['Kafka'],
          rationale: 'Strong backend match',
        })}
      />,
    )
    expect(screen.getByText('82')).toBeInTheDocument()
    expect(screen.getByText('/100 fit')).toBeInTheDocument()
    expect(screen.getByText('Strong backend match')).toBeInTheDocument()
    expect(screen.getByText('EF Core')).toBeInTheDocument()
    expect(screen.getByText('missing: Kafka')).toBeInTheDocument()
  })

  it('pending: shows "Fit pending" and no number (FR-005)', () => {
    render(<OfferCard offer={makeOffer({ state: 'pending' })} />)
    expect(screen.getByText('Fit pending')).toBeInTheDocument()
    expect(screen.queryByText('/100 fit')).not.toBeInTheDocument()
  })

  it('failed: shows "Fit unavailable" and no number', () => {
    render(<OfferCard offer={makeOffer({ state: 'failed' })} />)
    expect(screen.getByText('Fit unavailable')).toBeInTheDocument()
    expect(screen.queryByText('/100 fit')).not.toBeInTheDocument()
  })

  it('absent (no produced CV profile): renders no fit block at all', () => {
    render(<OfferCard offer={makeOffer(null)} />)
    expect(screen.queryByTestId('offer-fit')).not.toBeInTheDocument()
    expect(screen.queryByText('Fit pending')).not.toBeInTheDocument()
  })
})
