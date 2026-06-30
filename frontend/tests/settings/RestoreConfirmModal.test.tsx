import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RestoreConfirmModal } from '../../src/pages/Settings/RestoreConfirmModal.tsx'

describe('RestoreConfirmModal (US2, T030)', () => {
  it('shows the file name and a destructive warning', () => {
    render(<RestoreConfirmModal fileName="jobs-backup.zip" onConfirm={vi.fn()} onClose={vi.fn()} />)
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()
    expect(screen.getByText(/jobs-backup\.zip/)).toBeInTheDocument()
    expect(screen.getByText(/replace everything/i)).toBeInTheDocument()
  })

  it('Cancel aborts without restoring', async () => {
    const onConfirm = vi.fn()
    const onClose = vi.fn()
    render(<RestoreConfirmModal fileName="b.zip" onConfirm={onConfirm} onClose={onClose} />)

    await userEvent.click(screen.getByRole('button', { name: /cancel/i }))
    expect(onClose).toHaveBeenCalledTimes(1)
    expect(onConfirm).not.toHaveBeenCalled()
  })

  it('Confirm fires the restore', async () => {
    const onConfirm = vi.fn()
    render(<RestoreConfirmModal fileName="b.zip" onConfirm={onConfirm} onClose={vi.fn()} />)

    await userEvent.click(screen.getByRole('button', { name: /^restore$/i }))
    expect(onConfirm).toHaveBeenCalledTimes(1)
  })

  it('disables the actions and shows a busy label while restoring', () => {
    render(<RestoreConfirmModal fileName="b.zip" onConfirm={vi.fn()} onClose={vi.fn()} busy />)
    expect(screen.getByRole('button', { name: /restoring/i })).toBeDisabled()
    expect(screen.getByRole('button', { name: /cancel/i })).toBeDisabled()
  })
})
