import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

afterEach(() => {
  cleanup()
  sessionStorage.clear()
  // Clear CSRF double-submit cookie used by the API client.
  document.cookie = 'vshd.csrf=; Max-Age=0; path=/'
  window.history.replaceState({}, '', '/')
  vi.useRealTimers()
  vi.restoreAllMocks()
  vi.unstubAllEnvs()
  vi.unstubAllGlobals()
})
