import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router-dom'
import type {
  ApplicationDetailDto,
  ApplicationOutcome,
  CommunicationDirection,
  PipelineStageDto,
} from '../../api/types.ts'
import {
  addCommunication,
  addInterview,
  addNote,
  addTask,
  closeApplication,
  deleteApplication,
  deleteInterview,
  deleteTask,
  downloadDocument,
  deleteDocument,
  getApplication,
  moveStage,
  reopenApplication,
  updateInterview,
  updateTask,
  uploadDocument,
} from '../../api/applications.ts'
import { ApiError } from '../../api/client.ts'
import './ApplicationDrawer.css'

const OUTCOMES: { value: ApplicationOutcome; label: string }[] = [
  { value: 'rejected', label: 'Rejected' },
  { value: 'accepted', label: 'Accepted' },
  { value: 'withdrawn', label: 'Withdrawn' },
  { value: 'noResponse', label: 'No response' },
]

const OUTCOME_LABEL: Record<ApplicationOutcome, string> = {
  rejected: 'Rejected',
  accepted: 'Accepted',
  withdrawn: 'Withdrawn',
  noResponse: 'No response',
}

type Tab = 'timeline' | 'notes' | 'tasks' | 'documents' | 'interviews' | 'contact'
const TABS: { id: Tab; label: string }[] = [
  { id: 'timeline', label: 'Timeline' },
  { id: 'notes', label: 'Notes' },
  { id: 'tasks', label: 'Tasks' },
  { id: 'documents', label: 'Documents' },
  { id: 'interviews', label: 'Interviews' },
  { id: 'contact', label: 'Contact' },
]

interface Props {
  offerId: string
  stages: PipelineStageDto[]
  onClose: () => void
  onChanged: () => void
}

export function ApplicationDrawer({ offerId, stages, onClose, onChanged }: Props) {
  const [detail, setDetail] = useState<ApplicationDetailDto | null>(null)
  const [tab, setTab] = useState<Tab>('timeline')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const navigate = useNavigate()

  function handleViewOffer() {
    onClose()
    navigate(`/?offerId=${offerId}`)
  }

  const refresh = useCallback(async () => {
    setDetail(await getApplication(offerId))
  }, [offerId])

  useEffect(() => {
    let active = true
    getApplication(offerId)
      .then((d) => active && setDetail(d))
      .catch(
        (e) =>
          active && setError(e instanceof ApiError ? e.message : 'Failed to load the application.'),
      )
    return () => {
      active = false
    }
  }, [offerId])

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    return () => previouslyFocused?.focus?.()
  }, [])

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape' && !busy && !e.defaultPrevented) onClose()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose, busy])

  /** Run a mutation with busy/error handling, then refresh the drawer + notify the board. */
  const run = useCallback(
    async (fn: () => Promise<unknown>) => {
      setBusy(true)
      setError(null)
      try {
        await fn()
        await refresh()
        onChanged()
      } catch (e) {
        setError(e instanceof ApiError ? e.message : 'Something went wrong.')
      } finally {
        setBusy(false)
      }
    },
    [refresh, onChanged],
  )

  const counts: Record<Tab, number> = detail
    ? {
        timeline: detail.timeline.length,
        notes: detail.notes.length,
        tasks: detail.tasks.length,
        documents: detail.documents.length,
        interviews: detail.interviews.length,
        contact: detail.communications.length,
      }
    : { timeline: 0, notes: 0, tasks: 0, documents: 0, interviews: 0, contact: 0 }

  return createPortal(
    <div
      className="modal-overlay"
      onMouseDown={() => {
        if (!busy) onClose()
      }}
    >
      <div
        className="modal app-drawer"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        ref={dialogRef}
        onMouseDown={(e) => e.stopPropagation()}
      >
        {detail === null ? (
          <div className="app-drawer__loading">
            <p className="muted">{error ?? 'Loading…'}</p>
            <button type="button" className="btn btn--ghost btn--sm" onClick={onClose}>
              Close
            </button>
          </div>
        ) : (
          <>
            <header className="app-drawer__head">
              <div className="app-drawer__head-titles">
                <h2 className="app-drawer__title" id={titleId}>
                  {detail.title}
                </h2>
                <p className="app-drawer__company">{detail.company}</p>
              </div>
              <div className="app-drawer__head-actions">
                <button
                  type="button"
                  className="btn btn--ghost btn--sm"
                  onClick={handleViewOffer}
                  disabled={busy}
                >
                  View offer
                </button>
                <button
                  type="button"
                  className="app-drawer__icon-btn"
                  onClick={onClose}
                  aria-label="Close drawer"
                  disabled={busy}
                >
                  ✕
                </button>
              </div>
            </header>

            <div className="app-drawer__lifecycle">
              <label className="app-drawer__field">
                <span className="app-drawer__field-label">Stage</span>
                <select
                  value={detail.stageId}
                  disabled={busy || detail.status === 'closed'}
                  onChange={(e) => void run(() => moveStage(offerId, e.target.value))}
                  data-testid="stage-select"
                >
                  {stages.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </select>
              </label>

              {detail.status === 'active' ? (
                <CloseControl
                  busy={busy}
                  onClose={(outcome) => void run(() => closeApplication(offerId, outcome))}
                />
              ) : (
                <div className="app-drawer__field app-drawer__field--end">
                  <span className="app-drawer__field-label">Outcome</span>
                  <div className="app-drawer__closed">
                    <span className="chip chip--dismissed" data-testid="closed-outcome">
                      {detail.outcome ? OUTCOME_LABEL[detail.outcome] : 'Closed'}
                    </span>
                    <button
                      type="button"
                      className="btn btn--ghost btn--sm"
                      disabled={busy}
                      onClick={() => void run(() => reopenApplication(offerId))}
                    >
                      Reopen
                    </button>
                  </div>
                </div>
              )}
            </div>

            {error && (
              <div className="app-drawer__error" role="alert">
                {error}
              </div>
            )}

            <div className="app-drawer__tabs" role="tablist">
              {TABS.map((t) => (
                <button
                  key={t.id}
                  type="button"
                  role="tab"
                  aria-selected={tab === t.id}
                  className={
                    tab === t.id ? 'app-drawer__tab app-drawer__tab--active' : 'app-drawer__tab'
                  }
                  onClick={() => setTab(t.id)}
                >
                  {t.label}
                  {counts[t.id] > 0 && (
                    <span className="app-drawer__tab-count">{counts[t.id]}</span>
                  )}
                </button>
              ))}
            </div>

            <div className="app-drawer__panel">
              {tab === 'timeline' && <TimelinePanel detail={detail} />}
              {tab === 'notes' && (
                <NotesPanel
                  detail={detail}
                  busy={busy}
                  onAdd={(body) => run(() => addNote(offerId, body))}
                />
              )}
              {tab === 'tasks' && (
                <TasksPanel
                  detail={detail}
                  busy={busy}
                  onAdd={(t) => run(() => addTask(offerId, t))}
                  onToggle={(id, completed) => run(() => updateTask(offerId, id, { completed }))}
                  onDelete={(id) => run(() => deleteTask(offerId, id))}
                />
              )}
              {tab === 'documents' && (
                <DocumentsPanel
                  detail={detail}
                  busy={busy}
                  onUpload={(file) => run(() => uploadDocument(offerId, file))}
                  onDelete={(id) => run(() => deleteDocument(offerId, id))}
                  onDownload={(id, name) => downloadDocument(offerId, id, name)}
                />
              )}
              {tab === 'interviews' && (
                <InterviewsPanel
                  detail={detail}
                  busy={busy}
                  onAdd={(i) => run(() => addInterview(offerId, i))}
                  onOutcome={(id, outcome) => run(() => updateInterview(offerId, id, { outcome }))}
                  onDelete={(id) => run(() => deleteInterview(offerId, id))}
                />
              )}
              {tab === 'contact' && (
                <ContactPanel
                  detail={detail}
                  busy={busy}
                  onAdd={(c) => run(() => addCommunication(offerId, c))}
                />
              )}
            </div>

            <footer className="app-drawer__foot">
              <span className="app-drawer__foot-hint muted text-sm">
                Deleting is permanent — recoverable only from a backup.
              </span>
              <button
                type="button"
                className="btn btn--danger btn--sm"
                disabled={busy}
                onClick={() => {
                  if (
                    window.confirm(
                      'Permanently delete this application and all its history? This cannot be undone (only a backup can restore it).',
                    )
                  ) {
                    void run(() => deleteApplication(offerId)).then(onClose)
                  }
                }}
              >
                Delete application
              </button>
            </footer>
          </>
        )}
      </div>
    </div>,
    document.body,
  )
}

function CloseControl({
  busy,
  onClose,
}: {
  busy: boolean
  onClose: (outcome: ApplicationOutcome) => void
}) {
  const [outcome, setOutcome] = useState<ApplicationOutcome>('rejected')
  return (
    <div className="app-drawer__field app-drawer__field--end">
      <span className="app-drawer__field-label">Close as</span>
      <div className="app-drawer__close-control">
        <select
          value={outcome}
          disabled={busy}
          onChange={(e) => setOutcome(e.target.value as ApplicationOutcome)}
          data-testid="close-outcome"
        >
          {OUTCOMES.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        <button
          type="button"
          className="btn btn--primary btn--sm"
          disabled={busy}
          onClick={() => onClose(outcome)}
        >
          Close
        </button>
      </div>
    </div>
  )
}

function EmptyState({ children }: { children: ReactNode }) {
  return <p className="app-drawer__empty muted text-sm">{children}</p>
}

function TimelinePanel({ detail }: { detail: ApplicationDetailDto }) {
  if (detail.timeline.length === 0) return <EmptyState>Nothing has happened yet.</EmptyState>
  return (
    <ol className="app-drawer__timeline" data-testid="timeline">
      {detail.timeline.map((e, i) => (
        <li key={i} className="app-drawer__timeline-item">
          <span className="app-drawer__timeline-dot" aria-hidden="true" />
          <div className="app-drawer__timeline-body">
            <span className="app-drawer__timeline-when muted text-sm">
              {new Date(e.occurredAt).toLocaleString()}
            </span>
            <span className="app-drawer__timeline-title">{e.title}</span>
            {e.detail && (
              <span className="app-drawer__timeline-detail muted text-sm">{e.detail}</span>
            )}
          </div>
        </li>
      ))}
    </ol>
  )
}

function NotesPanel({
  detail,
  busy,
  onAdd,
}: {
  detail: ApplicationDetailDto
  busy: boolean
  onAdd: (body: string) => void
}) {
  const [body, setBody] = useState('')
  return (
    <div className="app-drawer__section">
      <form
        className="app-drawer__form"
        onSubmit={(e) => {
          e.preventDefault()
          if (body.trim()) {
            onAdd(body.trim())
            setBody('')
          }
        }}
      >
        <textarea
          value={body}
          onChange={(e) => setBody(e.target.value)}
          rows={2}
          maxLength={4000}
          placeholder="Add a note…"
          disabled={busy}
        />
        <div className="app-drawer__form-actions">
          <button
            type="submit"
            className="btn btn--primary btn--sm"
            disabled={busy || !body.trim()}
          >
            Add note
          </button>
        </div>
      </form>
      {detail.notes.length === 0 ? (
        <EmptyState>No notes yet.</EmptyState>
      ) : (
        <ul className="app-drawer__list" data-testid="notes-list">
          {detail.notes.map((n) => (
            <li key={n.id} className="app-drawer__item app-drawer__note">
              <span className="muted text-sm">{new Date(n.createdAt).toLocaleString()}</span>
              <span>{n.body}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function TasksPanel({
  detail,
  busy,
  onAdd,
  onToggle,
  onDelete,
}: {
  detail: ApplicationDetailDto
  busy: boolean
  onAdd: (t: { title: string; description?: string | null; dueAt?: string | null }) => void
  onToggle: (id: string, completed: boolean) => void
  onDelete: (id: string) => void
}) {
  const [title, setTitle] = useState('')
  const [dueAt, setDueAt] = useState('')
  return (
    <div className="app-drawer__section">
      <form
        className="app-drawer__form"
        onSubmit={(e) => {
          e.preventDefault()
          if (title.trim()) {
            onAdd({ title: title.trim(), dueAt: dueAt ? new Date(dueAt).toISOString() : null })
            setTitle('')
            setDueAt('')
          }
        }}
      >
        <div className="app-drawer__inputs">
          <input
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="Task title"
            maxLength={300}
            disabled={busy}
          />
          <input
            type="date"
            value={dueAt}
            onChange={(e) => setDueAt(e.target.value)}
            disabled={busy}
            aria-label="Due date"
          />
        </div>
        <div className="app-drawer__form-actions">
          <button
            type="submit"
            className="btn btn--primary btn--sm"
            disabled={busy || !title.trim()}
          >
            Add task
          </button>
        </div>
      </form>
      {detail.tasks.length === 0 ? (
        <EmptyState>No tasks yet.</EmptyState>
      ) : (
        <ul className="app-drawer__list" data-testid="tasks-list">
          {detail.tasks.map((t) => (
            <li
              key={t.id}
              className={
                t.overdue
                  ? 'app-drawer__item app-drawer__task app-drawer__task--overdue'
                  : 'app-drawer__item app-drawer__task'
              }
            >
              <label className="app-drawer__task-main">
                <input
                  type="checkbox"
                  checked={t.completedAt != null}
                  disabled={busy}
                  onChange={(e) => onToggle(t.id, e.target.checked)}
                />
                <span className={t.completedAt ? 'app-drawer__task-done' : undefined}>
                  {t.title}
                </span>
              </label>
              <span className="app-drawer__task-meta">
                {t.dueAt && (
                  <span className="muted text-sm">
                    Due {new Date(t.dueAt).toLocaleDateString()}
                  </span>
                )}
                {t.overdue && (
                  <span className="chip chip--failed" data-testid="task-overdue">
                    Overdue
                  </span>
                )}
                <button
                  type="button"
                  className="app-drawer__icon-btn app-drawer__icon-btn--danger"
                  disabled={busy}
                  onClick={() => onDelete(t.id)}
                  aria-label="Delete task"
                >
                  ✕
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function DocumentsPanel({
  detail,
  busy,
  onUpload,
  onDelete,
  onDownload,
}: {
  detail: ApplicationDetailDto
  busy: boolean
  onUpload: (file: File) => void
  onDelete: (id: string) => void
  onDownload: (id: string, name: string) => Promise<unknown>
}) {
  return (
    <div className="app-drawer__section">
      <div className="app-drawer__form-actions app-drawer__form-actions--start">
        <label className="btn btn--ghost btn--sm app-drawer__upload">
          Attach a file…
          <input
            type="file"
            hidden
            disabled={busy}
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file) onUpload(file)
              e.target.value = ''
            }}
            data-testid="document-input"
          />
        </label>
      </div>
      {detail.documents.length === 0 ? (
        <EmptyState>No documents attached.</EmptyState>
      ) : (
        <ul className="app-drawer__list" data-testid="documents-list">
          {detail.documents.map((d) => (
            <li key={d.id} className="app-drawer__item app-drawer__doc">
              <div className="app-drawer__doc-info">
                <span className="app-drawer__doc-name">{d.originalFileName}</span>
                <span className="muted text-sm">{formatBytes(d.sizeBytes)}</span>
              </div>
              <span className="app-drawer__doc-actions">
                <button
                  type="button"
                  className="btn btn--ghost btn--sm"
                  disabled={busy}
                  onClick={() => void onDownload(d.id, d.originalFileName)}
                >
                  Download
                </button>
                <button
                  type="button"
                  className="app-drawer__icon-btn app-drawer__icon-btn--danger"
                  disabled={busy}
                  onClick={() => onDelete(d.id)}
                  aria-label="Delete document"
                >
                  ✕
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function InterviewsPanel({
  detail,
  busy,
  onAdd,
  onOutcome,
  onDelete,
}: {
  detail: ApplicationDetailDto
  busy: boolean
  onAdd: (i: {
    kind: string
    scheduledAt?: string | null
    interviewer?: string | null
    notes?: string | null
  }) => void
  onOutcome: (id: string, outcome: string) => void
  onDelete: (id: string) => void
}) {
  const [kind, setKind] = useState('')
  const [scheduledAt, setScheduledAt] = useState('')
  const [interviewer, setInterviewer] = useState('')
  return (
    <div className="app-drawer__section">
      <form
        className="app-drawer__form"
        onSubmit={(e) => {
          e.preventDefault()
          if (kind.trim()) {
            onAdd({
              kind: kind.trim(),
              scheduledAt: scheduledAt ? new Date(scheduledAt).toISOString() : null,
              interviewer: interviewer.trim() || null,
            })
            setKind('')
            setScheduledAt('')
            setInterviewer('')
          }
        }}
      >
        <div className="app-drawer__inputs">
          <input
            value={kind}
            onChange={(e) => setKind(e.target.value)}
            placeholder="Kind (e.g. phone screen)"
            maxLength={80}
            disabled={busy}
          />
          <input
            type="datetime-local"
            value={scheduledAt}
            onChange={(e) => setScheduledAt(e.target.value)}
            disabled={busy}
            aria-label="Scheduled at"
          />
          <input
            value={interviewer}
            onChange={(e) => setInterviewer(e.target.value)}
            placeholder="Interviewer"
            maxLength={200}
            disabled={busy}
          />
        </div>
        <div className="app-drawer__form-actions">
          <button
            type="submit"
            className="btn btn--primary btn--sm"
            disabled={busy || !kind.trim()}
          >
            Add interview
          </button>
        </div>
      </form>
      {detail.interviews.length === 0 ? (
        <EmptyState>No interviews recorded.</EmptyState>
      ) : (
        <ul className="app-drawer__list" data-testid="interviews-list">
          {detail.interviews.map((i) => (
            <li key={i.id} className="app-drawer__item app-drawer__interview">
              <div className="app-drawer__interview-head">
                <span className="app-drawer__interview-kind">{i.kind}</span>
                {i.upcoming && (
                  <span className="chip chip--updated" data-testid="interview-upcoming">
                    Upcoming
                  </span>
                )}
                <button
                  type="button"
                  className="app-drawer__icon-btn app-drawer__icon-btn--danger"
                  disabled={busy}
                  onClick={() => onDelete(i.id)}
                  aria-label="Delete interview"
                >
                  ✕
                </button>
              </div>
              <div className="app-drawer__interview-meta">
                {i.scheduledAt && (
                  <span className="muted text-sm">{new Date(i.scheduledAt).toLocaleString()}</span>
                )}
                {i.interviewer && <span className="muted text-sm">with {i.interviewer}</span>}
              </div>
              {i.outcome ? (
                <span className="app-drawer__interview-outcome">Outcome: {i.outcome}</span>
              ) : (
                <OutcomeForm busy={busy} onSave={(o) => onOutcome(i.id, o)} />
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function OutcomeForm({ busy, onSave }: { busy: boolean; onSave: (outcome: string) => void }) {
  const [value, setValue] = useState('')
  return (
    <form
      className="app-drawer__outcome-form"
      onSubmit={(e) => {
        e.preventDefault()
        if (value.trim()) {
          onSave(value.trim())
          setValue('')
        }
      }}
    >
      <input
        value={value}
        onChange={(e) => setValue(e.target.value)}
        placeholder="Record outcome…"
        disabled={busy}
      />
      <button type="submit" className="btn btn--ghost btn--sm" disabled={busy || !value.trim()}>
        Save
      </button>
    </form>
  )
}

function ContactPanel({
  detail,
  busy,
  onAdd,
}: {
  detail: ApplicationDetailDto
  busy: boolean
  onAdd: (c: {
    occurredAt: string
    direction: CommunicationDirection
    channel: string
    summary: string
  }) => void
}) {
  const [occurredAt, setOccurredAt] = useState('')
  const [direction, setDirection] = useState<CommunicationDirection>('inbound')
  const [channel, setChannel] = useState('')
  const [summary, setSummary] = useState('')
  return (
    <div className="app-drawer__section">
      <form
        className="app-drawer__form"
        onSubmit={(e) => {
          e.preventDefault()
          if (channel.trim() && summary.trim()) {
            onAdd({
              occurredAt: occurredAt
                ? new Date(occurredAt).toISOString()
                : new Date().toISOString(),
              direction,
              channel: channel.trim(),
              summary: summary.trim(),
            })
            setOccurredAt('')
            setChannel('')
            setSummary('')
          }
        }}
      >
        <div className="app-drawer__inputs">
          <input
            type="datetime-local"
            value={occurredAt}
            onChange={(e) => setOccurredAt(e.target.value)}
            disabled={busy}
            aria-label="Occurred at"
          />
          <select
            value={direction}
            onChange={(e) => setDirection(e.target.value as CommunicationDirection)}
            disabled={busy}
            aria-label="Direction"
          >
            <option value="inbound">Inbound</option>
            <option value="outbound">Outbound</option>
          </select>
          <input
            value={channel}
            onChange={(e) => setChannel(e.target.value)}
            placeholder="Channel (email, phone…)"
            maxLength={80}
            disabled={busy}
          />
        </div>
        <textarea
          value={summary}
          onChange={(e) => setSummary(e.target.value)}
          rows={2}
          placeholder="Summary"
          disabled={busy}
        />
        <div className="app-drawer__form-actions">
          <button
            type="submit"
            className="btn btn--primary btn--sm"
            disabled={busy || !channel.trim() || !summary.trim()}
          >
            Log communication
          </button>
        </div>
      </form>
      {detail.communications.length === 0 ? (
        <EmptyState>No communications logged.</EmptyState>
      ) : (
        <ul className="app-drawer__list" data-testid="communications-list">
          {detail.communications.map((c) => (
            <li key={c.id} className="app-drawer__item app-drawer__comm">
              <span className="app-drawer__comm-head">
                <span className="chip chip--skill">{c.direction}</span>
                <span className="app-drawer__comm-channel">{c.channel}</span>
                <span className="muted text-sm">{new Date(c.occurredAt).toLocaleString()}</span>
              </span>
              <span>{c.summary}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
