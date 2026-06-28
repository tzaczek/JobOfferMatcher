// Typed helpers that map domain concepts to the single design-truth chip classes.
// Components call these instead of branching on colors inline (Principle VIII).
import './tokens.css'
import './base.css'

export type UserStatus = 'new' | 'viewed' | 'interested' | 'dismissed'
export type NormalizationQuality = 'Reported' | 'Estimated' | 'RoughEstimate'

export function statusChipClass(status: UserStatus): string {
  return `chip chip--${status}`
}

export function qualityChipClass(quality: NormalizationQuality): string {
  switch (quality) {
    case 'Reported':
      return 'chip chip--quality-reported'
    case 'Estimated':
      return 'chip chip--quality-estimated'
    case 'RoughEstimate':
      return 'chip chip--quality-rough'
  }
}

/** Fit score 0–100 → a CSS color token for the fit ring/label. */
export function fitColorVar(score: number): string {
  if (score >= 70) return 'var(--c-fit-high)'
  if (score >= 40) return 'var(--c-fit-mid)'
  return 'var(--c-fit-low)'
}
