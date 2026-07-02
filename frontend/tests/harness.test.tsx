// Confirms the Vitest + React Testing Library + jsdom + jest-dom harness is wired up (T019a).
import { describe, it, expect } from 'vitest'
import { screen } from '@testing-library/react'
import { statusChipClass, fitColorVar } from '../src/theme/index.ts'
import { OffersPage } from '../src/pages/Offers/OffersPage.tsx'
import { renderWithRouter } from './testUtils.tsx'

describe('test harness', () => {
  it('maps user status to a chip class', () => {
    expect(statusChipClass('interested')).toBe('chip chip--interested')
  })

  it('maps fit score to a color band', () => {
    expect(fitColorVar(90)).toBe('var(--c-fit-high)')
    expect(fitColorVar(50)).toBe('var(--c-fit-mid)')
    expect(fitColorVar(10)).toBe('var(--c-fit-low)')
  })

  it('renders a component with jest-dom matchers available', () => {
    renderWithRouter(<OffersPage />)
    expect(screen.getByRole('heading', { name: 'Offers' })).toBeInTheDocument()
  })
})
