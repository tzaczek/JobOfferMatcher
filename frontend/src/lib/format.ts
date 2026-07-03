import type { SalaryBandDto } from '../api/types.ts'

const PERIOD_SUFFIX: Record<string, string> = {
  hourly: '/hr',
  daily: '/day',
  monthly: '/mo',
  yearly: '/yr',
}

const nf = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 })

function amountRange(min: number | null, max: number | null): string | null {
  if (min != null && max != null) return `${nf.format(min)}–${nf.format(max)}`
  if (min != null) return `${nf.format(min)}+`
  if (max != null) return `up to ${nf.format(max)}`
  return null
}

/** Raw band rendered verbatim (research §7 honesty): "18,000–22,000 PLN net/mo (B2B)". */
export function formatSalaryBand(band: SalaryBandDto): string {
  const range = amountRange(band.min, band.max)
  if (!range || !band.currency) return 'Amount not disclosed'

  const parts = [`${range} ${band.currency}`]
  if (band.tax && band.tax !== 'unknown') parts.push(band.tax)
  const suffix = band.period ? (PERIOD_SUFFIX[band.period] ?? '') : ''
  let line = parts.join(' ') + suffix
  if (band.basis && band.basis !== 'unknown') line += ` (${band.basis.toUpperCase()})`
  return line
}

export function formatWorkMode(workMode: string | null): string {
  if (!workMode) return 'Work mode unknown'
  return workMode.charAt(0).toUpperCase() + workMode.slice(1)
}

export function titleCase(value: string | null): string {
  if (!value) return ''
  return value.charAt(0).toUpperCase() + value.slice(1)
}

export function formatDate(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  return d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
}

/** Coarse relative time ("just now", "12m ago", "2h ago", "3d ago") for the feed's freshness line (#5). */
export function formatRelativeTime(iso: string | null): string {
  if (!iso) return ''
  const then = new Date(iso).getTime()
  if (Number.isNaN(then)) return ''
  const sec = Math.round((Date.now() - then) / 1000)
  if (sec < 45) return 'just now'
  const min = Math.round(sec / 60)
  if (min < 60) return `${min}m ago`
  const hr = Math.round(min / 60)
  if (hr < 24) return `${hr}h ago`
  const day = Math.round(hr / 24)
  if (day < 30) return `${day}d ago`
  const mon = Math.round(day / 30)
  if (mon < 12) return `${mon}mo ago`
  return `${Math.round(mon / 12)}y ago`
}

/**
 * Map a scan outcome to its status-chip class — shared by the feed's freshness line (#5) and the
 * Scans history table so both tint identically (complete = interested/green, partial = updated/amber,
 * failed = missing/red, unknown = neutral).
 */
export function outcomeClass(outcome: string | null): string {
  switch (outcome) {
    case 'complete':
      return 'chip chip--interested'
    case 'partial':
      return 'chip chip--updated'
    case 'failed':
      return 'chip chip--missing'
    default:
      return 'chip chip--unavailable'
  }
}

/**
 * Format a calendar date that is stored as a midnight-UTC instant (e.g. the applied date), rendering
 * in UTC so what the user picked == what is shown — regardless of the viewer's timezone.
 */
export function formatDateUtc(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  return d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
}
