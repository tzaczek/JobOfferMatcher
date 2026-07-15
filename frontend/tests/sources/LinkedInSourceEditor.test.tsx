import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { SourceDto } from '../../src/api/types.ts'

const listSources = vi.fn()
const createSource = vi.fn()
const updateSource = vi.fn()
const enableSource = vi.fn()
const disableSource = vi.fn()
vi.mock('../../src/api/sources.ts', () => ({
  listSources: (...a: unknown[]) => listSources(...a),
  createSource: (...a: unknown[]) => createSource(...a),
  updateSource: (...a: unknown[]) => updateSource(...a),
  enableSource: (...a: unknown[]) => enableSource(...a),
  disableSource: (...a: unknown[]) => disableSource(...a),
}))

import { SourcesPage } from '../../src/pages/Sources/SourcesPage.tsx'

function linkedInSource(): SourceDto {
  return {
    id: 'src-1',
    name: 'LinkedIn',
    kind: 'InteractiveBrowser',
    requiresLogin: true,
    enabled: true,
    searchCriteria: {
      categories: [],
      experienceLevels: [],
      employmentTypes: [],
      workingTimes: [],
      withSalary: false,
      sortBy: null,
      orderBy: null,
      workplaceKeep: [],
      includeRecommended: true,
      linkedInSearches: [
        {
          keywords: 'Senior .NET Software Engineer',
          location: 'Kraków',
          geoId: '90009828',
          distance: 50,
          recency: 'r1296000',
        },
      ],
    },
  }
}

describe('LinkedIn source editor (T023)', () => {
  beforeEach(() => {
    listSources.mockReset()
    createSource.mockReset()
    updateSource.mockReset()
  })

  it('shows the LinkedIn fields only for InteractiveBrowser and saves them', async () => {
    listSources.mockResolvedValue({ data: [] })
    createSource.mockResolvedValue(linkedInSource())
    const { container } = render(<SourcesPage />)

    // Open the create form (header button); DirectApi fields are the default.
    await userEvent.click(await screen.findByRole('button', { name: 'Add source' }))
    expect(screen.getByPlaceholderText('backend, devops')).toBeInTheDocument() // DirectApi "Categories"
    expect(screen.queryByTestId('linkedin-fields')).not.toBeInTheDocument()

    // Switch the kind → the DirectApi fields give way to the LinkedIn section.
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Kind' }),
      'InteractiveBrowser',
    )
    expect(screen.getByTestId('linkedin-fields')).toBeInTheDocument()
    expect(screen.queryByPlaceholderText('backend, devops')).not.toBeInTheDocument()

    // Fill name, turn on the recommended feed, add a saved search.
    await userEvent.type(screen.getByPlaceholderText('JustJoin.it — .NET, Kraków'), 'LinkedIn')
    await userEvent.click(screen.getByRole('checkbox', { name: /Recommended.*feed/i }))
    await userEvent.click(screen.getByRole('button', { name: 'Add search' }))
    await userEvent.type(screen.getByLabelText('Search 1 keywords'), 'Senior .NET')

    // Submit (the enabled in-form submit, not the disabled header button).
    await userEvent.click(container.querySelector('button[type="submit"]')!)

    await waitFor(() => expect(createSource).toHaveBeenCalled())
    const body = createSource.mock.calls[0][0]
    expect(body.kind).toBe('InteractiveBrowser')
    expect(body.searchCriteria.includeRecommended).toBe(true)
    expect(body.searchCriteria.linkedInSearches).toHaveLength(1)
    expect(body.searchCriteria.linkedInSearches[0].keywords).toBe('Senior .NET')
  })

  it('renders a saved LinkedIn source with its recommended + searches summary', async () => {
    listSources.mockResolvedValue({ data: [linkedInSource()] })
    render(<SourcesPage />)

    expect(await screen.findByText('Recommended feed')).toBeInTheDocument()
    expect(screen.getByText('Saved searches')).toBeInTheDocument()
    expect(screen.getByText('Senior .NET Software Engineer')).toBeInTheDocument()
  })
})
