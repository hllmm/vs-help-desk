import { describe, expect, it } from 'vitest'
import { resolveSafeReturnPath } from './safeReturnPath'

describe('resolveSafeReturnPath', () => {
  it('allows relative app paths', () => {
    expect(resolveSafeReturnPath('/tickets')).toBe('/tickets')
    expect(resolveSafeReturnPath('/tickets/abc-123')).toBe('/tickets/abc-123')
  })

  it('rejects protocol-relative and absolute URLs', () => {
    expect(resolveSafeReturnPath('//evil.example')).toBe('/tickets')
    expect(resolveSafeReturnPath('https://evil.example')).toBe('/tickets')
    expect(resolveSafeReturnPath('http://evil.example/x')).toBe('/tickets')
  })

  it('rejects login and empty', () => {
    expect(resolveSafeReturnPath('/login')).toBe('/tickets')
    expect(resolveSafeReturnPath(null)).toBe('/tickets')
    expect(resolveSafeReturnPath('')).toBe('/tickets')
  })
})
