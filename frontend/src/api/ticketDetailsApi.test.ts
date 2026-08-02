import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  fetchTicketDetails,
  fetchTicketMessages,
  replyToTicket,
} from './ticketsApi'
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
  nextMessageCursor: null,
  hasMoreMessages: false,
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
    document.cookie = 'vshd.csrf=; Max-Age=0; path=/'
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

  it('fetchTicketMessages requests the older history page with pageSize and cursor', async () => {
    const controller = new AbortController()
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          messages: [],
          attachments: [],
          nextCursor: null,
          hasMore: false,
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await fetchTicketMessages('id with/slash', {
      signal: controller.signal,
      pageSize: 100,
      cursor: 'opaque cursor?+=&',
    })

    expect(result).toEqual({
      messages: [],
      attachments: [],
      nextCursor: null,
      hasMore: false,
    })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const url = fetchMock.mock.calls[0]?.[0] as string
    expect(url.startsWith(`/api/tickets/${encodeURIComponent('id with/slash')}/messages?`)).toBe(true)
    expect(url).toContain('pageSize=100')
    const params = new URLSearchParams({ cursor: 'opaque cursor?+=&' })
    expect(url).toContain(`cursor=${params.toString().slice('cursor='.length)}`)
    expect(
      new URLSearchParams(url.slice(url.indexOf('?') + 1)).get('cursor'),
    ).toBe('opaque cursor?+=&')
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'GET',
      signal: controller.signal,
    })
  })

  it('replyToTicket posts { content } only to /api/tickets/{encoded-id}/replies', async () => {
    const controller = new AbortController()
    document.cookie = 'vshd.csrf=reply-csrf'
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
      credentials: 'include',
    })
    const body = JSON.parse(
      fetchMock.mock.calls[0]?.[1]?.body as string,
    ) as Record<string, unknown>
    expect(body).toEqual({ content: 'Merhaba' })
    expect(body).not.toHaveProperty('isHtml')
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBeNull()
    expect(headers.get('X-CSRF-Token')).toBe('reply-csrf')
  })
})

