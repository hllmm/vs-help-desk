import { beforeEach, describe, expect, it, vi } from 'vitest'
import { resolveTicket } from './ticketsApi'
import type { ResolveTicketResult } from './types'

const sampleResolve: ResolveTicketResult = {
  ticketId: 'ticket-1',
  ticketNumber: 'VS-000001',
  status: 'Resolved',
  resolvedAt: '2026-07-20T12:00:00.000Z',
  updatedAt: '2026-07-20T12:00:00.000Z',
  lastActivityAt: '2026-07-20T12:00:00.000Z',
  closedByUserId: 'user-1',
  changed: true,
}

describe('ticket resolution API', () => {
  beforeEach(() => {
    sessionStorage.clear()
    vi.unstubAllGlobals()
  })

  it('resolveTicket posts no body to encoded /api/tickets/{id}/resolve with signal', async () => {
    const controller = new AbortController()
    sessionStorage.setItem('vshd.accessToken', 'secret-token')
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(sampleResolve), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await resolveTicket('ticket/with spaces', {
      signal: controller.signal,
    })

    expect(result).toEqual(sampleResolve)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      `/api/tickets/${encodeURIComponent('ticket/with spaces')}/resolve`,
    )
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'POST',
      signal: controller.signal,
    })
    expect(fetchMock.mock.calls[0]?.[1]?.body).toBeUndefined()
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer secret-token')
    expect(headers.has('Content-Type')).toBe(false)
    expect(String(fetchMock.mock.calls[0]?.[0])).not.toContain('secret-token')
  })

  it('returns 200 resolve payload without treating it as user copy', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(sampleResolve), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(resolveTicket('ticket-1')).resolves.toEqual(sampleResolve)
  })

  it('surfaces protected 401 via shared client session expiry', async () => {
    sessionStorage.setItem('vshd.accessToken', 'token')
    sessionStorage.setItem(
      'vshd.user',
      JSON.stringify({
        userId: '1',
        fullName: 'Test',
        username: 'test',
      }),
    )
    const assign = vi.fn()
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { pathname: '/tickets/ticket-1', assign },
    })
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ message: 'raw-backend-401' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(resolveTicket('ticket-1')).rejects.toMatchObject({
      status: 401,
      name: 'ApiError',
    })
    expect(sessionStorage.getItem('vshd.accessToken')).toBeNull()
    expect(assign).toHaveBeenCalledWith('/login?reason=session-expired')
  })

  it('maps 404 through ApiError without exposing a UI string contract', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ message: 'ticket-missing-backend' }), {
        status: 404,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(resolveTicket('missing')).rejects.toMatchObject({
      status: 404,
      name: 'ApiError',
    })
  })

  it('maps 409 conflict through ApiError', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ message: 'concurrency-backend' }), {
        status: 409,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(resolveTicket('ticket-1')).rejects.toMatchObject({
      status: 409,
      name: 'ApiError',
    })
  })

  it('propagates network TypeError without inventing user copy', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockRejectedValue(new TypeError('Failed to fetch')),
    )

    await expect(resolveTicket('ticket-1')).rejects.toBeInstanceOf(TypeError)
  })

  it('maps 5xx through ApiError', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ message: 'upstream-500-text' }), {
        status: 500,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(resolveTicket('ticket-1')).rejects.toMatchObject({
      status: 500,
      name: 'ApiError',
    })
  })
})
