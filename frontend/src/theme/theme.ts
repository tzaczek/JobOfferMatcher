// Light/dark theme state. The active theme is reflected as data-theme on <html>;
// the actual color values live in tokens.css. The inline script in index.html
// applies the correct theme before first paint (no flash); this module keeps it
// in sync afterwards and lets React components read/toggle it.
import { useEffect, useState } from 'react'

export type ThemeMode = 'light' | 'dark'

const STORAGE_KEY = 'jom-theme'

// Must match the app-background / dark-background tokens used by tokens.css so the
// mobile browser chrome (theme-color) matches the page.
const THEME_COLOR: Record<ThemeMode, string> = {
  light: '#f8f9fc',
  dark: '#0e1016',
}

function systemTheme(): ThemeMode {
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

/** The user's pinned choice, or null when they're following the system preference. */
function storedTheme(): ThemeMode | null {
  try {
    const v = localStorage.getItem(STORAGE_KEY)
    return v === 'light' || v === 'dark' ? v : null
  } catch {
    return null
  }
}

/** The theme currently on <html> (set pre-paint by index.html), falling back to
 *  stored choice then system preference. */
export function getActiveTheme(): ThemeMode {
  const attr = document.documentElement.dataset.theme
  if (attr === 'light' || attr === 'dark') return attr
  return storedTheme() ?? systemTheme()
}

/** Reflect a theme onto the document without persisting it (used for system changes). */
export function applyTheme(mode: ThemeMode): void {
  document.documentElement.dataset.theme = mode
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', THEME_COLOR[mode])
}

// In-process subscription so every useTheme() consumer re-renders together.
const listeners = new Set<(m: ThemeMode) => void>()

function broadcast(mode: ThemeMode): void {
  listeners.forEach((l) => l(mode))
}

/** Pin and apply an explicit theme choice. */
export function setTheme(mode: ThemeMode): void {
  try {
    localStorage.setItem(STORAGE_KEY, mode)
  } catch {
    // Storage unavailable (private mode / disabled) — apply for this session anyway.
  }
  applyTheme(mode)
  broadcast(mode)
}

export function useTheme(): {
  theme: ThemeMode
  toggle: () => void
  setTheme: (mode: ThemeMode) => void
} {
  const [theme, setLocal] = useState<ThemeMode>(getActiveTheme)

  useEffect(() => {
    const onChange = (m: ThemeMode) => setLocal(m)
    listeners.add(onChange)

    // Follow the OS only while the user hasn't pinned a choice.
    const mq = window.matchMedia?.('(prefers-color-scheme: dark)')
    const onSystem = (e: MediaQueryListEvent) => {
      if (storedTheme() === null) {
        const m: ThemeMode = e.matches ? 'dark' : 'light'
        applyTheme(m)
        broadcast(m)
      }
    }
    mq?.addEventListener?.('change', onSystem)

    return () => {
      listeners.delete(onChange)
      mq?.removeEventListener?.('change', onSystem)
    }
  }, [])

  return {
    theme,
    setTheme,
    toggle: () => setTheme(theme === 'dark' ? 'light' : 'dark'),
  }
}
