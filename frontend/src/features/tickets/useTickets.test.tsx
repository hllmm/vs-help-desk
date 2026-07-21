import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { TicketListItem } from '../../api/types'
import { useTickets } from './useTickets'

const fetchTickets = vi.hoisted(() => vi.fn())

vi.mock('../../api/ticketsApi', () => ({
  fetchTickets,
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const sampleTickets: TicketListItem[] = [
  {
    id: '1',
    ticketNumber: 'VS-000001',
    subject: 'İlk talep',
    customerName: 'Ayşe',
    customerEmail: 'ayse@example.com',
    status: 'New',
    lastActivityAt: '2026-07-20T09:00:00.000Z',
    assignedUserId: null,
  },
]

const refreshedTickets: TicketListItem[] = [
  {
    ...sampleTickets[0]!,
    id: '2',
    ticketNumber: 'VS-000002',
    subject: 'İkinci talep',
  },
]

describe('useTickets', () => {
  beforeEach(() => {
    fetchTickets.mockReset()
  })

  it('moves from initial loading to loaded data', async () => {
    const pending = deferred<TicketListItem[]>()
    fetchTickets.mockReturnValueOnce(pending.promise)

    const { result } = renderHook(() => useTickets())

    expect(result.current.isInitialLoading).toBe(true)
    expect(result.current.isRefreshing).toBe(false)
    expect(result.current.hasLoaded).toBe(false)
    expect(result.current.tickets).toEqual([])
    expect(result.current.error).toBeNull()

    await act(async () => {
      pending.resolve(sampleTickets)
    })

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    expect(result.current.isInitialLoading).toBe(false)
    expect(result.current.isRefreshing).toBe(false)
    expect(result.current.tickets).toEqual(sampleTickets)
    expect(result.current.error).toBeNull()
  })

  it('treats an empty response as a completed load', async () => {
    fetchTickets.mockResolvedValueOnce([])

    const { result } = renderHook(() => useTickets())

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    expect(result.current.tickets).toEqual([])
    expect(result.current.isInitialLoading).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('preserves old rows while refreshing', async () => {
    fetchTickets.mockResolvedValueOnce(sampleTickets)

    const { result } = renderHook(() => useTickets())

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    const refreshPending = deferred<TicketListItem[]>()
    fetchTickets.mockReturnValueOnce(refreshPending.promise)

    let refreshPromise: Promise<void>
    act(() => {
      refreshPromise = result.current.refresh()
    })

    await waitFor(() => {
      expect(result.current.isRefreshing).toBe(true)
    })

    expect(result.current.isInitialLoading).toBe(false)
    expect(result.current.tickets).toEqual(sampleTickets)

    await act(async () => {
      refreshPending.resolve(refreshedTickets)
      await refreshPromise!
    })

    expect(result.current.tickets).toEqual(refreshedTickets)
    expect(result.current.isRefreshing).toBe(false)
  })

  it('aborts the previous request before refresh', async () => {
    const first = deferred<TicketListItem[]>()
    fetchTickets.mockReturnValueOnce(first.promise)

    const { result } = renderHook(() => useTickets())

    await waitFor(() => {
      expect(fetchTickets).toHaveBeenCalledTimes(1)
    })

    const firstSignal = fetchTickets.mock.calls[0]?.[0]?.signal as AbortSignal
    expect(firstSignal.aborted).toBe(false)

    const second = deferred<TicketListItem[]>()
    fetchTickets.mockReturnValueOnce(second.promise)

    act(() => {
      void result.current.refresh()
    })

    await waitFor(() => {
      expect(fetchTickets).toHaveBeenCalledTimes(2)
    })

    expect(firstSignal.aborted).toBe(true)
    const secondSignal = fetchTickets.mock.calls[1]?.[0]?.signal as
      | AbortSignal
      | undefined
    expect(secondSignal?.aborted).toBe(false)

    await act(async () => {
      second.resolve(sampleTickets)
    })
  })

  it('prevents a stale promise that ignores abort from committing', async () => {
    const first = deferred<TicketListItem[]>()
    fetchTickets.mockReturnValueOnce(first.promise)

    const { result } = renderHook(() => useTickets())

    await waitFor(() => {
      expect(fetchTickets).toHaveBeenCalledTimes(1)
    })

    const second = deferred<TicketListItem[]>()
    fetchTickets.mockReturnValueOnce(second.promise)

    act(() => {
      void result.current.refresh()
    })

    await waitFor(() => {
      expect(fetchTickets).toHaveBeenCalledTimes(2)
    })

    await act(async () => {
      second.resolve(refreshedTickets)
    })

    await waitFor(() => {
      expect(result.current.tickets).toEqual(refreshedTickets)
    })

    await act(async () => {
      first.resolve(sampleTickets)
    })

    expect(result.current.tickets).toEqual(refreshedTickets)
  })

  it('aborts and invalidates sequence on unmount', async () => {
    const pending = deferred<TicketListItem[]>()
    fetchTickets.mockReturnValueOnce(pending.promise)

    const { result, unmount } = renderHook(() => useTickets())

    await waitFor(() => {
      expect(fetchTickets).toHaveBeenCalledTimes(1)
    })

    const signal = fetchTickets.mock.calls[0]?.[0]?.signal as AbortSignal
    unmount()

    expect(signal.aborted).toBe(true)

    await act(async () => {
      pending.resolve(sampleTickets)
    })

    // Unmounted: no further assertions on result state, but resolve must not throw.
    expect(result.current.tickets).toEqual([])
  })

  it('classifies TypeError as network and ApiError as server', async () => {
    fetchTickets.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    const { result, rerender } = renderHook(() => useTickets())

    await waitFor(() => {
      expect(result.current.error).toBe('network')
    })
    expect(result.current.hasLoaded).toBe(true)
    expect(result.current.isInitialLoading).toBe(false)

    fetchTickets.mockRejectedValueOnce(new ApiError(500, 'Server exploded'))

    await act(async () => {
      await result.current.refresh()
    })
    rerender()

    await waitFor(() => {
      expect(result.current.error).toBe('server')
    })
  })

  it('does not expose protected 401 as a page error', async () => {
    fetchTickets.mockRejectedValueOnce(new ApiError(401, 'Unauthorized'))

    const { result } = renderHook(() => useTickets())

    await waitFor(() => {
      expect(fetchTickets).toHaveBeenCalled()
    })

    // Allow microtasks from the catch path to settle.
    await act(async () => {
      await Promise.resolve()
    })

    expect(result.current.error).toBeNull()
    expect(result.current.isInitialLoading).toBe(true)
    expect(result.current.hasLoaded).toBe(false)
  })
})
