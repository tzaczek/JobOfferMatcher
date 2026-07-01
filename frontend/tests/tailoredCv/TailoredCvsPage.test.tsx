import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import type { OfferDto, TailoredCvDto } from '../../src/api/types.ts'

const listTailored = vi.fn()
const deleteTailored = vi.fn()
const downloadTailoredPdf = vi.fn()
vi.mock('../../src/api/tailoredCv.ts', () => ({
  listTailored: (...a: unknown[]) => listTailored(...a),
  deleteTailored: (...a: unknown[]) => deleteTailored(...a),
  downloadTailoredPdf: (...a: unknown[]) => downloadTailoredPdf(...a),
  getDraft: vi.fn(),
  getTailored: vi.fn(),
  generate: vi.fn(),
  previewUrl: (offerId: string) => `/api/tailored-cv/offer/${offerId}/preview`,
}))

import { TailoredCvsPage } from '../../src/pages/TailoredCvs/TailoredCvsPage.tsx'
import { OfferCard } from '../../src/components/OfferCard/OfferCard.tsx'

function tcv(overrides: Partial<TailoredCvDto> = {}): TailoredCvDto {
  return {
    offerId: 'o1',
    offerTitle: 'Senior .NET Engineer',
    company: 'Acme',
    sourceCvId: 'cv1',
    state: 'produced',
    generationVersion: 1,
    emphasisedSkills: ['C#'],
    prompt: 'p',
    hasPdf: true,
    generatedAt: '2026-06-30T12:00:00Z',
    lastError: null,
    ...overrides,
  }
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
    requiredSkills: ['C#'],
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
    applied: false,
    ...overrides,
  }
}

describe('TailoredCvsPage (US4, T047)', () => {
  beforeEach(() => {
    listTailored.mockReset()
    deleteTailored.mockReset()
    downloadTailoredPdf.mockReset()
  })

  it('lists every tailored CV with a reach-back to its offer', async () => {
    listTailored.mockResolvedValue({ data: [tcv(), tcv({ offerId: 'o2', offerTitle: 'Backend Developer' })] })
    render(<TailoredCvsPage />)

    expect(await screen.findByText('Senior .NET Engineer')).toBeInTheDocument()
    expect(screen.getByText('Backend Developer')).toBeInTheDocument()
    expect(screen.getAllByTestId('tailored-cv-row')).toHaveLength(2)
    // The title is the reach-back into the offer-scoped modal.
    expect(screen.getAllByTestId('tailored-cv-open').length).toBe(2)
  })

  it('shows an empty state when there are none', async () => {
    listTailored.mockResolvedValue({ data: [] })
    render(<TailoredCvsPage />)

    expect(await screen.findByTestId('empty-state')).toBeInTheDocument()
  })
})

describe('OfferCard tailored-CV indicator (US4, T047)', () => {
  it('shows the indicator + a reopen label when a tailored CV exists', () => {
    render(<OfferCard offer={makeOffer()} tailoredState="produced" />)

    expect(screen.getByTestId('tailored-indicator')).toHaveTextContent(/tailored cv/i)
    expect(screen.getByRole('button', { name: /^Tailored CV$/ })).toBeInTheDocument()
  })

  it('shows the "Tailor CV" action when none exists', () => {
    render(<OfferCard offer={makeOffer()} />)

    expect(screen.queryByTestId('tailored-indicator')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^Tailor CV$/ })).toBeInTheDocument()
  })
})
