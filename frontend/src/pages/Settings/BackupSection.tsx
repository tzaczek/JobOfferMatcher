import { useRef, useState } from 'react'
import { downloadBackup, inspectBackup, restoreBackup } from '../../api/backup.ts'
import { ApiError } from '../../api/client.ts'
import type { BackupCompatibility, BackupInspectionDto } from '../../api/types.ts'
import { RestoreConfirmModal } from './RestoreConfirmModal.tsx'

type Msg = { kind: 'ok' | 'error'; text: string } | null

const COMPAT_CHIP: Record<BackupCompatibility, string> = {
  Same: 'chip chip--produced',
  Older: 'chip chip--updated',
  Newer: 'chip chip--missing',
}

const COMPAT_LABEL: Record<BackupCompatibility, string> = {
  Same: 'Same version',
  Older: 'Older version — will upgrade on restore',
  Newer: 'Newer version — cannot restore',
}

/**
 * Backup & Restore settings card (003 US1–US3). Backup downloads a complete archive; Restore picks a
 * file, inspects it (read-only summary + compatibility badge), then — behind an explicit confirm — uploads
 * it for a guarded, all-or-nothing restore. The Restore button is gated on a valid, non-newer backup.
 */
export function BackupSection() {
  const [backingUp, setBackingUp] = useState(false)
  const [backupMsg, setBackupMsg] = useState<Msg>(null)

  const [file, setFile] = useState<File | null>(null)
  const [inspecting, setInspecting] = useState(false)
  const [inspection, setInspection] = useState<BackupInspectionDto | null>(null)
  const [restoreMsg, setRestoreMsg] = useState<Msg>(null)
  const [confirming, setConfirming] = useState(false)
  const [restoring, setRestoring] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  async function handleBackup() {
    setBackingUp(true)
    setBackupMsg(null)
    try {
      const { fileName } = await downloadBackup()
      setBackupMsg({ kind: 'ok', text: `Backup downloaded: ${fileName}` })
    } catch (err) {
      setBackupMsg({ kind: 'error', text: err instanceof ApiError ? err.message : 'Backup failed. Please try again.' })
    } finally {
      setBackingUp(false)
    }
  }

  async function handleFilePicked(picked: File) {
    setFile(picked)
    setInspection(null)
    setRestoreMsg(null)
    setInspecting(true)
    try {
      const result = await inspectBackup(picked)
      setInspection(result)
    } catch (err) {
      setRestoreMsg({
        kind: 'error',
        text: err instanceof ApiError ? `Not a valid backup: ${err.message}` : 'That file is not a valid backup.',
      })
    } finally {
      setInspecting(false)
    }
  }

  async function handleRestore() {
    if (!file) return
    setRestoring(true)
    setRestoreMsg(null)
    try {
      const report = await restoreBackup(file)
      setConfirming(false)
      setInspection(null)
      setFile(null)
      if (fileInputRef.current) fileInputRef.current.value = ''
      setRestoreMsg({
        kind: 'ok',
        text: `Restore complete (${report.compatibility}). A safety backup was saved to ${report.safetyBackupPath}.`,
      })
    } catch (err) {
      setRestoreMsg({ kind: 'error', text: err instanceof ApiError ? err.message : 'Restore failed.' })
    } finally {
      setRestoring(false)
    }
  }

  const canRestore = inspection?.valid === true && inspection.compatibility !== 'Newer'

  return (
    <section className="card settings-card">
      <h2 className="settings-card__title">Backup &amp; Restore</h2>
      <p className="muted text-sm">
        Download a complete backup of everything — the database and your uploaded CV files — as a single
        archive. Keep it somewhere safe; it contains personal data and is not encrypted.
      </p>

      {backupMsg?.kind === 'error' && (
        <div className="settings-msg settings-msg--error" role="alert">
          {backupMsg.text}
        </div>
      )}
      {backupMsg?.kind === 'ok' && (
        <div className="settings-msg settings-msg--ok" role="status">
          {backupMsg.text}
        </div>
      )}

      <div className="settings-actions">
        <button type="button" className="btn btn--primary" onClick={handleBackup} disabled={backingUp}>
          {backingUp ? 'Preparing backup…' : 'Backup'}
        </button>
      </div>

      <hr className="settings-divider" />

      <h3 className="settings-card__subtitle">Restore from a backup</h3>
      <p className="muted text-sm">
        Restoring <strong>replaces all current data</strong> with the backup. Pick a backup file to review
        it first; the current state is saved as a safety backup before anything is replaced.
      </p>

      <input
        ref={fileInputRef}
        type="file"
        accept=".zip"
        hidden
        data-testid="restore-file-input"
        onChange={(e) => {
          const picked = e.target.files?.[0]
          if (picked) void handleFilePicked(picked)
        }}
      />

      <div className="settings-actions">
        <button
          type="button"
          className="btn btn--ghost"
          onClick={() => fileInputRef.current?.click()}
          disabled={inspecting || restoring}
        >
          {inspecting ? 'Reading backup…' : file ? 'Choose a different backup…' : 'Choose backup file…'}
        </button>
      </div>

      {inspection && (
        <div className="backup-summary" data-testid="inspect-summary">
          <span className={COMPAT_CHIP[inspection.compatibility]}>{COMPAT_LABEL[inspection.compatibility]}</span>
          <ul className="muted text-sm">
            <li>Taken: {new Date(inspection.createdAtUtc).toLocaleString()}</li>
            <li>Offers: {inspection.tableCounts.offers ?? 0}</li>
            <li>CV files: {inspection.cvFileCount}</li>
            <li>Source version: {inspection.migrationTip}</li>
          </ul>
          {inspection.warnings.length > 0 && (
            <ul className="settings-msg settings-msg--error text-sm">
              {inspection.warnings.map((w) => (
                <li key={w}>{w}</li>
              ))}
            </ul>
          )}
        </div>
      )}

      {restoreMsg?.kind === 'error' && (
        <div className="settings-msg settings-msg--error" role="alert">
          {restoreMsg.text}
        </div>
      )}
      {restoreMsg?.kind === 'ok' && (
        <div className="settings-msg settings-msg--ok" role="status">
          {restoreMsg.text}
        </div>
      )}

      {file && (
        <div className="settings-actions">
          <button
            type="button"
            className="btn btn--danger"
            onClick={() => setConfirming(true)}
            disabled={!canRestore || restoring}
          >
            Restore
          </button>
        </div>
      )}

      {confirming && file && (
        <RestoreConfirmModal
          fileName={file.name}
          busy={restoring}
          onConfirm={handleRestore}
          onClose={() => setConfirming(false)}
        />
      )}
    </section>
  )
}
