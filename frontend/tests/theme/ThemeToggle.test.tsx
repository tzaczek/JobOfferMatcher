// Verifies the dark-mode toggle flips the document theme, persists the choice,
// and stays accessible.
import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ThemeToggle } from '../../src/components/ThemeToggle/ThemeToggle.tsx'

beforeEach(() => {
  localStorage.clear()
  delete document.documentElement.dataset.theme
})

describe('ThemeToggle', () => {
  it('defaults to light and offers to switch to dark', () => {
    render(<ThemeToggle />)
    const btn = screen.getByRole('button', { name: 'Switch to dark mode' })
    expect(btn).toHaveAttribute('aria-pressed', 'false')
  })

  it('switches to dark on click and persists the choice', () => {
    render(<ThemeToggle />)
    fireEvent.click(screen.getByRole('button', { name: 'Switch to dark mode' }))

    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(localStorage.getItem('jom-theme')).toBe('dark')

    const btn = screen.getByRole('button', { name: 'Switch to light mode' })
    expect(btn).toHaveAttribute('aria-pressed', 'true')
  })

  it('toggles back to light on a second click', () => {
    render(<ThemeToggle />)
    fireEvent.click(screen.getByRole('button', { name: 'Switch to dark mode' }))
    fireEvent.click(screen.getByRole('button', { name: 'Switch to light mode' }))

    expect(document.documentElement.dataset.theme).toBe('light')
    expect(localStorage.getItem('jom-theme')).toBe('light')
    expect(screen.getByRole('button', { name: 'Switch to dark mode' })).toBeInTheDocument()
  })

  it('honours a stored dark preference on mount', () => {
    localStorage.setItem('jom-theme', 'dark')
    render(<ThemeToggle />)
    expect(screen.getByRole('button', { name: 'Switch to light mode' })).toBeInTheDocument()
  })
})
