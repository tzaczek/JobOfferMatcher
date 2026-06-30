import { useEffect, useId, useRef } from 'react'
import { createPortal } from 'react-dom'
import '../../components/ApplyModal/ApplyModal.css'

interface RestoreConfirmModalProps {
  /** The name of the backup file about to be restored, shown for context. */
  fileName: string
  onConfirm: () => void | Promise<void>
  onClose: () => void
  /** Disables the actions while the restore request is in flight. */
  busy?: boolean
}

/**
 * Destructive-confirm dialog for a restore (003 US2). Built on the same portal + focus-trap pattern as
 * ApplyModal; the confirm action uses the `.btn--danger` variant because it replaces all live data.
 */
export function RestoreConfirmModal({ fileName, onConfirm, onClose, busy = false }: RestoreConfirmModalProps) {
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const cancelRef = useRef<HTMLButtonElement>(null)

  // Focus Cancel on open (the safe default for a destructive dialog); restore focus on close.
  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    cancelRef.current?.focus()
    return () => previouslyFocused?.focus?.()
  }, [])

  // Escape closes (unless busy); Tab is trapped within the dialog.
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        if (busy || e.defaultPrevented) return
        onClose()
        return
      }
      if (e.key !== 'Tab' || !dialogRef.current) return
      const focusable = dialogRef.current.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], input:not([disabled]), [tabindex]:not([tabindex="-1"])',
      )
      if (focusable.length === 0) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault()
        last.focus()
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose, busy])

  async function handleConfirm() {
    try {
      await onConfirm()
    } catch {
      // Parent surfaces the error and keeps the modal open for retry.
    }
  }

  return createPortal(
    <div className="modal-overlay" onMouseDown={() => { if (!busy) onClose() }}>
      <div
        className="modal"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        ref={dialogRef}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h2 className="modal__title" id={titleId}>
          Replace all current data?
        </h2>
        <p className="modal__subtitle muted text-sm">
          Restoring <strong>{fileName}</strong> will replace everything currently in the app — all offers,
          settings, and CV files — with the contents of this backup. A safety backup of the current state
          is saved first, but this cannot be undone from the app.
        </p>

        <div className="modal__actions">
          <button type="button" className="btn btn--ghost btn--sm" onClick={onClose} disabled={busy} ref={cancelRef}>
            Cancel
          </button>
          <button type="button" className="btn btn--danger btn--sm" onClick={handleConfirm} disabled={busy}>
            {busy ? 'Restoring…' : 'Restore'}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
