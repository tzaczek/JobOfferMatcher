import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApiError } from '../../src/api/client.ts'
import type { BackupInspectionDto } from '../../src/api/types.ts'

const downloadBackup = vi.fn()
const inspectBackup = vi.fn()
const restoreBackup = vi.fn()
vi.mock('../../src/api/backup.ts', () => ({
  downloadBackup: (...a: unknown[]) => downloadBackup(...a),
  inspectBackup: (...a: unknown[]) => inspectBackup(...a),
  restoreBackup: (...a: unknown[]) => restoreBackup(...a),
}))

import { BackupSection } from '../../src/pages/Settings/BackupSection.tsx'

describe('BackupSection — Backup (US1, T016)', () => {
  beforeEach(() => {
    downloadBackup.mockReset()
    inspectBackup.mockReset()
    restoreBackup.mockReset()
  })

  it('renders the Backup button and invokes downloadBackup on click', async () => {
    downloadBackup.mockResolvedValue({ fileName: 'jobs-backup-2026-06-29-150140.zip' })
    render(<BackupSection />)

    const btn = screen.getByRole('button', { name: /^backup$/i })
    await userEvent.click(btn)

    expect(downloadBackup).toHaveBeenCalledTimes(1)
    expect(await screen.findByRole('status')).toHaveTextContent(/jobs-backup-2026-06-29-150140\.zip/)
  })

  it('shows a busy state while the backup is preparing', async () => {
    let resolve: (v: { fileName: string }) => void = () => {}
    downloadBackup.mockReturnValue(new Promise<{ fileName: string }>((r) => (resolve = r)))
    render(<BackupSection />)

    await userEvent.click(screen.getByRole('button', { name: /^backup$/i }))
    expect(screen.getByRole('button', { name: /preparing backup/i })).toBeDisabled()

    resolve({ fileName: 'jobs-backup.zip' })
    await waitFor(() => expect(screen.getByRole('button', { name: /^backup$/i })).not.toBeDisabled())
  })

  it('shows an error message when the backup fails', async () => {
    downloadBackup.mockRejectedValue(new ApiError('BusyMaintenance', 'A backup or restore is already running.', 409))
    render(<BackupSection />)

    await userEvent.click(screen.getByRole('button', { name: /^backup$/i }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/already running/i)
  })
})

function inspection(overrides: Partial<BackupInspectionDto> = {}): BackupInspectionDto {
  return {
    valid: true,
    createdAtUtc: '2026-06-29T14:32:10Z',
    appProductVersion: '.NET 10.0.0',
    migrationTip: '20260629155706_AppliedFlag',
    compatibility: 'Same',
    tableCounts: { offers: 184 },
    cvFileCount: 1,
    totalCvBytes: 524288,
    warnings: [],
    ...overrides,
  }
}

const ZIP = new File([new Uint8Array([1, 2, 3])], 'backup.zip', { type: 'application/zip' })

describe('BackupSection — Restore + inspect (US2/US3, T042)', () => {
  beforeEach(() => {
    downloadBackup.mockReset()
    inspectBackup.mockReset()
    restoreBackup.mockReset()
  })

  it('selecting a file shows the inspect summary and enables Restore', async () => {
    inspectBackup.mockResolvedValue(inspection())
    render(<BackupSection />)

    fireEvent.change(screen.getByTestId('restore-file-input'), { target: { files: [ZIP] } })

    expect(await screen.findByTestId('inspect-summary')).toBeInTheDocument()
    expect(screen.getByText(/Offers: 184/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^restore$/i })).not.toBeDisabled()
  })

  it('an invalid file shows an error and no Restore action', async () => {
    inspectBackup.mockRejectedValue(new ApiError('InvalidArchive', 'not a backup', 400))
    render(<BackupSection />)

    fireEvent.change(screen.getByTestId('restore-file-input'), { target: { files: [ZIP] } })

    expect(await screen.findByRole('alert')).toHaveTextContent(/not a valid backup/i)
    expect(screen.queryByTestId('inspect-summary')).not.toBeInTheDocument()
    // The Restore action stays disabled for an invalid file (quickstart US3).
    expect(screen.getByRole('button', { name: /^restore$/i })).toBeDisabled()
  })

  it('a newer backup keeps Restore disabled', async () => {
    inspectBackup.mockResolvedValue(inspection({ compatibility: 'Newer' }))
    render(<BackupSection />)

    fireEvent.change(screen.getByTestId('restore-file-input'), { target: { files: [ZIP] } })

    expect(await screen.findByTestId('inspect-summary')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^restore$/i })).toBeDisabled()
  })

  it('confirming the restore uploads the file and reports the safety backup', async () => {
    inspectBackup.mockResolvedValue(inspection())
    restoreBackup.mockResolvedValue({
      restoredAtUtc: '2026-06-29T15:01:44Z',
      compatibility: 'Same',
      tableCounts: { offers: 184 },
      cvFileCount: 1,
      safetyBackupPath: '/backups/jobs-safety-2026-06-29-150140.zip',
      backfillApplied: false,
    })
    render(<BackupSection />)

    fireEvent.change(screen.getByTestId('restore-file-input'), { target: { files: [ZIP] } })
    await userEvent.click(await screen.findByRole('button', { name: /^restore$/i }))

    // Confirm modal opens; confirm fires the upload.
    const dialog = await screen.findByRole('alertdialog')
    await userEvent.click(within(dialog).getByRole('button', { name: /^restore$/i }))

    await waitFor(() => expect(restoreBackup).toHaveBeenCalledWith(ZIP))
    expect(await screen.findByRole('status')).toHaveTextContent(/safety backup was saved/i)
  })
})
