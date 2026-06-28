import { useEffect, useState } from 'react'
import type { WeightsDto } from '../../api/types.ts'
import { getWeights, updateWeights } from '../../api/settings.ts'
import { ApiError } from '../../api/client.ts'

type WeightKey = 'skills' | 'seniority' | 'workMode' | 'employment' | 'salary'
const LABELS: Record<WeightKey, string> = {
  skills: 'Skills',
  seniority: 'Seniority',
  workMode: 'Work mode',
  employment: 'Employment',
  salary: 'Salary',
}

export function WeightsSection() {
  const [w, setW] = useState<Record<WeightKey, number> | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    getWeights()
      .then((dto) => setW(extract(dto)))
      .catch(() => setError('Failed to load weights.'))
  }, [])

  if (!w) return null

  const total = Object.values(w).reduce((a, b) => a + b, 0)

  async function save(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setSaved(false)
    try {
      const updated = await updateWeights(w!)
      setW(extract(updated))
      setSaved(true)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save weights.')
    }
  }

  return (
    <form className="card settings-card" onSubmit={save}>
      <h2 className="settings-card__title">Scoring weights</h2>
      <p className="muted text-sm">Each axis contributes its weight to the 0–100 fit score. Must sum to 100.</p>

      <div className="weights-grid">
        {(Object.keys(LABELS) as WeightKey[]).map((key) => (
          <label key={key} className="settings-field">
            <span>{LABELS[key]}</span>
            <input
              type="number"
              value={w[key]}
              onChange={(e) => setW({ ...w, [key]: Number(e.target.value) })}
            />
          </label>
        ))}
      </div>

      <div className={total === 100 ? 'weights-total weights-total--ok' : 'weights-total weights-total--bad'}>
        Total: {total} / 100
      </div>

      {error && (
        <div className="settings-msg settings-msg--error" role="alert">
          {error}
        </div>
      )}
      {saved && (
        <div className="settings-msg settings-msg--ok" role="status">
          Weights saved.
        </div>
      )}

      <div className="settings-actions">
        <button type="submit" className="btn btn--primary" disabled={total !== 100}>
          Save weights
        </button>
      </div>
    </form>
  )
}

function extract(dto: WeightsDto): Record<WeightKey, number> {
  return {
    skills: dto.skills,
    seniority: dto.seniority,
    workMode: dto.workMode,
    employment: dto.employment,
    salary: dto.salary,
  }
}
