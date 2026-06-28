import { useEffect, useState } from 'react'
import type { NormalizationDto } from '../../api/types.ts'
import { getNormalization, updateNormalization } from '../../api/settings.ts'
import { ApiError } from '../../api/client.ts'

const FX_CURRENCIES = ['EUR', 'USD', 'GBP', 'CHF']

export function NormalizationSection() {
  const [dto, setDto] = useState<NormalizationDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    getNormalization()
      .then(setDto)
      .catch(() => setError('Failed to load normalization settings.'))
  }, [])

  if (!dto) return null

  function setFx(code: string, value: number) {
    setDto({ ...dto!, fxToBase: { ...dto!.fxToBase, [code]: value } })
  }

  async function save(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setSaved(false)
    try {
      const updated = await updateNormalization({
        baseCurrency: dto!.baseCurrency,
        fxToBase: dto!.fxToBase,
        assumedMonthlyHours: dto!.assumedMonthlyHours,
        assumedMonthlyWorkingDays: dto!.assumedMonthlyWorkingDays,
        b2bToPermanentFactor: dto!.b2bToPermanentFactor,
        rangeStrategy: dto!.rangeStrategy,
        fxSource: dto!.fxSource,
      })
      setDto(updated)
      setSaved(true)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save.')
    }
  }

  return (
    <form className="card settings-card" onSubmit={save}>
      <h2 className="settings-card__title">Salary normalization</h2>
      <p className="muted text-sm">
        Best-effort, offline conversion to a comparable monthly figure (base {dto.baseCurrency}). All
        normalized figures are estimates.
      </p>

      <label className="settings-field">
        <span>B2B → Permanent factor</span>
        <input
          type="number"
          step="0.01"
          value={dto.b2bToPermanentFactor}
          onChange={(e) => setDto({ ...dto, b2bToPermanentFactor: Number(e.target.value) })}
        />
      </label>

      <div className="weights-grid">
        {FX_CURRENCIES.map((code) => (
          <label key={code} className="settings-field">
            <span>{code} → {dto.baseCurrency}</span>
            <input
              type="number"
              step="0.01"
              value={dto.fxToBase[code] ?? 0}
              onChange={(e) => setFx(code, Number(e.target.value))}
            />
          </label>
        ))}
      </div>

      <div className="weights-grid">
        <label className="settings-field">
          <span>Assumed hours/month</span>
          <input
            type="number"
            value={dto.assumedMonthlyHours}
            onChange={(e) => setDto({ ...dto, assumedMonthlyHours: Number(e.target.value) })}
          />
        </label>
        <label className="settings-field">
          <span>Assumed days/month</span>
          <input
            type="number"
            value={dto.assumedMonthlyWorkingDays}
            onChange={(e) => setDto({ ...dto, assumedMonthlyWorkingDays: Number(e.target.value) })}
          />
        </label>
      </div>

      <p className="muted text-xs">FX as of {dto.fxAsOf} ({dto.fxSource})</p>

      {error && (
        <div className="settings-msg settings-msg--error" role="alert">
          {error}
        </div>
      )}
      {saved && (
        <div className="settings-msg settings-msg--ok" role="status">
          Normalization saved.
        </div>
      )}

      <div className="settings-actions">
        <button type="submit" className="btn btn--primary">
          Save normalization
        </button>
      </div>
    </form>
  )
}
