import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { EnrichmentSettingsDto, EnrichmentStatusDto } from '../../src/api/types.ts'

// ---- enrichment status/re-run (indicator) ----
const getEnrichmentStatus = vi.fn()
const triggerRerun = vi.fn()
vi.mock('../../src/api/enrichment.ts', () => ({
  getEnrichmentStatus: (...a: unknown[]) => getEnrichmentStatus(...a),
  triggerRerun: (...a: unknown[]) => triggerRerun(...a),
}))

// ---- enrichment settings (section) ----
const getEnrichmentSettings = vi.fn()
const updateEnrichmentSettings = vi.fn()
vi.mock('../../src/api/settings.ts', () => ({
  getEnrichmentSettings: (...a: unknown[]) => getEnrichmentSettings(...a),
  updateEnrichmentSettings: (...a: unknown[]) => updateEnrichmentSettings(...a),
}))

import { EnrichmentIndicator } from '../../src/components/EnrichmentIndicator/EnrichmentIndicator.tsx'
import { EnrichmentSection } from '../../src/pages/Settings/EnrichmentSection.tsx'

function status(overrides: Partial<EnrichmentStatusDto> = {}): EnrichmentStatusDto {
  return {
    pendingTotal: 0,
    pendingProfiles: 0,
    pendingSummaries: 0,
    pendingFits: 0,
    failedTotal: 0,
    hasProducedProfile: true,
    lastResultAt: null,
    ...overrides,
  }
}

const SETTINGS: EnrichmentSettingsDto = {
  offerSummaryMaxWords: 60,
  cvSummaryMaxWords: 60,
  maxKeySkills: 10,
  fitRationaleMaxWords: 30,
  retryLimit: 3,
}

describe('EnrichmentIndicator (T059)', () => {
  beforeEach(() => {
    getEnrichmentStatus.mockReset()
    triggerRerun.mockReset()
  })

  it('shows pending and failed counts and the /enrich hint', async () => {
    getEnrichmentStatus.mockResolvedValue(status({ pendingTotal: 12, failedTotal: 2 }))
    render(<EnrichmentIndicator />)
    expect(await screen.findByTestId('pending-count')).toHaveTextContent('12 pending')
    expect(screen.getByTestId('failed-count')).toHaveTextContent('2 failed')
    expect(screen.getByText(/enrich/)).toBeInTheDocument()
  })

  it('re-runs failed and refreshes the counts', async () => {
    getEnrichmentStatus
      .mockResolvedValueOnce(status({ pendingTotal: 0, failedTotal: 2 }))
      // Re-running re-arms the failed items as pending; the live status poll now reflects that count.
      .mockResolvedValue(status({ pendingTotal: 2, failedTotal: 0 }))
    triggerRerun.mockResolvedValue(status({ pendingTotal: 2, failedTotal: 0 }))
    render(<EnrichmentIndicator />)

    const btn = await screen.findByRole('button', { name: /re-run failed/i })
    await userEvent.click(btn)

    expect(triggerRerun).toHaveBeenCalledWith('failed')
    expect(await screen.findByTestId('pending-count')).toHaveTextContent('2 pending')
  })

  it('shows "up to date" when nothing is pending or failed', async () => {
    getEnrichmentStatus.mockResolvedValue(status())
    render(<EnrichmentIndicator />)
    expect(await screen.findByText(/up to date/i)).toBeInTheDocument()
  })
})

describe('EnrichmentSection (T059)', () => {
  beforeEach(() => {
    getEnrichmentSettings.mockReset()
    updateEnrichmentSettings.mockReset()
  })

  it('loads the caps and saves edits', async () => {
    getEnrichmentSettings.mockResolvedValue(SETTINGS)
    updateEnrichmentSettings.mockResolvedValue({ ...SETTINGS, maxKeySkills: 8 })
    render(<EnrichmentSection />)

    const skills = (await screen.findByLabelText('Max key skills')) as HTMLInputElement
    expect(skills.value).toBe('10')
    await userEvent.clear(skills)
    await userEvent.type(skills, '8')

    await userEvent.click(screen.getByRole('button', { name: /save enrichment settings/i }))
    await waitFor(() => expect(updateEnrichmentSettings).toHaveBeenCalled())
    expect(updateEnrichmentSettings.mock.calls[0][0].maxKeySkills).toBe(8)
  })
})
