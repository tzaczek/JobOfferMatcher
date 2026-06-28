import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { OfferCard } from '../../src/components/OfferCard/OfferCard.tsx'
import type { OfferDto } from '../../src/api/types.ts'

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
    requiredSkills: ['C#', '.NET', 'Azure'],
    niceToHaveSkills: [],
    salaryBands: [
      { min: 18000, max: 22000, currency: 'PLN', period: 'monthly', basis: 'b2b', tax: 'net' },
    ],
    normalizedSalary: null,
    fit: null,
    canonicalUrl: 'https://justjoin.it/job-offers/senior-dotnet-engineer-acme-krakow',
    isNew: true,
    isUpdated: false,
    availability: 'available',
    firstSeenAt: '2026-06-25T08:00:00Z',
    firstSuggestedAt: '2026-06-25T08:00:00Z',
    lastSeenAt: '2026-06-26T08:00:00Z',
    userStatus: 'new',
    ...overrides,
  }
}

describe('OfferCard (T023a)', () => {
  it('renders title, company and core details', () => {
    render(<OfferCard offer={makeOffer()} />)
    expect(screen.getByText('Senior .NET Engineer')).toBeInTheDocument()
    expect(screen.getByText('Acme Software')).toBeInTheDocument()
    expect(screen.getByText('Kraków')).toBeInTheDocument()
    expect(screen.getByText('Remote')).toBeInTheDocument()
  })

  it('renders the raw salary band verbatim', () => {
    render(<OfferCard offer={makeOffer()} />)
    expect(screen.getByText(/18,000–22,000 PLN net\/mo \(B2B\)/)).toBeInTheDocument()
  })

  it('shows "Salary not disclosed" when there are no bands (FR-010)', () => {
    render(<OfferCard offer={makeOffer({ salaryBands: [] })} />)
    expect(screen.getByText('Salary not disclosed')).toBeInTheDocument()
  })

  it('links to the working canonical offer URL in a new tab (FR-029)', () => {
    render(<OfferCard offer={makeOffer()} />)
    const link = screen.getByRole('link', { name: /view offer/i })
    expect(link).toHaveAttribute(
      'href',
      'https://justjoin.it/job-offers/senior-dotnet-engineer-acme-krakow',
    )
    expect(link).toHaveAttribute('target', '_blank')
  })

  it('shows the New badge for new offers', () => {
    render(<OfferCard offer={makeOffer({ isNew: true })} />)
    expect(screen.getByText('New')).toBeInTheDocument()
  })
})
