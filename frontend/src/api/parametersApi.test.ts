import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  listParameterAudit,
  listParameters,
  updateParameter,
} from './parametersApi'
import type { Parameter, ParameterChangeLog } from './types'

const sampleParameter: Parameter = {
  key: 'AutoResolve.InactiveDays',
  value: '3',
  description:
    'WaitingCustomerReply sonrası otomatik çözüm eşiği (gün)',
  updatedAt: '2026-07-21T12:00:00.000Z',
}

const sampleAudit: ParameterChangeLog = {
  id: 'log-1',
  parameterKey: 'AutoResolve.InactiveDays',
  oldValue: '3',
  newValue: '5',
  changedByUserId: 'user-1',
  changedByUsername: 'admin',
  changedAt: '2026-07-21T13:00:00.000Z',
}

function clearCsrfCookie() {
  document.cookie = 'vshd.csrf=; Max-Age=0; path=/'
}

describe('parameters API', () => {
  beforeEach(() => {
    sessionStorage.clear()
    clearCsrfCookie()
    vi.unstubAllGlobals()
  })

  it('listParameters uses GET /api/parameters with AbortSignal', async () => {
    const controller = new AbortController()
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([sampleParameter]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await listParameters({ signal: controller.signal })

    expect(result).toEqual([sampleParameter])
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/parameters')
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'GET',
      signal: controller.signal,
      credentials: 'include',
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.has('Content-Type')).toBe(false)
    expect(headers.get('Authorization')).toBeNull()
  })

  it('listParameters uses credentials include without Authorization', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([sampleParameter]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await listParameters()

    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBeNull()
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      credentials: 'include',
    })
  })

  it('updateParameter puts { value } only with CSRF when cookie present', async () => {
    const controller = new AbortController()
    document.cookie = 'vshd.csrf=param-csrf'
    const updated: Parameter = {
      ...sampleParameter,
      value: '7',
      updatedAt: '2026-07-21T13:00:00.000Z',
    }
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(updated), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await updateParameter(
      'AutoResolve.InactiveDays',
      '7',
      { signal: controller.signal },
    )

    expect(result).toEqual(updated)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      `/api/parameters/${encodeURIComponent('AutoResolve.InactiveDays')}`,
    )
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'PUT',
      signal: controller.signal,
      body: JSON.stringify({ value: '7' }),
      credentials: 'include',
    })
    const body = JSON.parse(
      fetchMock.mock.calls[0]?.[1]?.body as string,
    ) as Record<string, unknown>
    expect(body).toEqual({ value: '7' })
    expect(body).not.toHaveProperty('key')
    expect(body).not.toHaveProperty('description')
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBeNull()
    expect(headers.get('X-CSRF-Token')).toBe('param-csrf')
    expect(headers.get('Content-Type')).toBe('application/json')
  })

  it('updateParameter encodes keys with reserved characters', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(sampleParameter), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await updateParameter('key with/slash', '1')

    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      `/api/parameters/${encodeURIComponent('key with/slash')}`,
    )
  })

  it('listParameterAudit uses GET /api/parameters/audit with take and key', async () => {
    const controller = new AbortController()
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([sampleAudit]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await listParameterAudit({
      take: 20,
      key: 'AutoResolve.InactiveDays',
      signal: controller.signal,
    })

    expect(result).toEqual([sampleAudit])
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const url = String(fetchMock.mock.calls[0]?.[0])
    expect(url.startsWith('/api/parameters/audit?')).toBe(true)
    expect(url).toContain('take=20')
    expect(url).toContain(
      `key=${encodeURIComponent('AutoResolve.InactiveDays')}`,
    )
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'GET',
      signal: controller.signal,
      credentials: 'include',
    })
  })
})
