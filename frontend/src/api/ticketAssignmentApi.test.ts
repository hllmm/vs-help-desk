import { beforeEach, describe, expect, it, vi } from 'vitest'
import { assignTicket, fetchAssignableUsers } from './ticketsApi'
import type { AssignTicketResult, AssignableUser } from './types'

const assignees: AssignableUser[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    fullName: 'Ada Destek',
    username: 'ada.destek',
  },
]

const assignment: AssignTicketResult = {
  ticketId: '22222222-2222-2222-2222-222222222222',
  assignedUserId: assignees[0]!.id,
  updatedAt: '2026-07-21T12:00:00.000Z',
  changed: true,
}

describe('ticket assignment API', () => {
  beforeEach(() => {
    document.cookie = 'vshd.csrf=; Max-Age=0; path=/'
    vi.unstubAllGlobals()
  })

  it('fetches minimal active assignees with cookies and no CSRF header', async () => {
    const controller = new AbortController()
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(assignees), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(
      fetchAssignableUsers({ signal: controller.signal }),
    ).resolves.toEqual(assignees)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/tickets/assignees')
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      signal: controller.signal,
      credentials: 'include',
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBeNull()
    expect(headers.get('X-CSRF-Token')).toBeNull()
  })

  it('puts encoded assignee including null with cookie CSRF and exact body', async () => {
    const controller = new AbortController()
    document.cookie = 'vshd.csrf=assignment-csrf'
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ ...assignment, assignedUserId: null }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await assignTicket('ticket/with spaces', null, {
      signal: controller.signal,
    })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      `/api/tickets/${encodeURIComponent('ticket/with spaces')}/assignee`,
    )
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'PUT',
      signal: controller.signal,
      credentials: 'include',
    })
    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toEqual({
      userId: null,
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBeNull()
    expect(headers.get('X-CSRF-Token')).toBe('assignment-csrf')
    expect(headers.get('Content-Type')).toBe('application/json')
  })

  it('returns the typed assignment result for an active user', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(assignment), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(
      assignTicket(assignment.ticketId, assignment.assignedUserId),
    ).resolves.toEqual(assignment)
  })
})
