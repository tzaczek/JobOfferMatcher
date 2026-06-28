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
