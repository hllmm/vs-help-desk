import { beforeEach, describe, expect, it, vi } from 'vitest'
import { replyToTicket, fetchTicketDetails } from './ticketsApi'
import type { SupportReplyResult, TicketDetails } from './types'

const sampleDetails: TicketDetails = {
  id: 'ticket-1',
  ticketNumber: 'VS-000001',
  subject: 'Yazıcı',
  customerName: 'Ayşe',
  customerEmail: 'ayse@example.com',
  status: 'New',
  assignedUserId: null,
  createdAt: '2026-07-20T09:00:00.000Z',
  updatedAt: '2026-07-20T09:00:00.000Z',
  lastActivityAt: '2026-07-20T09:00:00.000Z',
  waitingCustomerSince: null,
  resolvedAt: null,
  closedByUserId: null,
  messages: [],
  attachments: [],
}

const sampleReply: SupportReplyResult = {
  ticketId: 'ticket-1',
  ticketNumber: 'VS-000001',
  messageId: 'msg-1',
  status: 'WaitingCustomerReply',
  emailDelivered: true,
  ticketStateUpdated: true,
  noticeCode: null,
}

describe('ticket details API', () => {
  beforeEach(() => {
    sessionStorage.clear()
    vi.unstubAllGlobals()
  })

  it('fetchTicketDetails uses GET /api/tickets/{encoded-id} with AbortSignal', async () => {
    const controller = new AbortController()
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(sampleDetails), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await fetchTicketDetails('id with/slash', {
      signal: controller.signal,
    })

    expect(result).toEqual(sampleDetails)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      `/api/tickets/${encodeURIComponent('id with/slash')}`,
    )
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'GET',
      signal: controller.signal,
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.has('Content-Type')).toBe(false)
  })

  it('replyToTicket posts { content } only to /api/tickets/{encoded-id}/replies', async () => {
    const controller = new AbortController()
    sessionStorage.setItem('vshd.accessToken', 'secret-token')
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(sampleReply), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await replyToTicket(
      'ticket/42',
      { content: 'Merhaba' },
      { signal: controller.signal },
    )

    expect(result).toEqual(sampleReply)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      `/api/tickets/${encodeURIComponent('ticket/42')}/replies`,
    )
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'POST',
      signal: controller.signal,
      body: JSON.stringify({ content: 'Merhaba' }),
    })
    const body = JSON.parse(
      fetchMock.mock.calls[0]?.[1]?.body as string,
    ) as Record<string, unknown>
    expect(body).toEqual({ content: 'Merhaba' })
    expect(body).not.toHaveProperty('isHtml')
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer secret-token')
    expect(String(fetchMock.mock.calls[0]?.[0])).not.toContain('secret-token')
  })
})
