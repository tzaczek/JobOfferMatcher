import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import type { ScanStatusDto } from '../../src/api/types.ts'
import { ScanBanner } from '../../src/pages/Offers/ScanBanner.tsx'

function incomplete(reason: string | null): ScanStatusDto {
  return {
    state: 'incomplete',
    outcome: 'partial',
    counts: { collected: 0, new: 0, updated: 0, unavailable: 0, failed: 0 },
    incompleteReason: reason,
  }
}

describe('ScanBanner LinkedIn login states (T033)', () => {
  it('shows the login-window hint while scanning a login-required source', () => {
    render(<ScanBanner scanning status={null} error={null} awaitingLogin />)
    expect(screen.getByTestId('login-hint')).toHaveTextContent(/finish signing in there/i)
  })

  it('does not show the hint when no login-required source is being scanned', () => {
    render(<ScanBanner scanning status={null} error={null} awaitingLogin={false} />)
    expect(screen.queryByTestId('login-hint')).not.toBeInTheDocument()
  })

  it('specialises the incomplete branch for LoginNotCompleted', () => {
    render(<ScanBanner scanning={false} status={incomplete('LoginNotCompleted')} error={null} />)
    expect(screen.getByTestId('login-required')).toHaveTextContent(
      /LinkedIn login required — run a manual scan to sign in/i,
    )
  })

  it('keeps the generic incomplete message for other reasons', () => {
    render(<ScanBanner scanning={false} status={incomplete('ChallengeDetected')} error={null} />)
    expect(screen.queryByTestId('login-required')).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/Scan incomplete \(ChallengeDetected\)/i)
  })
})
