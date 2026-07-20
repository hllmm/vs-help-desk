import { beforeEach, describe, expect, it, vi } from 'vitest'
import { listParameters, updateParameter } from './parametersApi'
import type { Parameter } from './types'

const sampleParameter: Parameter = {
  key: 'AutoResolve.InactiveDays',
  value: '3',
  description:
    'WaitingCustomerReply sonrası otomatik çözüm eşiği (gün)',
  updatedAt: '2026-07-21T12:00:00.000Z',
}

describe('parameters API', () => {
  beforeEach(() => {
    sessionStorage.clear()
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
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.has('Content-Type')).toBe(false)
  })

  it('listParameters sends Authorization bearer token when present', async () => {
    sessionStorage.setItem('vshd.accessToken', 'secret-token')
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([sampleParameter]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await listParameters()

    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer secret-token')
    expect(String(fetchMock.mock.calls[0]?.[0])).not.toContain('secret-token')
  })

  it('updateParameter puts { value } only to /api/parameters/{encoded-key}', async () => {
    const controller = new AbortController()
    sessionStorage.setItem('vshd.accessToken', 'secret-token')
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
    })
    const body = JSON.parse(
      fetchMock.mock.calls[0]?.[1]?.body as string,
    ) as Record<string, unknown>
    expect(body).toEqual({ value: '7' })
    expect(body).not.toHaveProperty('key')
    expect(body).not.toHaveProperty('description')
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer secret-token')
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(String(fetchMock.mock.calls[0]?.[0])).not.toContain('secret-token')
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
})
