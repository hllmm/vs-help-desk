import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { TicketDetails } from '../../api/types'
import { useTicketDetails } from './useTicketDetails'

const fetchTicketDetails = vi.hoisted(() => vi.fn())

vi.mock('../../api/ticketsApi', () => ({
  fetchTicketDetails,
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

const sampleDetail: TicketDetails = {
  id: 'ticket-1',
  ticketNumber: 'VS-000001',
  subject: 'İlk talep',
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

const refreshedDetail: TicketDetails = {
  ...sampleDetail,
  subject: 'Güncellenmiş talep',
  status: 'CustomerReplied',
}

describe('useTicketDetails', () => {
  beforeEach(() => {
    fetchTicketDetails.mockReset()
  })

  it('moves from initial loading to ready detail', async () => {
    const pending = deferred<TicketDetails>()
    fetchTicketDetails.mockReturnValueOnce(pending.promise)

    const { result } = renderHook(() => useTicketDetails('ticket-1'))

    expect(result.current.isInitialLoading).toBe(true)
    expect(result.current.isRefreshing).toBe(false)
    expect(result.current.hasLoaded).toBe(false)
    expect(result.current.detail).toBeNull()
    expect(result.current.error).toBeNull()

    await act(async () => {
      pending.resolve(sampleDetail)
    })

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    expect(result.current.isInitialLoading).toBe(false)
    expect(result.current.isRefreshing).toBe(false)
    expect(result.current.detail).toEqual(sampleDetail)
    expect(result.current.error).toBeNull()
    expect(fetchTicketDetails).toHaveBeenCalledWith('ticket-1', {
      signal: expect.any(AbortSignal),
    })
  })

  it('classifies 404 as not-found, clears detail, and sets hasLoaded', async () => {
    fetchTicketDetails.mockRejectedValueOnce(new ApiError(404, 'missing'))

    const { result } = renderHook(() => useTicketDetails('missing'))

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    expect(result.current.detail).toBeNull()
    expect(result.current.error).toEqual({ kind: 'not-found' })
    expect(result.current.isInitialLoading).toBe(false)
  })

  it('classifies TypeError as network and 5xx as server', async () => {
    fetchTicketDetails.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    const { result } = renderHook(() => useTicketDetails('ticket-1'))

    await waitFor(() => {
      expect(result.current.error).toEqual({ kind: 'network' })
    })
    expect(result.current.hasLoaded).toBe(true)

    fetchTicketDetails.mockRejectedValueOnce(new ApiError(500, 'boom'))

    await act(async () => {
      await result.current.refresh()
    })

    await waitFor(() => {
      expect(result.current.error).toEqual({ kind: 'server' })
    })
  })

  it('preserves old detail while refreshing and after a failed refresh', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail)

    const { result } = renderHook(() => useTicketDetails('ticket-1'))

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    const refreshPending = deferred<TicketDetails>()
    fetchTicketDetails.mockReturnValueOnce(refreshPending.promise)

    let refreshPromise: Promise<void>
    act(() => {
      refreshPromise = result.current.refresh()
    })

    await waitFor(() => {
      expect(result.current.isRefreshing).toBe(true)
    })

    expect(result.current.isInitialLoading).toBe(false)
    expect(result.current.detail).toEqual(sampleDetail)

    await act(async () => {
      refreshPending.resolve(refreshedDetail)
      await refreshPromise!
    })

    expect(result.current.detail).toEqual(refreshedDetail)
    expect(result.current.isRefreshing).toBe(false)

    fetchTicketDetails.mockRejectedValueOnce(new TypeError('offline'))

    await act(async () => {
      await result.current.refresh()
    })

    await waitFor(() => {
      expect(result.current.error).toEqual({ kind: 'network' })
    })
    expect(result.current.detail).toEqual(refreshedDetail)
    expect(result.current.hasLoaded).toBe(true)
    expect(result.current.isRefreshing).toBe(false)
  })

  it('aborts the previous request before a new one', async () => {
    const first = deferred<TicketDetails>()
    fetchTicketDetails.mockReturnValueOnce(first.promise)

    const { result } = renderHook(() => useTicketDetails('ticket-1'))

    await waitFor(() => {
      expect(fetchTicketDetails).toHaveBeenCalledTimes(1)
    })

    const firstSignal = fetchTicketDetails.mock.calls[0]?.[1]?.signal as AbortSignal
    expect(firstSignal.aborted).toBe(false)

    const second = deferred<TicketDetails>()
    fetchTicketDetails.mockReturnValueOnce(second.promise)

    act(() => {
      void result.current.refresh()
    })

    await waitFor(() => {
      expect(fetchTicketDetails).toHaveBeenCalledTimes(2)
    })

    expect(firstSignal.aborted).toBe(true)
    const secondSignal = fetchTicketDetails.mock.calls[1]?.[1]?.signal as
      | AbortSignal
      | undefined
    expect(secondSignal?.aborted).toBe(false)

    await act(async () => {
      second.resolve(sampleDetail)
    })
  })

  it('prevents a stale promise that ignores abort from committing', async () => {
    const first = deferred<TicketDetails>()
    fetchTicketDetails.mockReturnValueOnce(first.promise)

    const { result } = renderHook(() => useTicketDetails('ticket-1'))

    await waitFor(() => {
      expect(fetchTicketDetails).toHaveBeenCalledTimes(1)
    })

    const second = deferred<TicketDetails>()
    fetchTicketDetails.mockReturnValueOnce(second.promise)

    act(() => {
      void result.current.refresh()
    })

    await waitFor(() => {
      expect(fetchTicketDetails).toHaveBeenCalledTimes(2)
    })

    await act(async () => {
      second.resolve(refreshedDetail)
    })

    await waitFor(() => {
      expect(result.current.detail).toEqual(refreshedDetail)
    })

    await act(async () => {
      first.resolve(sampleDetail)
    })

    expect(result.current.detail).toEqual(refreshedDetail)
  })

  it('aborts and invalidates sequence on unmount', async () => {
    const pending = deferred<TicketDetails>()
    fetchTicketDetails.mockReturnValueOnce(pending.promise)

    const { result, unmount } = renderHook(() => useTicketDetails('ticket-1'))

    await waitFor(() => {
      expect(fetchTicketDetails).toHaveBeenCalledTimes(1)
    })

    const signal = fetchTicketDetails.mock.calls[0]?.[1]?.signal as AbortSignal
    unmount()

    expect(signal.aborted).toBe(true)

    await act(async () => {
      pending.resolve(sampleDetail)
    })

    expect(result.current.detail).toBeNull()
  })

  it('reloads when the route ticket id changes', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail)

    const { result, rerender } = renderHook(
      ({ id }: { id: string | undefined }) => useTicketDetails(id),
      { initialProps: { id: 'ticket-1' } },
    )

    await waitFor(() => {
      expect(result.current.detail).toEqual(sampleDetail)
    })

    const nextDetail: TicketDetails = {
      ...sampleDetail,
      id: 'ticket-2',
      ticketNumber: 'VS-000002',
      subject: 'İkinci talep',
    }
    const pending = deferred<TicketDetails>()
    fetchTicketDetails.mockReturnValueOnce(pending.promise)

    rerender({ id: 'ticket-2' })

    await waitFor(() => {
      expect(fetchTicketDetails).toHaveBeenLastCalledWith('ticket-2', {
        signal: expect.any(AbortSignal),
      })
    })

    expect(result.current.detail).toBeNull()
    expect(result.current.isInitialLoading).toBe(true)
    expect(result.current.hasLoaded).toBe(false)

    await act(async () => {
      pending.resolve(nextDetail)
    })

    await waitFor(() => {
      expect(result.current.detail).toEqual(nextDetail)
    })
  })

  it('does not expose protected 401 as a page error', async () => {
    fetchTicketDetails.mockRejectedValueOnce(new ApiError(401, 'Unauthorized'))

    const { result } = renderHook(() => useTicketDetails('ticket-1'))

    await waitFor(() => {
      expect(fetchTicketDetails).toHaveBeenCalled()
    })

    await act(async () => {
      await Promise.resolve()
    })

    expect(result.current.error).toBeNull()
    expect(result.current.isInitialLoading).toBe(true)
    expect(result.current.hasLoaded).toBe(false)
  })

  it('performs no request for empty or undefined route ids and settles as not found', async () => {
    const { result, rerender } = renderHook(
      ({ id }: { id: string | undefined }) => useTicketDetails(id),
      { initialProps: { id: undefined as string | undefined } },
    )

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    expect(fetchTicketDetails).not.toHaveBeenCalled()
    expect(result.current.detail).toBeNull()
    expect(result.current.error).toEqual({ kind: 'not-found' })
    expect(result.current.isInitialLoading).toBe(false)

    rerender({ id: '   ' })

    await waitFor(() => {
      expect(result.current.error).toEqual({ kind: 'not-found' })
    })
    expect(fetchTicketDetails).not.toHaveBeenCalled()
  })
})
