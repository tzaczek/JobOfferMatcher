import { useEffect, useState } from 'react'
import type { SearchCriteriaDto, SourceDto } from '../../api/types.ts'
import {
  createSource,
  disableSource,
  enableSource,
  listSources,
  updateSource,
} from '../../api/sources.ts'
import { ApiError } from '../../api/client.ts'
import './SourcesPage.css'

interface SourceForm {
  id: string | null // null = creating a new source
  name: string
  kind: string
  requiresLogin: boolean
  categories: string
  experienceLevels: string
  employmentTypes: string
  workingTimes: string
  workplaceKeep: string
  withSalary: boolean
  sortBy: string
  orderBy: string
}

// The backend SourceKind enum (Domain/Sources/SourceKind.cs) — submitted verbatim so it round-trips.
const SOURCE_KINDS = [
  { value: 'DirectApi', label: 'Direct API' },
  { value: 'InteractiveBrowser', label: 'Interactive browser (manual login)' },
] as const

const BLANK_FORM: SourceForm = {
  id: null,
  name: '',
  kind: 'DirectApi',
  requiresLogin: false,
  categories: '',
  experienceLevels: '',
  employmentTypes: '',
  workingTimes: '',
  workplaceKeep: '',
  withSalary: false,
  sortBy: '',
  orderBy: '',
}

function toList(value: string): string[] {
  return value
    .split(',')
    .map((v) => v.trim())
    .filter((v) => v.length > 0)
}

function formFromSource(s: SourceDto): SourceForm {
  const c = s.searchCriteria
  return {
    id: s.id,
    name: s.name,
    kind: s.kind,
    requiresLogin: s.requiresLogin,
    categories: c.categories.join(', '),
    experienceLevels: c.experienceLevels.join(', '),
    employmentTypes: c.employmentTypes.join(', '),
    workingTimes: c.workingTimes.join(', '),
    workplaceKeep: c.workplaceKeep.join(', '),
    withSalary: c.withSalary,
    sortBy: c.sortBy ?? '',
    orderBy: c.orderBy ?? '',
  }
}

function criteriaFromForm(f: SourceForm): SearchCriteriaDto {
  return {
    categories: toList(f.categories),
    experienceLevels: toList(f.experienceLevels),
    employmentTypes: toList(f.employmentTypes),
    workingTimes: toList(f.workingTimes),
    withSalary: f.withSalary,
    sortBy: f.sortBy.trim() || null,
    orderBy: f.orderBy.trim() || null,
    workplaceKeep: toList(f.workplaceKeep),
  }
}

function criteriaRows(c: SearchCriteriaDto): { label: string; value: string }[] {
  const rows: { label: string; value: string }[] = []
  if (c.categories.length) rows.push({ label: 'Categories', value: c.categories.join(', ') })
  if (c.experienceLevels.length) rows.push({ label: 'Experience', value: c.experienceLevels.join(', ') })
  if (c.employmentTypes.length) rows.push({ label: 'Employment', value: c.employmentTypes.join(', ') })
  if (c.workingTimes.length) rows.push({ label: 'Working time', value: c.workingTimes.join(', ') })
  if (c.workplaceKeep.length) rows.push({ label: 'Workplace', value: c.workplaceKeep.join(', ') })
  rows.push({ label: 'With salary', value: c.withSalary ? 'Yes' : 'No' })
  if (c.sortBy) rows.push({ label: 'Sort by', value: c.sortBy })
  if (c.orderBy) rows.push({ label: 'Order', value: c.orderBy })
  return rows
}

export function SourcesPage() {
  const [sources, setSources] = useState<SourceDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [form, setForm] = useState<SourceForm | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)

  async function reload(signal?: AbortSignal) {
    const r = await listSources(signal)
    setSources(r.data)
  }

  useEffect(() => {
    const controller = new AbortController()
    reload(controller.signal).catch((e) => {
      if (e instanceof DOMException && e.name === 'AbortError') return
      setError(e instanceof ApiError ? e.message : 'Failed to load sources.')
    })
    return () => controller.abort()
  }, [])

  function setField<K extends keyof SourceForm>(key: K, value: SourceForm[K]) {
    setForm((f) => (f ? { ...f, [key]: value } : f))
  }

  function openCreate() {
    setFormError(null)
    setForm({ ...BLANK_FORM })
  }

  function openEdit(s: SourceDto) {
    setFormError(null)
    setForm(formFromSource(s))
  }

  function closeForm() {
    setForm(null)
    setFormError(null)
  }

  async function handleSave(e: React.FormEvent) {
    e.preventDefault()
    if (!form) return
    setSaving(true)
    setFormError(null)
    try {
      const searchCriteria = criteriaFromForm(form)
      if (form.id) {
        await updateSource(form.id, {
          name: form.name,
          searchCriteria,
          requiresLogin: form.requiresLogin,
        })
      } else {
        await createSource({
          name: form.name,
          kind: form.kind,
          searchCriteria,
          requiresLogin: form.requiresLogin,
        })
      }
      closeForm()
      await reload()
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : 'Failed to save source.')
    } finally {
      setSaving(false)
    }
  }

  async function handleToggle(s: SourceDto) {
    setBusyId(s.id)
    setError(null)
    try {
      if (s.enabled) await disableSource(s.id)
      else await enableSource(s.id)
      await reload()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to update source.')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <section className="sources-page">
      <div className="sources-page__header">
        <div>
          <h1 className="page-title">Sources</h1>
          <p className="page-subtitle">Configure where offers are collected from and their search criteria.</p>
        </div>
        <button type="button" className="btn btn--primary" onClick={openCreate} disabled={form?.id === null}>
          Add source
        </button>
      </div>

      {error && (
        <div className="state-block" role="alert">
          {error}
        </div>
      )}

      {form && (
        <form className="card sources-form" onSubmit={handleSave}>
          <h2 className="settings-card__title">{form.id ? 'Edit source' : 'New source'}</h2>

          <label className="settings-field">
            <span>Name</span>
            <input
              type="text"
              value={form.name}
              onChange={(e) => setField('name', e.target.value)}
              placeholder="JustJoin.it — .NET, Kraków"
              required
            />
          </label>

          {form.id ? (
            <p className="muted text-sm">
              Kind: <strong>{form.kind}</strong>
            </p>
          ) : (
            <label className="settings-field">
              <span>Kind</span>
              <select value={form.kind} onChange={(e) => setField('kind', e.target.value)}>
                {SOURCE_KINDS.map((k) => (
                  <option key={k.value} value={k.value}>
                    {k.label}
                  </option>
                ))}
              </select>
            </label>
          )}

          <label className="settings-field">
            <span>Categories</span>
            <input
              type="text"
              value={form.categories}
              onChange={(e) => setField('categories', e.target.value)}
              placeholder="backend, devops"
            />
            <small className="muted">Comma-separated.</small>
          </label>

          <label className="settings-field">
            <span>Experience levels</span>
            <input
              type="text"
              value={form.experienceLevels}
              onChange={(e) => setField('experienceLevels', e.target.value)}
              placeholder="senior, mid"
            />
            <small className="muted">Comma-separated.</small>
          </label>

          <label className="settings-field">
            <span>Employment types</span>
            <input
              type="text"
              value={form.employmentTypes}
              onChange={(e) => setField('employmentTypes', e.target.value)}
              placeholder="b2b, permanent"
            />
            <small className="muted">Comma-separated.</small>
          </label>

          <label className="settings-field">
            <span>Working times</span>
            <input
              type="text"
              value={form.workingTimes}
              onChange={(e) => setField('workingTimes', e.target.value)}
              placeholder="full-time"
            />
            <small className="muted">Comma-separated.</small>
          </label>

          <label className="settings-field">
            <span>Workplace keep</span>
            <input
              type="text"
              value={form.workplaceKeep}
              onChange={(e) => setField('workplaceKeep', e.target.value)}
              placeholder="remote, hybrid"
            />
            <small className="muted">Comma-separated workplace filters to keep.</small>
          </label>

          <div className="sources-form__row">
            <label className="settings-field">
              <span>Sort by</span>
              <input
                type="text"
                value={form.sortBy}
                onChange={(e) => setField('sortBy', e.target.value)}
                placeholder="published"
              />
            </label>
            <label className="settings-field">
              <span>Order by</span>
              <input
                type="text"
                value={form.orderBy}
                onChange={(e) => setField('orderBy', e.target.value)}
                placeholder="desc"
              />
            </label>
          </div>

          <label className="settings-toggle">
            <input
              type="checkbox"
              checked={form.withSalary}
              onChange={(e) => setField('withSalary', e.target.checked)}
            />
            <span>Only offers with disclosed salary</span>
          </label>

          <label className="settings-toggle">
            <input
              type="checkbox"
              checked={form.requiresLogin}
              onChange={(e) => setField('requiresLogin', e.target.checked)}
            />
            <span>Requires login</span>
          </label>

          {formError && <div className="settings-msg settings-msg--error">{formError}</div>}

          <div className="sources-form__actions">
            <button type="button" className="btn btn--ghost" onClick={closeForm} disabled={saving}>
              Cancel
            </button>
            <button type="submit" className="btn btn--primary" disabled={saving}>
              {saving ? 'Saving…' : form.id ? 'Save changes' : 'Add source'}
            </button>
          </div>
        </form>
      )}

      {!error && sources === null && (
        <div className="state-block">
          <span className="spinner" aria-label="Loading" /> Loading sources…
        </div>
      )}

      {!error && sources !== null && sources.length === 0 && !form && (
        <div className="state-block" data-testid="sources-empty">
          No sources configured yet — add one to start collecting offers.
        </div>
      )}

      {!error && sources !== null && sources.length > 0 && (
        <ul className="sources-list">
          {sources.map((s) => (
            <li key={s.id} className="card source-item">
              <div className="source-item__head">
                <div>
                  <h2 className="source-item__name">{s.name}</h2>
                  <div className="source-item__meta">
                    <span className="chip chip--skill">{s.kind}</span>
                    {s.enabled ? (
                      <span className="chip chip--interested">Enabled</span>
                    ) : (
                      <span className="chip chip--dismissed">Disabled</span>
                    )}
                    {s.requiresLogin && <span className="chip chip--updated">Requires login</span>}
                  </div>
                </div>
                <div className="source-item__actions">
                  <button
                    type="button"
                    className="btn btn--ghost btn--sm"
                    onClick={() => handleToggle(s)}
                    disabled={busyId === s.id}
                  >
                    {s.enabled ? 'Disable' : 'Enable'}
                  </button>
                  <button
                    type="button"
                    className="btn btn--ghost btn--sm"
                    onClick={() => openEdit(s)}
                    aria-label={`Edit ${s.name}`}
                  >
                    Edit
                  </button>
                </div>
              </div>

              <dl className="source-criteria">
                {criteriaRows(s.searchCriteria).map((row) => (
                  <div key={row.label} className="source-criteria__row">
                    <dt>{row.label}</dt>
                    <dd>{row.value}</dd>
                  </div>
                ))}
              </dl>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
