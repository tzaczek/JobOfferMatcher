import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../src/api/client.ts'
import type {
  ApplicationBoardDto,
  ApplicationDetailDto,
  PipelineStageDto,
} from '../../src/api/types.ts'
import { renderWithRouter } from '../testUtils.tsx'

const getBoard = vi.fn()
const listStages = vi.fn()
const getApplication = vi.fn()
const moveStage = vi.fn()
const closeApplication = vi.fn()
const reopenApplication = vi.fn()
const addNote = vi.fn()
const addTask = vi.fn()
const updateTask = vi.fn()
const deleteTask = vi.fn()
const uploadDocument = vi.fn()
const deleteDocument = vi.fn()
const downloadDocument = vi.fn()
const addInterview = vi.fn()
const updateInterview = vi.fn()
const deleteInterview = vi.fn()
const addCommunication = vi.fn()
const deleteApplication = vi.fn()
const createStage = vi.fn()
const renameStage = vi.fn()
const reorderStages = vi.fn()
const deleteStage = vi.fn()

vi.mock('../../src/api/applications.ts', () => ({
  getBoard: (...a: unknown[]) => getBoard(...a),
  listStages: (...a: unknown[]) => listStages(...a),
  getApplication: (...a: unknown[]) => getApplication(...a),
  moveStage: (...a: unknown[]) => moveStage(...a),
  closeApplication: (...a: unknown[]) => closeApplication(...a),
  reopenApplication: (...a: unknown[]) => reopenApplication(...a),
  addNote: (...a: unknown[]) => addNote(...a),
  addTask: (...a: unknown[]) => addTask(...a),
  updateTask: (...a: unknown[]) => updateTask(...a),
  deleteTask: (...a: unknown[]) => deleteTask(...a),
  uploadDocument: (...a: unknown[]) => uploadDocument(...a),
  deleteDocument: (...a: unknown[]) => deleteDocument(...a),
  downloadDocument: (...a: unknown[]) => downloadDocument(...a),
  addInterview: (...a: unknown[]) => addInterview(...a),
  updateInterview: (...a: unknown[]) => updateInterview(...a),
  deleteInterview: (...a: unknown[]) => deleteInterview(...a),
  addCommunication: (...a: unknown[]) => addCommunication(...a),
  deleteApplication: (...a: unknown[]) => deleteApplication(...a),
  createStage: (...a: unknown[]) => createStage(...a),
  renameStage: (...a: unknown[]) => renameStage(...a),
  reorderStages: (...a: unknown[]) => reorderStages(...a),
  deleteStage: (...a: unknown[]) => deleteStage(...a),
}))

import { ApplicationsPage } from '../../src/pages/Applications/ApplicationsPage.tsx'
import { ApplicationDrawer } from '../../src/components/ApplicationDrawer/ApplicationDrawer.tsx'
import { PipelineStagesSection } from '../../src/pages/Settings/PipelineStagesSection.tsx'

const STAGES: PipelineStageDto[] = [
  { id: 's-applied', name: 'Applied', position: 0 },
  { id: 's-screening', name: 'Screening', position: 1 },
]

function board(overrides: Partial<ApplicationBoardDto> = {}): ApplicationBoardDto {
  return {
    stages: [
      {
        id: 's-applied',
        name: 'Applied',
        position: 0,
        applications: [
          {
            offerId: 'o1',
            title: 'Senior .NET Engineer',
            company: 'Acme',
            stageId: 's-applied',
            status: 'active',
            outcome: null,
            appliedAt: '2026-06-20T00:00:00Z',
            outstandingTaskCount: 1,
            overdueTaskCount: 1,
            nextInterviewAt: null,
          },
        ],
      },
      { id: 's-screening', name: 'Screening', position: 1, applications: [] },
    ],
    closed: [
      {
        offerId: 'o2',
        title: 'Rejected Role',
        company: 'Beta',
        stageId: 's-applied',
        status: 'closed',
        outcome: 'rejected',
        appliedAt: '2026-06-10T00:00:00Z',
        outstandingTaskCount: 0,
        overdueTaskCount: 0,
        nextInterviewAt: null,
      },
    ],
    ...overrides,
  }
}

function detail(overrides: Partial<ApplicationDetailDto> = {}): ApplicationDetailDto {
  return {
    offerId: 'o1',
    title: 'Senior .NET Engineer',
    company: 'Acme',
    stageId: 's-applied',
    status: 'active',
    outcome: null,
    appliedAt: '2026-06-20T00:00:00Z',
    closedAt: null,
    timeline: [
      { occurredAt: '2026-06-20T00:00:00Z', kind: 'note', title: 'Note added', detail: 'hello' },
    ],
    notes: [{ id: 'n1', body: 'hello', createdAt: '2026-06-20T00:00:00Z' }],
    tasks: [],
    documents: [],
    communications: [],
    interviews: [],
    ...overrides,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('ApplicationsPage board', () => {
  it('renders stage columns with cards, an overdue badge, and the closed section', async () => {
    getBoard.mockResolvedValue(board())
    listStages.mockResolvedValue(STAGES)

    renderWithRouter(<ApplicationsPage />)

    expect(await screen.findByText('Senior .NET Engineer')).toBeInTheDocument()
    expect(screen.getByTestId('overdue-badge')).toHaveTextContent('1 overdue')
    expect(screen.getByText('Closed')).toBeInTheDocument()
    expect(screen.getByText('Rejected Role')).toBeInTheDocument()
  })

  it('shows the empty state when there are no applications', async () => {
    getBoard.mockResolvedValue({
      stages: STAGES.map((s) => ({ ...s, applications: [] })),
      closed: [],
    })
    listStages.mockResolvedValue(STAGES)

    renderWithRouter(<ApplicationsPage />)

    expect(await screen.findByTestId('applications-empty')).toBeInTheDocument()
  })

  it('each card links back to its offer via ?offerId=, separately from opening the drawer', async () => {
    getBoard.mockResolvedValue(board())
    listStages.mockResolvedValue(STAGES)

    renderWithRouter(<ApplicationsPage />)

    await screen.findByText('Senior .NET Engineer')
    const links = screen.getAllByRole('link', { name: 'View offer' })
    expect(links.map((l) => l.getAttribute('href'))).toEqual(['/?offerId=o1', '/?offerId=o2'])
  })
})

describe('ApplicationDrawer lifecycle', () => {
  it('moves stage, closes with an outcome, and reopens', async () => {
    const user = userEvent.setup()
    getApplication.mockResolvedValue(detail())
    moveStage.mockResolvedValue(undefined)
    closeApplication.mockResolvedValue(undefined)

    renderWithRouter(
      <ApplicationDrawer offerId="o1" stages={STAGES} onClose={() => {}} onChanged={() => {}} />,
    )

    // Move to Screening.
    await user.selectOptions(await screen.findByTestId('stage-select'), 's-screening')
    expect(moveStage).toHaveBeenCalledWith('o1', 's-screening')

    // Close as accepted.
    await user.selectOptions(screen.getByTestId('close-outcome'), 'accepted')
    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(closeApplication).toHaveBeenCalledWith('o1', 'accepted')
  })

  it('reopens a closed application', async () => {
    const user = userEvent.setup()
    getApplication.mockResolvedValue(
      detail({ status: 'closed', outcome: 'rejected', closedAt: '2026-06-25T00:00:00Z' }),
    )
    reopenApplication.mockResolvedValue(undefined)

    renderWithRouter(
      <ApplicationDrawer offerId="o1" stages={STAGES} onClose={() => {}} onChanged={() => {}} />,
    )

    await user.click(await screen.findByRole('button', { name: 'Reopen' }))
    expect(reopenApplication).toHaveBeenCalledWith('o1')
  })

  it('adds a note from the Notes tab', async () => {
    const user = userEvent.setup()
    getApplication.mockResolvedValue(detail({ notes: [] }))
    addNote.mockResolvedValue({ id: 'n2', body: 'call back', createdAt: '2026-06-21T00:00:00Z' })

    renderWithRouter(
      <ApplicationDrawer offerId="o1" stages={STAGES} onClose={() => {}} onChanged={() => {}} />,
    )

    await user.click(await screen.findByRole('tab', { name: 'Notes' }))
    await user.type(screen.getByPlaceholderText('Add a note…'), 'call back')
    await user.click(screen.getByRole('button', { name: 'Add note' }))
    expect(addNote).toHaveBeenCalledWith('o1', 'call back')
  })

  it('View offer closes the drawer and navigates to the offer', async () => {
    const user = userEvent.setup()
    getApplication.mockResolvedValue(detail())
    const onClose = vi.fn()
    render(
      <MemoryRouter initialEntries={['/applications']}>
        <Routes>
          <Route
            path="/applications"
            element={
              <ApplicationDrawer
                offerId="o1"
                stages={STAGES}
                onClose={onClose}
                onChanged={() => {}}
              />
            }
          />
          <Route path="/" element={<OffersRouteStub />} />
        </Routes>
      </MemoryRouter>,
    )

    await screen.findByTestId('stage-select')
    await user.click(screen.getByRole('button', { name: 'View offer' }))

    expect(onClose).toHaveBeenCalled()
    expect(await screen.findByTestId('offers-route')).toHaveTextContent('o1')
  })
})

function OffersRouteStub() {
  const [params] = useSearchParams()
  return <div data-testid="offers-route">{params.get('offerId')}</div>
}

describe('PipelineStagesSection', () => {
  it('adds a stage', async () => {
    const user = userEvent.setup()
    listStages.mockResolvedValue(STAGES)
    createStage.mockResolvedValue({ id: 's-new', name: 'Offer', position: 2 })

    render(<PipelineStagesSection />)

    await screen.findByDisplayValue('Applied')
    await user.type(screen.getByLabelText('New stage name'), 'Offer')
    await user.click(screen.getByRole('button', { name: 'Add stage' }))
    expect(createStage).toHaveBeenCalledWith('Offer')
  })

  it('asks to reassign before removing an occupied stage', async () => {
    const user = userEvent.setup()
    listStages.mockResolvedValue(STAGES)
    deleteStage.mockRejectedValueOnce(new ApiError('StageInUse', 'in use', 409))
    deleteStage.mockResolvedValueOnce(undefined)

    render(<PipelineStagesSection />)

    await screen.findByDisplayValue('Applied')
    const firstRow = screen.getByDisplayValue('Applied').closest('li') as HTMLElement
    await user.click(within(firstRow).getByRole('button', { name: 'Remove stage' }))

    // The reassign UI appears; confirming reassigns to the other stage then removes.
    expect(await screen.findByText('Move its applications to:')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Reassign & remove' }))
    expect(deleteStage).toHaveBeenLastCalledWith('s-applied', 's-screening')
  })
})
