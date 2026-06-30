import { useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { ApplicationInput } from '../../api/types.ts'
import './ApplyModal.css'

interface ApplyModalProps {
  /** Offer title, shown in the header for context. */
  offerTitle: string
  /** True when the offer is already applied (editing) vs marking it fresh — drives the title + date default. */
  isEditing: boolean
  /** Existing date (ISO-8601) when editing an already-applied offer; null when marking fresh. */
  initialAppliedAt?: string | null
  /** Existing note when editing. */
  initialNote?: string | null
  onSave: (input: ApplicationInput) => void | Promise<void>
  onClose: () => void
  /** Disables the form while the save request is in flight. */
  saving?: boolean
}

/** Today's local date as a `YYYY-MM-DD` string for the date input's default. */
function todayInputValue(): string {
  const now = new Date()
  const offsetMs = now.getTimezoneOffset() * 60_000
  return new Date(now.getTime() - offsetMs).toISOString().slice(0, 10)
}

/** ISO-8601 → `YYYY-MM-DD` (date portion) for the date input. */
function toInputDate(iso: string | null | undefined): string {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : d.toISOString().slice(0, 10)
}

export function ApplyModal({
  offerTitle,
  isEditing,
  initialAppliedAt,
  initialNote,
  onSave,
  onClose,
  saving = false,
}: ApplyModalProps) {
  // Fresh "mark applied" defaults the date to today; editing preserves the stored date — including a
  // deliberately date-less application (empty field), so saving a note change never fabricates a date.
  const [date, setDate] = useState(() => toInputDate(initialAppliedAt) || (isEditing ? '' : todayInputValue()))
  const [note, setNote] = useState(initialNote ?? '')
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const dateRef = useRef<HTMLInputElement>(null)

  // Focus the first field on open; restore focus to the trigger on close.
  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    dateRef.current?.focus()
    return () => previouslyFocused?.focus?.()
  }, [])

  // Escape closes (not while saving, and not when a focused control — e.g. the date picker's native
  // popup — should handle it first); Tab is trapped within the dialog (basic two-end wrap). Bubble
  // phase so the date input can consume its own Escape before it reaches us.
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        if (saving || e.defaultPrevented) return
        onClose()
        return
      }
      if (e.key !== 'Tab' || !dialogRef.current) return
      const focusable = dialogRef.current.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], input:not([disabled]), textarea:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])',
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
  }, [onClose, saving])

  async function handleSave() {
    // An empty date field means "applied, date not recorded" (the date is optional). Await so a
    // rejected parent save doesn't surface as an unhandled rejection (the parent shows the error).
    try {
      await onSave({ appliedAt: date || null, note: note.trim() || null })
    } catch {
      // Parent surfaces the error and keeps the modal open for retry.
    }
  }

  return createPortal(
    <div className="modal-overlay" onMouseDown={() => { if (!saving) onClose() }}>
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        ref={dialogRef}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h2 className="modal__title" id={titleId}>
          {isEditing ? 'Edit application' : 'Mark as applied'}
        </h2>
        <p className="modal__subtitle muted text-sm">{offerTitle}</p>

        <label className="modal__field" htmlFor={`${titleId}-date`}>
          Date applied <span className="muted">(optional)</span>
          <input
            id={`${titleId}-date`}
            ref={dateRef}
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            disabled={saving}
          />
        </label>

        <label className="modal__field" htmlFor={`${titleId}-note`}>
          Note <span className="muted">(optional)</span>
          <textarea
            id={`${titleId}-note`}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            rows={3}
            maxLength={2000}
            placeholder="e.g. referred by Anna, applied via company site…"
            disabled={saving}
          />
        </label>

        <div className="modal__actions">
          <button type="button" className="btn btn--ghost btn--sm" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button type="button" className="btn btn--primary btn--sm" onClick={handleSave} disabled={saving}>
            {saving ? 'Saving…' : 'Save'}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
