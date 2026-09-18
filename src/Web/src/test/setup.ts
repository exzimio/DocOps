import '@testing-library/jest-dom/vitest'
import { afterEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'
import i18n from '@/i18n'

// jsdom does not implement matchMedia; the theme resolver depends on it.
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }),
})

// jsdom reports an English navigator language; the product default is pt-PT, so tests assert on it.
await i18n.changeLanguage('pt-PT')

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  localStorage.clear()
})
