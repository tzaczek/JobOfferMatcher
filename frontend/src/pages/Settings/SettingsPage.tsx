import { useEffect, useState } from 'react'
import type { ScheduleDto } from '../../api/types.ts'
import { getSchedule, updateSchedule } from '../../api/schedule.ts'
import { ApiError } from '../../api/client.ts'
import { WeightsSection } from './WeightsSection.tsx'
import { NormalizationSection } from './NormalizationSection.tsx'
import './SettingsPage.css'

export function SettingsPage() {
  const [schedule, setSchedule] = useState<ScheduleDto | null>(null)
  const [cron, setCron] = useState('')
  const [timeZone, setTimeZone] = useState('')
  const [enabled, setEnabled] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    getSchedule(controller.signal)
      .then((s) => {
        setSchedule(s)
        setCron(s.cron)
        setTimeZone(s.timeZone)
        setEnabled(s.enabled)
      })
      .catch((e) => {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setError('Failed to load schedule.')
      })
    return () => controller.abort()
  }, [])

  async function handleSave(e: React.FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError(null)
    setSaved(false)
    try {
      const updated = await updateSchedule({ cron, timeZone, enabled })
      setSchedule(updated)
      setSaved(true)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save schedule.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="settings-page">
      <h1 className="page-title">Settings</h1>
      <p className="page-subtitle">Configure when scans run automatically.</p>

      <form className="card settings-card" onSubmit={handleSave}>
        <h2 className="settings-card__title">Scan schedule</h2>

        <label className="settings-field">
          <span>Cron expression</span>
          <input
            type="text"
            value={cron}
            onChange={(e) => setCron(e.target.value)}
            placeholder="0 6,13,20 * * *"
            spellCheck={false}
          />
          <small className="muted">Runs at least 3×/day. Example: 6am, 1pm, 8pm daily.</small>
        </label>

        <label className="settings-field">
          <span>Time zone</span>
          <input
            type="text"
            value={timeZone}
            onChange={(e) => setTimeZone(e.target.value)}
            placeholder="Europe/Warsaw"
            spellCheck={false}
          />
        </label>

        <label className="settings-toggle">
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
          <span>Run scans automatically</span>
        </label>

        {schedule?.lastRunUtc && (
          <p className="muted text-sm">Last scheduled run: {new Date(schedule.lastRunUtc).toLocaleString()}</p>
        )}

        {error && (
          <div className="settings-msg settings-msg--error" role="alert">
            {error}
          </div>
        )}
        {saved && (
          <div className="settings-msg settings-msg--ok" role="status">
            Schedule saved.
          </div>
        )}

        <div className="settings-actions">
          <button type="submit" className="btn btn--primary" disabled={saving}>
            {saving ? 'Saving…' : 'Save schedule'}
          </button>
        </div>
      </form>

      <p className="muted text-sm settings-note">
        Note: automatic scans only run while the app is running. For unattended runs while the app is
        closed, install it as a Windows Service / Task Scheduler login-item.
      </p>

      <WeightsSection />
      <NormalizationSection />
    </section>
  )
}
