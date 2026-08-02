import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type {
  TicketListItem,
  TicketListPage,
  TicketStatusCounts,
} from '../../api/types'
import { useTickets } from './useTickets'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function ticket(id: string): TicketListItem {
  return {
    id,
    ticketNumber: `VS-${id.padStart(6, '0')}`,
    subject: `Konu ${id}`,
    customerName: `Müşteri ${id}`,
    customerEmail: `musteri${id}@example.com`,
    status: 'New',
    lastActivityAt: '2026-08-02T08:00:00.000Z',
    assignedUserId: null,
  }
}

const counts: TicketStatusCounts = {
  all: 4,
  new: 1,
  waitingCustomerReply: 1,
  customerReplied: 1,
  resolved: 1,
}

function page(
  items: TicketListItem[],
  options: Partial<Omit<TicketListPage, 'items'>> = {},
): TicketListPage {
  return {
    items,
    nextCursor: null,
    hasMore: false,
    counts,
    ...options,
  }
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function requestUrl(callIndex: number): string {
  return String(vi.mocked(fetch).mock.calls[callIndex]?.[0])
}

function requestSignal(callIndex: number): AbortSignal {
  return vi.mocked(fetch).mock.calls[callIndex]?.[1]?.signal as AbortSignal
}

describe('useTickets', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests the first bounded page without empty filters or cursor', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse(page([ticket('1')])))

    const { result } = renderHook(() =>
      useTickets({ query: '   ', status: 'All' }),
    )

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(requestUrl(0)).toBe('/api/tickets?pageSize=50')
    expect(result.current.tickets).toEqual([ticket('1')])
    expect(result.current.counts).toEqual(counts)
    expect(result.current.hasMore).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('trims and URL-encodes a valid search and sends a selected status', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse(page([])))
      .mockResolvedValueOnce(jsonResponse(page([])))

    const { result, rerender } = renderHook(
      ({ query, status }: { query: string; status: 'All' | 'New' }) =>
        useTickets({ query, status }),
      {
        initialProps: {
          query: '  İş  ',
          status: 'All' as 'All' | 'New',
        },
      },
    )

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })
    expect(requestUrl(0)).toBe('/api/tickets?pageSize=50&search=%C4%B0%C5%9F')

    rerender({ query: '', status: 'New' })
    await waitFor(() => {
      expect(fetch).toHaveBeenCalledTimes(2)
    })
    expect(requestUrl(1)).toBe('/api/tickets?pageSize=50&status=New')
  })

  it('follows the active cursor, preserves server order, and deduplicates ids', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(
        jsonResponse(
          page([ticket('1'), ticket('2')], {
            nextCursor: 'son hareket/+id==',
            hasMore: true,
          }),
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse(page([ticket('2'), ticket('3')], { counts })),
      )

    const { result } = renderHook(() =>
      useTickets({ query: '', status: 'All' }),
    )
    await waitFor(() => {
      expect(result.current.hasMore).toBe(true)
    })

    await act(async () => {
      await result.current.loadMore()
    })

    expect(requestUrl(1)).toBe(
      '/api/tickets?pageSize=50&cursor=son+hareket%2F%2Bid%3D%3D',
    )
    expect(result.current.tickets.map(({ id }) => id)).toEqual(['1', '2', '3'])
    expect(result.current.hasMore).toBe(false)
    expect(result.current.isLoadingMore).toBe(false)
  })

  it('refresh replaces accumulated pages and drops the old cursor', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(
        jsonResponse(
          page([ticket('1')], { nextCursor: 'old-cursor', hasMore: true }),
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse(
          page([ticket('2')], { nextCursor: 'next-cursor', hasMore: true }),
        ),
      )
      .mockResolvedValueOnce(jsonResponse(page([ticket('9')])))

    const { result } = renderHook(() =>
      useTickets({ query: '', status: 'All' }),
    )
    await waitFor(() => expect(result.current.hasMore).toBe(true))
    await act(async () => result.current.loadMore())
    expect(result.current.tickets.map(({ id }) => id)).toEqual(['1', '2'])

    await act(async () => result.current.refresh())

    expect(requestUrl(2)).toBe('/api/tickets?pageSize=50')
    expect(result.current.tickets.map(({ id }) => id)).toEqual(['9'])
    expect(result.current.hasMore).toBe(false)
  })

  it('aborts and clears accumulated pages when query or status changes', async () => {
    const stale = deferred<Response>()
    const queryPending = deferred<Response>()
    const statusPending = deferred<Response>()
    vi.mocked(fetch)
      .mockReturnValueOnce(stale.promise)
      .mockReturnValueOnce(queryPending.promise)
      .mockReturnValueOnce(statusPending.promise)

    const { result, rerender } = renderHook(
      ({ query, status }: { query: string; status: 'All' | 'Resolved' }) =>
        useTickets({ query, status }),
      {
        initialProps: {
          query: '',
          status: 'All' as 'All' | 'Resolved',
        },
      },
    )
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    const staleSignal = requestSignal(0)

    rerender({ query: 'yazıcı', status: 'All' })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    expect(staleSignal.aborted).toBe(true)
    expect(result.current.tickets).toEqual([])
    await act(async () => {
      queryPending.resolve(jsonResponse(page([ticket('2')])))
    })
    await waitFor(() => expect(result.current.tickets).toEqual([ticket('2')]))

    rerender({ query: 'yazıcı', status: 'Resolved' })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(3))
    expect(result.current.tickets).toEqual([])
    await act(async () => {
      statusPending.resolve(jsonResponse(page([ticket('3')])))
    })
    await waitFor(() => expect(result.current.tickets).toEqual([ticket('3')]))

    await act(async () => {
      stale.resolve(jsonResponse(page([ticket('1')])))
    })
    expect(result.current.tickets).toEqual([ticket('3')])
  })

  it('keeps successful initialization through replacement and empty refresh failure', async () => {
    const replacement = deferred<Response>()
    const refresh = deferred<Response>()
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse(page([])))
      .mockReturnValueOnce(replacement.promise)
      .mockReturnValueOnce(refresh.promise)

    const { result, rerender } = renderHook(
      ({ query }) => useTickets({ query, status: 'All' }),
      { initialProps: { query: '' } },
    )
    await waitFor(() => expect(result.current.isLoading).toBe(false))
    expect(result.current.hasInitialized).toBe(true)

    rerender({ query: 'yazıcı' })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    expect(result.current.isLoading).toBe(true)
    expect(result.current.tickets).toEqual([])
    expect(result.current.hasInitialized).toBe(true)

    await act(async () => {
      replacement.resolve(jsonResponse(page([])))
    })
    await waitFor(() => expect(result.current.isLoading).toBe(false))

    act(() => {
      void result.current.refresh()
    })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(3))
    expect(result.current.isLoading).toBe(true)
    expect(result.current.hasInitialized).toBe(true)

    await act(async () => {
      refresh.reject(new Error('Server boom'))
    })
    await waitFor(() => {
      expect(result.current.error).toEqual({ kind: 'server', source: 'list' })
    })
    expect(result.current.hasInitialized).toBe(true)
  })

  it('distinguishes replace and append failures and retries without losing rows', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse({ message: 'bozuk' }, 500))
      .mockResolvedValueOnce(
        jsonResponse(
          page([ticket('1')], { nextCursor: 'retry-cursor', hasMore: true }),
        ),
      )
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(jsonResponse(page([ticket('2')])))

    const { result } = renderHook(() =>
      useTickets({ query: '', status: 'All' }),
    )
    await waitFor(() => {
      expect(result.current.error).toEqual({ kind: 'server', source: 'list' })
    })

    await act(async () => result.current.refresh())
    expect(result.current.error).toBeNull()
    expect(result.current.tickets).toEqual([ticket('1')])

    await act(async () => result.current.loadMore())
    expect(result.current.error).toEqual({ kind: 'network', source: 'loadMore' })
    expect(result.current.tickets).toEqual([ticket('1')])
    expect(result.current.hasMore).toBe(true)

    await act(async () => result.current.loadMore())
    expect(requestUrl(3)).toContain('cursor=retry-cursor')
    expect(result.current.error).toBeNull()
    expect(result.current.tickets.map(({ id }) => id)).toEqual(['1', '2'])
  })

  it('ignores an append response after a replacement starts', async () => {
    const append = deferred<Response>()
    vi.mocked(fetch)
      .mockResolvedValueOnce(
        jsonResponse(page([ticket('1')], { nextCursor: 'cursor', hasMore: true })),
      )
      .mockReturnValueOnce(append.promise)
      .mockResolvedValueOnce(jsonResponse(page([ticket('9')])))

    const { result } = renderHook(() =>
      useTickets({ query: '', status: 'All' }),
    )
    await waitFor(() => expect(result.current.hasMore).toBe(true))
    act(() => {
      void result.current.loadMore()
    })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    const appendSignal = requestSignal(1)

    await act(async () => result.current.refresh())
    expect(appendSignal.aborted).toBe(true)
    expect(result.current.tickets).toEqual([ticket('9')])

    await act(async () => {
      append.resolve(jsonResponse(page([ticket('2')])))
    })
    expect(result.current.tickets).toEqual([ticket('9')])
  })

  it('does not let a stale append release a newer append guard', async () => {
    const staleAppend = deferred<Response>()
    const activeAppend = deferred<Response>()
    vi.mocked(fetch)
      .mockResolvedValueOnce(
        jsonResponse(
          page([ticket('1')], { nextCursor: 'old-cursor', hasMore: true }),
        ),
      )
      .mockReturnValueOnce(staleAppend.promise)
      .mockResolvedValueOnce(
        jsonResponse(
          page([ticket('9')], { nextCursor: 'new-cursor', hasMore: true }),
        ),
      )
      .mockReturnValueOnce(activeAppend.promise)

    const { result } = renderHook(() =>
      useTickets({ query: '', status: 'All' }),
    )
    await waitFor(() => expect(result.current.hasMore).toBe(true))
    act(() => {
      void result.current.loadMore()
    })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))

    await act(async () => result.current.refresh())
    act(() => {
      void result.current.loadMore()
    })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(4))

    await act(async () => {
      staleAppend.resolve(jsonResponse(page([ticket('2')])))
    })
    act(() => {
      void result.current.loadMore()
    })
    await act(async () => {
      await Promise.resolve()
    })

    expect(fetch).toHaveBeenCalledTimes(4)

    await act(async () => {
      activeAppend.resolve(jsonResponse(page([ticket('10')])))
    })
  })

  it('aborts the active request on unmount', async () => {
    const pending = deferred<Response>()
    vi.mocked(fetch).mockReturnValueOnce(pending.promise)

    const { unmount } = renderHook(() =>
      useTickets({ query: '', status: 'All' }),
    )
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    const signal = requestSignal(0)

    unmount()

    expect(signal.aborted).toBe(true)
  })
})
