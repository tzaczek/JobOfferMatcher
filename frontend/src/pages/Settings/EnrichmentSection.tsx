import { useEffect, useState } from 'react'
import type { EnrichmentSettingsDto } from '../../api/types.ts'
import { getEnrichmentSettings, updateEnrichmentSettings } from '../../api/settings.ts'
import { ApiError } from '../../api/client.ts'

type FieldKey = keyof EnrichmentSettingsDto
const LABELS: Record<FieldKey, string> = {
  offerSummaryMaxWords: 'Offer summary — max words',
  cvSummaryMaxWords: 'CV summary — max words',
  maxKeySkills: 'Max key skills',
  fitRationaleMaxWords: 'Fit rationale — max words',
  retryLimit: 'Retry limit',
}
const ORDER: FieldKey[] = [
  'offerSummaryMaxWords',
  'cvSummaryMaxWords',
  'maxKeySkills',
  'fitRationaleMaxWords',
  'retryLimit',
]

/** Enrichment caps surfaced to the worker as guidance (FR-018). All caps soft; retryLimit ≥ 1. */
export function EnrichmentSection() {
  const [settings, setSettings] = useState<EnrichmentSettingsDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    getEnrichmentSettings(controller.signal)
      .then(setSettings)
      .catch((e) => {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setError('Failed to load enrichment settings.')
      })
    return () => controller.abort()
  }, [])

  if (!settings) return null

  const invalid = ORDER.some((k) => settings[k] < 1)

  async function save(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setSaved(false)
    try {
      const updated = await updateEnrichmentSettings(settings!)
      setSettings(updated)
      setSaved(true)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save enrichment settings.')
    }
  }

  return (
    <form className="card settings-card" onSubmit={save}>
      <h2 className="settings-card__title">Enrichment limits</h2>
      <p className="muted text-sm">
        Soft guidance to the Claude <code>/enrich</code> worker. The worker stays within these caps; the
        server validates loosely. The retry limit decides when a repeatedly-failing item is marked failed.
      </p>

      <div className="weights-grid">
        {ORDER.map((key) => (
          <label key={key} className="settings-field">
            <span>{LABELS[key]}</span>
            <input
              type="number"
              min={1}
              value={settings[key]}
              onChange={(e) => setSettings({ ...settings, [key]: Number(e.target.value) })}
            />
          </label>
        ))}
      </div>

      {error && (
        <div className="settings-msg settings-msg--error" role="alert">
          {error}
        </div>
      )}
      {saved && (
        <div className="settings-msg settings-msg--ok" role="status">
          Enrichment settings saved.
        </div>
      )}

      <div className="settings-actions">
        <button type="submit" className="btn btn--primary" disabled={invalid}>
          Save enrichment settings
        </button>
      </div>
    </form>
  )
}
