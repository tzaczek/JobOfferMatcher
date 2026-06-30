// Vitest + React Testing Library global setup.
// jest-dom adds matchers like toBeInTheDocument() and augments Vitest's expect.
import '@testing-library/jest-dom/vitest'
import { afterEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'

// jsdom doesn't implement matchMedia; the theme module uses it to detect the OS
// color-scheme preference. Default to light (matches: false) for deterministic tests.
if (!window.matchMedia) {
  window.matchMedia = (query: string): MediaQueryList =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }) as unknown as MediaQueryList
}

afterEach(() => {
  cleanup()
})
