import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../src/api/client.ts'
import { renderWithRouter } from '../testUtils.tsx'
import type { TailoredCvDraftDto, TailoredCvDto } from '../../src/api/types.ts'

const getDraft = vi.fn()
const getTailored = vi.fn()
const generate = vi.fn()
const downloadTailoredPdf = vi.fn()
const deleteTailored = vi.fn()
vi.mock('../../src/api/tailoredCv.ts', () => ({
  getDraft: (...a: unknown[]) => getDraft(...a),
  getTailored: (...a: unknown[]) => getTailored(...a),
  generate: (...a: unknown[]) => generate(...a),
  downloadTailoredPdf: (...a: unknown[]) => downloadTailoredPdf(...a),
  deleteTailored: (...a: unknown[]) => deleteTailored(...a),
  previewUrl: (offerId: string, v?: number) =>
    `/api/tailored-cv/offer/${offerId}/preview${v !== undefined ? `?v=${v}` : ''}`,
}))

import { TailorCvModal } from '../../src/components/TailorCvModal/TailorCvModal.tsx'

function draft(overrides: Partial<TailoredCvDraftDto> = {}): TailoredCvDraftDto {
  return {
    offerId: 'o1',
    offerTitle: 'Senior .NET Engineer',
    company: 'Acme',
    prompt: 'Tailor my CV for Senior .NET Engineer at Acme. Emphasise: C#, PostgreSQL.',
    emphasisedSkills: ['C#', 'PostgreSQL'],
    allOfferSkills: ['C#', 'PostgreSQL', 'Docker', 'React'],
    sourceCv: { id: 'cv1', fileName: 'my-cv.pdf' },
    ...overrides,
  }
}

function view(overrides: Partial<TailoredCvDto> = {}): TailoredCvDto {
  return {
    offerId: 'o1',
    offerTitle: 'Senior .NET Engineer',
    company: 'Acme',
    sourceCvId: 'cv1',
    state: 'pending',
    generationVersion: 1,
    emphasisedSkills: ['C#', 'PostgreSQL'],
    prompt: 'p',
    hasPdf: false,
    generatedAt: null,
    lastError: null,
    ...overrides,
  }
}

const NOT_FOUND = new ApiError('TailoredCvNotFound', 'none', 404)

function renderModal() {
  renderWithRouter(
    <TailorCvModal offerId="o1" offerTitle="Senior .NET Engineer" onClose={() => {}} />,
  )
}

describe('TailorCvModal — draft + generate (US1, T031)', () => {
  beforeEach(() => {
    getDraft.mockReset()
    getTailored.mockReset()
    generate.mockReset()
    downloadTailoredPdf.mockReset()
    deleteTailored.mockReset()
  })

  it('shows the draft: the attached CV, the exact prompt, and the emphasised-skill chips', async () => {
    getDraft.mockResolvedValue(draft())
    getTailored.mockRejectedValue(NOT_FOUND)
    renderModal()

    expect(await screen.findByText('my-cv.pdf')).toBeInTheDocument()
    expect(screen.getByTestId('tailor-prompt')).toHaveValue(draft().prompt)
    expect(screen.getByRole('button', { name: 'C#' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Docker' })).toHaveAttribute('aria-pressed', 'false')
  })

  it('Generate posts the prompt + selection and shows pending', async () => {
    getDraft.mockResolvedValue(draft())
    getTailored.mockRejectedValue(NOT_FOUND) // no existing; poll fetch also rejects → stops at pending
    generate.mockResolvedValue(view({ state: 'pending' }))
    renderModal()

    await screen.findByTestId('tailor-prompt')
    await userEvent.click(screen.getByRole('button', { name: /^Generate$/ }))

    expect(generate).toHaveBeenCalledWith('o1', {
      prompt: draft().prompt,
      emphasisedSkills: ['C#', 'PostgreSQL'],
    })
    expect(await screen.findByTestId('tailor-status')).toHaveTextContent(/pending/i)
  })

  it('a produced result renders the inline preview iframe', async () => {
    getDraft.mockResolvedValue(draft())
    getTailored.mockRejectedValueOnce(NOT_FOUND) // mount: no existing
    generate.mockResolvedValue(view({ state: 'pending' }))
    getTailored.mockResolvedValue(view({ state: 'produced', hasPdf: true })) // poll → produced
    renderModal()

    await screen.findByTestId('tailor-prompt')
    await userEvent.click(screen.getByRole('button', { name: /^Generate$/ }))

    const preview = await screen.findByTestId('tailor-preview')
    expect(preview).toHaveAttribute(
      'src',
      expect.stringContaining('/api/tailored-cv/offer/o1/preview'),
    )
  })

  it('shows the "add a CV first" state when there is no CV', async () => {
    getDraft.mockRejectedValue(new ApiError('NoCvOnFile', 'add a cv', 409))
    renderModal()

    expect(await screen.findByText(/add a cv first/i)).toBeInTheDocument()
  })
})

describe('TailorCvModal — edit + toggle + regenerate (US2, T035)', () => {
  beforeEach(() => {
    getDraft.mockReset()
    getTailored.mockReset()
    generate.mockReset()
  })

  it('toggling a skill recomposes the visible prompt while it is the unedited default', async () => {
    getDraft.mockResolvedValue(draft())
    getTailored.mockRejectedValue(NOT_FOUND)
    renderModal()
    await screen.findByTestId('tailor-prompt')

    getDraft.mockResolvedValueOnce(
      draft({ prompt: 'RECOMPOSED with Docker', emphasisedSkills: ['C#', 'PostgreSQL', 'Docker'] }),
    )
    await userEvent.click(screen.getByRole('button', { name: 'Docker' }))

    expect(getDraft).toHaveBeenLastCalledWith('o1', { skills: ['C#', 'PostgreSQL', 'Docker'] })
    expect(await screen.findByDisplayValue('RECOMPOSED with Docker')).toBeInTheDocument()
  })

  it('once the prompt is edited, toggling no longer clobbers it; Regenerate posts the edited prompt', async () => {
    getDraft.mockResolvedValue(draft())
    getTailored.mockResolvedValue(view({ state: 'produced', hasPdf: true })) // existing ⇒ button says Regenerate
    generate.mockResolvedValue(view({ state: 'pending', generationVersion: 2 }))
    renderModal()

    const textarea = await screen.findByTestId('tailor-prompt')
    await userEvent.clear(textarea)
    await userEvent.type(textarea, 'EDITED PROMPT')

    // Toggling after an edit must not call getDraft again (no recompose) nor change the textarea.
    const callsBefore = getDraft.mock.calls.length
    await userEvent.click(screen.getByRole('button', { name: 'Docker' }))
    expect(getDraft).toHaveBeenCalledTimes(callsBefore)
    expect(screen.getByTestId('tailor-prompt')).toHaveValue('EDITED PROMPT')

    await userEvent.click(screen.getByRole('button', { name: /^Regenerate$/ }))
    expect(generate).toHaveBeenCalledWith('o1', {
      prompt: 'EDITED PROMPT',
      emphasisedSkills: ['C#', 'PostgreSQL', 'Docker'],
    })
  })
})

describe('TailorCvModal — download (US3, T040)', () => {
  beforeEach(() => {
    getDraft.mockReset()
    getTailored.mockReset()
    downloadTailoredPdf.mockReset()
  })

  it('no Download action while pending (not produced)', async () => {
    getDraft.mockResolvedValue(draft())
    getTailored.mockResolvedValue(view({ state: 'pending', hasPdf: false }))
    renderModal()

    await screen.findByTestId('tailor-prompt')
    expect(screen.queryByRole('button', { name: /download pdf/i })).not.toBeInTheDocument()
  })

  it('Download triggers the blob download when produced', async () => {
    getDraft.mockResolvedValue(draft())
    getTailored.mockResolvedValue(view({ state: 'produced', hasPdf: true, generationVersion: 2 }))
    downloadTailoredPdf.mockResolvedValue({ fileName: 'CV - Acme - Senior .NET Engineer.pdf' })
    renderModal()

    const dl = await screen.findByRole('button', { name: /download pdf/i })
    expect(dl).not.toBeDisabled()
    await userEvent.click(dl)
    expect(downloadTailoredPdf).toHaveBeenCalledWith('o1')
  })
})

function OffersRouteStub() {
  const [params] = useSearchParams()
  return <div data-testid="offers-route">{params.get('offerId')}</div>
}

describe('TailorCvModal — view offer (reach-back navigation)', () => {
  beforeEach(() => {
    getDraft.mockReset()
    getTailored.mockReset()
  })

  it('closes the modal and navigates to the offer', async () => {
    getDraft.mockResolvedValue(draft())
    getTailored.mockRejectedValue(NOT_FOUND)
    const onClose = vi.fn()
    render(
      <MemoryRouter initialEntries={['/tailored-cvs']}>
        <Routes>
          <Route
            path="/tailored-cvs"
            element={
              <TailorCvModal offerId="o1" offerTitle="Senior .NET Engineer" onClose={onClose} />
            }
          />
          <Route path="/" element={<OffersRouteStub />} />
        </Routes>
      </MemoryRouter>,
    )

    await screen.findByTestId('tailor-prompt')
    await userEvent.click(screen.getByRole('button', { name: 'View offer' }))

    expect(onClose).toHaveBeenCalled()
    expect(await screen.findByTestId('offers-route')).toHaveTextContent('o1')
  })
})
