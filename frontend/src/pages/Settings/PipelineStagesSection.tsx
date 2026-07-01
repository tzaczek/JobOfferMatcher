import { useEffect, useState } from 'react'
import type { PipelineStageDto } from '../../api/types.ts'
import { createStage, deleteStage, listStages, renameStage, reorderStages } from '../../api/applications.ts'
import { ApiError } from '../../api/client.ts'
import './PipelineStagesSection.css'

/**
 * Settings card for the user-configurable interview pipeline (FR-019): add / rename / reorder / remove
 * stages. Removing a stage that still holds applications is blocked by the API (StageInUse) and the user
 * is asked to reassign its applications to another stage first — an application is never orphaned.
 */
export function PipelineStagesSection() {
  const [stages, setStages] = useState<PipelineStageDto[]>([])
  const [newName, setNewName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [reassignFor, setReassignFor] = useState<string | null>(null)
  const [reassignTo, setReassignTo] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    listStages(controller.signal)
      .then(setStages)
      .catch((e) => {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setError('Failed to load pipeline stages.')
      })
    return () => controller.abort()
  }, [])

  async function run(fn: () => Promise<unknown>) {
    setBusy(true)
    setError(null)
    try {
      await fn()
      setStages(await listStages())
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Something went wrong.')
      throw e
    } finally {
      setBusy(false)
    }
  }

  async function handleAdd(e: React.FormEvent) {
    e.preventDefault()
    if (!newName.trim()) return
    await run(() => createStage(newName.trim())).then(() => setNewName(''), () => {})
  }

  function handleRename(stage: PipelineStageDto, name: string) {
    if (name.trim() && name.trim() !== stage.name) {
      void run(() => renameStage(stage.id, name.trim())).catch(() => {})
    }
  }

  function move(index: number, delta: number) {
    const next = [...stages]
    const target = index + delta
    if (target < 0 || target >= next.length) return
    ;[next[index], next[target]] = [next[target], next[index]]
    setStages(next)
    void run(() => reorderStages(next.map((s) => s.id))).catch(() => {})
  }

  async function handleRemove(stage: PipelineStageDto) {
    try {
      await run(() => deleteStage(stage.id))
    } catch (e) {
      if (e instanceof ApiError && e.code === 'StageInUse') {
        // Ask where to reassign this stage's applications before removing it.
        setReassignFor(stage.id)
        setReassignTo(stages.find((s) => s.id !== stage.id)?.id ?? '')
      }
    }
  }

  async function confirmReassignRemove(stageId: string) {
    if (!reassignTo) return
    await run(() => deleteStage(stageId, reassignTo)).then(() => setReassignFor(null), () => {})
  }

  return (
    <section className="card settings-card" data-testid="pipeline-stages">
      <h2 className="settings-card__title">Pipeline stages</h2>
      <p className="muted text-sm">The columns of your Applications board. Reorder, rename, add, or remove them.</p>

      <ul className="stage-editor__list">
        {stages.map((stage, i) => (
          <li key={stage.id} className="stage-editor__row">
            <input
              className="stage-editor__name"
              defaultValue={stage.name}
              maxLength={80}
              disabled={busy}
              onBlur={(e) => handleRename(stage, e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') (e.target as HTMLInputElement).blur()
              }}
              aria-label={`Stage ${i + 1} name`}
            />
            <div className="stage-editor__actions">
              <button type="button" className="btn btn--ghost btn--sm" disabled={busy || i === 0} onClick={() => move(i, -1)} aria-label="Move up">
                ↑
              </button>
              <button
                type="button"
                className="btn btn--ghost btn--sm"
                disabled={busy || i === stages.length - 1}
                onClick={() => move(i, 1)}
                aria-label="Move down"
              >
                ↓
              </button>
              <button type="button" className="btn btn--ghost btn--sm" disabled={busy} onClick={() => void handleRemove(stage)} aria-label="Remove stage">
                Remove
              </button>
            </div>

            {reassignFor === stage.id && (
              <div className="stage-editor__reassign" role="group" aria-label="Reassign applications">
                <span className="text-sm">Move its applications to:</span>
                <select value={reassignTo} onChange={(e) => setReassignTo(e.target.value)} disabled={busy}>
                  {stages
                    .filter((s) => s.id !== stage.id)
                    .map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name}
                      </option>
                    ))}
                </select>
                <button type="button" className="btn btn--danger btn--sm" disabled={busy} onClick={() => void confirmReassignRemove(stage.id)}>
                  Reassign & remove
                </button>
                <button type="button" className="btn btn--ghost btn--sm" disabled={busy} onClick={() => setReassignFor(null)}>
                  Cancel
                </button>
              </div>
            )}
          </li>
        ))}
      </ul>

      <form className="stage-editor__add" onSubmit={handleAdd}>
        <input value={newName} onChange={(e) => setNewName(e.target.value)} placeholder="New stage name" maxLength={80} disabled={busy} aria-label="New stage name" />
        <button type="submit" className="btn btn--primary btn--sm" disabled={busy || !newName.trim()}>
          Add stage
        </button>
      </form>

      {error && (
        <div className="settings-msg settings-msg--error" role="alert">
          {error}
        </div>
      )}
    </section>
  )
}
