import { useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router-dom'
import { ApiError } from '../../api/client.ts'
import {
  deleteTailored,
  downloadTailoredPdf,
  generate,
  getDraft,
  getTailored,
  previewUrl,
} from '../../api/tailoredCv.ts'
import type { TailoredCvDraftDto, TailoredCvDto } from '../../api/types.ts'
import { poll } from '../../lib/polling.ts'
import { enrichmentStatusClass } from '../../theme/index.ts'
import '../ApplyModal/ApplyModal.css'
import './TailorCvModal.css'

interface TailorCvModalProps {
  offerId: string
  offerTitle: string
  onClose: () => void
  /** Called after a successful generate/regenerate/delete so the opener can refresh its tailored-CV state. */
  onChanged?: () => void
}

/**
 * Generate / view / iterate a per-offer tailored CV (US1–US3). Clones the ApplyModal portal + focus-trap.
 * The full prompt is visible + editable; toggling an emphasised-skill chip recomposes the default prompt
 * (until the user edits it, FR-004); Generate enqueues the request (the local /tailor-cv worker produces
 * it); a produced CV is shown inline (preview iframe) and downloadable as a PDF.
 */
export function TailorCvModal({ offerId, offerTitle, onClose, onChanged }: TailorCvModalProps) {
  const [draft, setDraft] = useState<TailoredCvDraftDto | null>(null)
  const [noCv, setNoCv] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [prompt, setPrompt] = useState('')
  const [selected, setSelected] = useState<string[]>([])
  const [promptEdited, setPromptEdited] = useState(false)

  const [tailored, setTailored] = useState<TailoredCvDto | null>(null)
  const [generating, setGenerating] = useState(false)
  const [genError, setGenError] = useState<string | null>(null)
  const [downloadBusy, setDownloadBusy] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)

  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const navigate = useNavigate()

  function handleViewOffer() {
    onClose()
    navigate(`/?offerId=${offerId}`)
  }

  // Load the prefilled draft + any existing tailored CV on open.
  useEffect(() => {
    const controller = new AbortController()
    ;(async () => {
      try {
        const d = await getDraft(offerId, undefined, controller.signal)
        setDraft(d)
        setPrompt(d.prompt)
        setSelected(d.emphasisedSkills)
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        if (e instanceof ApiError && e.code === 'NoCvOnFile') {
          setNoCv(true)
          return
        }
        setLoadError(e instanceof ApiError ? e.message : 'Failed to load the tailoring draft.')
        return
      }
      try {
        setTailored(await getTailored(offerId, controller.signal))
      } catch {
        // 404 (TailoredCvNotFound) ⇒ no existing tailored CV yet; that's fine.
      }
    })()
    return () => controller.abort()
  }, [offerId])

  // While pending, poll until the worker produces (or fails) it — so an open modal updates live.
  useEffect(() => {
    if (tailored?.state !== 'pending') return
    const controller = new AbortController()
    poll<TailoredCvDto>({
      fetch: (signal) => getTailored(offerId, signal),
      done: (t) => t.state !== 'pending',
      onTick: setTailored,
      intervalMs: 2500,
      signal: controller.signal,
    }).catch(() => {
      // Aborted on close / transient error — the indicator simply stops updating.
    })
    return () => controller.abort()
  }, [tailored?.state, offerId])

  // Escape closes; Tab is trapped within the dialog (mirrors ApplyModal).
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        if (generating || e.defaultPrevented) return
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
  }, [onClose, generating])

  async function toggleSkill(skill: string) {
    const next = selected.includes(skill)
      ? selected.filter((s) => s !== skill)
      : [...selected, skill]
    setSelected(next)
    // While the prompt is still the unedited default, a toggle recomposes the visible prompt (FR-004).
    if (!promptEdited) {
      try {
        const d = await getDraft(offerId, { skills: next })
        setPrompt(d.prompt)
      } catch {
        // Non-fatal: the selection still updates; the prompt just isn't recomposed.
      }
    }
  }

  async function handleGenerate() {
    setGenerating(true)
    setGenError(null)
    try {
      const result = await generate(offerId, { prompt, emphasisedSkills: selected })
      setTailored(result)
      onChanged?.()
    } catch (e) {
      setGenError(e instanceof ApiError ? e.message : 'Failed to start generation.')
    } finally {
      setGenerating(false)
    }
  }

  async function handleDownload() {
    setDownloadBusy(true)
    setDownloadError(null)
    try {
      await downloadTailoredPdf(offerId)
    } catch (e) {
      setDownloadError(e instanceof ApiError ? e.message : 'Download failed.')
    } finally {
      setDownloadBusy(false)
    }
  }

  async function handleRemove() {
    try {
      await deleteTailored(offerId)
      onChanged?.()
      onClose()
    } catch (e) {
      setGenError(e instanceof ApiError ? e.message : 'Failed to remove the tailored CV.')
    }
  }

  const hasExisting = tailored !== null
  const isProduced = tailored?.state === 'produced'

  return createPortal(
    <div
      className="modal-overlay"
      onMouseDown={() => {
        if (!generating) onClose()
      }}
    >
      <div
        className="modal tailor-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        ref={dialogRef}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h2 className="modal__title" id={titleId}>
          Tailor CV
        </h2>
        <div className="tailor-modal__subtitle-row">
          <p className="modal__subtitle muted text-sm">{offerTitle}</p>
          <button type="button" className="btn btn--ghost btn--sm" onClick={handleViewOffer}>
            View offer
          </button>
        </div>

        {noCv ? (
          <div className="tailor-modal__empty">
            <p className="cv-msg cv-msg--warn">
              Add a CV first — there is nothing to tailor from. Upload one on{' '}
              <strong>CV &amp; Profile</strong>.
            </p>
            <div className="modal__actions">
              <button type="button" className="btn btn--ghost btn--sm" onClick={onClose}>
                Close
              </button>
            </div>
          </div>
        ) : loadError ? (
          <div className="tailor-modal__empty">
            <p className="cv-msg cv-msg--error" role="alert">
              {loadError}
            </p>
            <div className="modal__actions">
              <button type="button" className="btn btn--ghost btn--sm" onClick={onClose}>
                Close
              </button>
            </div>
          </div>
        ) : draft ? (
          <>
            <p className="tailor-modal__source text-sm">
              Source CV: <strong>{draft.sourceCv?.fileName}</strong>{' '}
              <span className="muted">(read-only)</span>
            </p>

            <div className="modal__field">
              <span>Emphasise these skills</span>
              <div className="tailor-modal__chips" data-testid="tailor-skills">
                {draft.allOfferSkills.map((skill) => {
                  const on = selected.includes(skill)
                  return (
                    <button
                      key={skill}
                      type="button"
                      aria-pressed={on}
                      className={
                        on ? 'chip chip--skill tailor-chip tailor-chip--on' : 'chip tailor-chip'
                      }
                      onClick={() => toggleSkill(skill)}
                    >
                      {skill}
                    </button>
                  )
                })}
              </div>
            </div>

            <label className="modal__field" htmlFor={`${titleId}-prompt`}>
              Prompt <span className="muted">(editable — this exact text is used)</span>
              <textarea
                id={`${titleId}-prompt`}
                className="tailor-modal__prompt"
                value={prompt}
                onChange={(e) => {
                  setPrompt(e.target.value)
                  setPromptEdited(true)
                }}
                rows={8}
                disabled={generating}
                data-testid="tailor-prompt"
              />
            </label>

            {tailored && (
              <div className="tailor-modal__status" data-testid="tailor-status">
                <span className={enrichmentStatusClass(tailored.state)}>
                  {tailored.state === 'produced'
                    ? 'Produced'
                    : tailored.state === 'failed'
                      ? 'Generation failed'
                      : 'Pending — run /tailor-cv in Claude Code'}
                </span>
                {tailored.state === 'failed' && tailored.lastError && (
                  <span className="muted text-sm">{tailored.lastError}</span>
                )}
              </div>
            )}

            {isProduced && (
              <iframe
                className="tailor-modal__preview"
                title="Tailored CV preview"
                src={previewUrl(offerId, tailored?.generationVersion)}
                data-testid="tailor-preview"
              />
            )}

            {genError && (
              <p className="cv-msg cv-msg--error" role="alert">
                {genError}
              </p>
            )}
            {downloadError && (
              <p className="cv-msg cv-msg--error" role="alert">
                {downloadError}
              </p>
            )}

            <div className="modal__actions">
              {hasExisting && (
                <button
                  type="button"
                  className="btn btn--ghost btn--sm tailor-modal__remove"
                  onClick={handleRemove}
                  disabled={generating}
                >
                  Remove
                </button>
              )}
              <button
                type="button"
                className="btn btn--ghost btn--sm"
                onClick={onClose}
                disabled={generating}
              >
                Close
              </button>
              {isProduced && (
                <button
                  type="button"
                  className="btn btn--ghost btn--sm"
                  onClick={handleDownload}
                  disabled={!tailored?.hasPdf || downloadBusy}
                >
                  {downloadBusy ? 'Downloading…' : 'Download PDF'}
                </button>
              )}
              <button
                type="button"
                className="btn btn--primary btn--sm"
                onClick={handleGenerate}
                disabled={generating || !prompt.trim()}
              >
                {generating ? 'Starting…' : hasExisting ? 'Regenerate' : 'Generate'}
              </button>
            </div>
          </>
        ) : (
          <div className="state-block">
            <span className="spinner" aria-label="Loading" /> Loading…
          </div>
        )}
      </div>
    </div>,
    document.body,
  )
}
