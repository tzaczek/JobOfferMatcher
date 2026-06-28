// Vitest + React Testing Library global setup.
// jest-dom adds matchers like toBeInTheDocument() and augments Vitest's expect.
import '@testing-library/jest-dom/vitest'
import { afterEach } from 'vitest'
import { cleanup } from '@testing-library/react'

afterEach(() => {
  cleanup()
})
