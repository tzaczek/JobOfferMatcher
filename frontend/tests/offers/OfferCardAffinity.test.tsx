import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { OfferCard } from '../../src/components/OfferCard/OfferCard.tsx'
import type { AffinityDto, FitDto, OfferDto } from '../../src/api/types.ts'

function makeOffer(affinity: AffinityDto | undefined, fit: FitDto | null = null): OfferDto {
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
    affinity,
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

describe('OfferCard affinity — a distinct signal beside fit (T028)', () => {
  it('produced: renders the affinity score, "/100 affinity", rationale and resembles chips', () => {
    render(
      <OfferCard
        offer={makeOffer({
          state: 'produced',
          score: 74,
          resembles: ['senior .NET', 'remote'],
          rationale: 'Close to the roles you applied to.',
        })}
      />,
    )
    expect(screen.getByTestId('offer-affinity')).toBeInTheDocument()
    expect(screen.getByText('74')).toBeInTheDocument()
    expect(screen.getByText('/100 affinity')).toBeInTheDocument()
    // Rationale is collapsed behind a "Why this affinity" expander on the card (finding #7).
    expect(screen.getByText('Why this affinity')).toBeInTheDocument()
    expect(screen.getByText('Close to the roles you applied to.')).toBeInTheDocument()
    // Resembles chips stay visible on the card (they are not part of the deduped skills row).
    expect(screen.getByText('senior .NET')).toBeInTheDocument()
  })

  it('is a SEPARATE block from fit (never blended) — both render distinctly', () => {
    render(
      <OfferCard
        offer={makeOffer(
          { state: 'produced', score: 74, resembles: [], rationale: null },
          { state: 'produced', score: 82, matched: [], missing: [], rationale: null },
        )}
      />,
    )
    expect(screen.getByTestId('offer-fit')).toBeInTheDocument()
    expect(screen.getByTestId('offer-affinity')).toBeInTheDocument()
    expect(screen.getByText('/100 fit')).toBeInTheDocument()
    expect(screen.getByText('/100 affinity')).toBeInTheDocument()
  })

  it('pending: shows "Affinity pending" and no number', () => {
    render(<OfferCard offer={makeOffer({ state: 'pending' })} />)
    expect(screen.getByText('Affinity pending')).toBeInTheDocument()
    expect(screen.queryByText('/100 affinity')).not.toBeInTheDocument()
  })

  it('failed: shows "Affinity unavailable" and no number', () => {
    render(<OfferCard offer={makeOffer({ state: 'failed' })} />)
    expect(screen.getByText('Affinity unavailable')).toBeInTheDocument()
    expect(screen.queryByText('/100 affinity')).not.toBeInTheDocument()
  })

  it('insufficient: shows the cold-start message distinctly (not "pending")', () => {
    render(<OfferCard offer={makeOffer({ state: 'insufficient' })} />)
    expect(screen.getByTestId('affinity-insufficient')).toBeInTheDocument()
    expect(screen.getByText(/not enough application history yet/i)).toBeInTheDocument()
    expect(screen.queryByText('Affinity pending')).not.toBeInTheDocument()
  })
})
