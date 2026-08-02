import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { ResolveTicketResult, TicketDetails } from '../../api/types'
import { useTicketDetails } from './useTicketDetails'

const fetchTicketDetails = vi.hoisted(() => vi.fn())
const fetchTicketMessages = vi.hoisted(() => vi.fn())

vi.mock('../../api/ticketsApi', () => ({
  fetchTicketDetails,
  fetchTicketMessages,
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
  nextMessageCursor: null,
  hasMoreMessages: false,
}

const refreshedDetail: TicketDetails = {
  ...sampleDetail,
  subject: 'Güncellenmiş talep',
  status: 'CustomerReplied',
}

describe('useTicketDetails', () => {
  beforeEach(() => {
    fetchTicketDetails.mockReset()
    fetchTicketMessages.mockReset()
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

  function sampleResolveResult(
    overrides: Partial<ResolveTicketResult> = {},
  ): ResolveTicketResult {
    return {
      ticketId: 'ticket-1',
      ticketNumber: 'VS-000001',
      status: 'Resolved',
      resolvedAt: '2026-07-20T12:00:00.000Z',
      updatedAt: '2026-07-20T12:05:00.000Z',
      lastActivityAt: '2026-07-20T12:05:00.000Z',
      closedByUserId: 'user-closer',
      changed: true,
      ...overrides,
    }
  }

  it('applyResolvedTicket patches matching detail fields and clears waitingCustomerSince', async () => {
    fetchTicketDetails.mockResolvedValueOnce({
      ...sampleDetail,
      waitingCustomerSince: '2026-07-19T09:00:00.000Z',
      assignedUserId: 'assignee-1',
      messages: [
        {
          id: 'msg-1',
          senderType: 'Customer',
          userId: null,
          content: 'Merhaba',
          isHtml: false,
          createdAt: '2026-07-20T09:05:00.000Z',
        },
      ],
      attachments: [
        {
          id: 'att-1',
          ticketMessageId: 'msg-1',
          fileName: 'ekran.png',
          contentType: 'image/png',
          fileSize: 10,
          createdAt: '2026-07-20T09:05:01.000Z',
        },
      ],
    })

    const { result } = renderHook(() => useTicketDetails('ticket-1'))
    await waitFor(() => {
      expect(result.current.detail).not.toBeNull()
    })

    const resolveResult = sampleResolveResult()
    act(() => {
      result.current.applyResolvedTicket(resolveResult)
    })

    expect(result.current.detail).toMatchObject({
      id: 'ticket-1',
      subject: 'İlk talep',
      customerName: 'Ayşe',
      customerEmail: 'ayse@example.com',
      assignedUserId: 'assignee-1',
      createdAt: sampleDetail.createdAt,
      status: 'Resolved',
      resolvedAt: resolveResult.resolvedAt,
      updatedAt: resolveResult.updatedAt,
      lastActivityAt: resolveResult.lastActivityAt,
      closedByUserId: resolveResult.closedByUserId,
      waitingCustomerSince: null,
    })
    expect(result.current.detail?.messages).toHaveLength(1)
    expect(result.current.detail?.attachments).toHaveLength(1)
    expect(result.current.detail?.messages[0]?.content).toBe('Merhaba')
  })

  it('applyResolvedTicket ignores mismatched ticket ids', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail)
    const { result } = renderHook(() => useTicketDetails('ticket-1'))
    await waitFor(() => {
      expect(result.current.detail).toEqual(sampleDetail)
    })

    act(() => {
      result.current.applyResolvedTicket(
        sampleResolveResult({ ticketId: 'other-ticket' }),
      )
    })

    expect(result.current.detail).toEqual(sampleDetail)
  })

  it('cannot resurrect an old route after the route id changes', async () => {
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
    fetchTicketDetails.mockResolvedValueOnce(nextDetail)
    rerender({ id: 'ticket-2' })

    await waitFor(() => {
      expect(result.current.detail).toEqual(nextDetail)
    })

    act(() => {
      result.current.applyResolvedTicket(
        sampleResolveResult({ ticketId: 'ticket-1' }),
      )
    })

    expect(result.current.detail).toEqual(nextDetail)
    expect(result.current.detail?.status).toBe('New')
  })

  it('coexists with an active refresh whose later response replaces the patch', async () => {
    fetchTicketDetails.mockResolvedValueOnce({
      ...sampleDetail,
      waitingCustomerSince: '2026-07-19T09:00:00.000Z',
    })
    const { result } = renderHook(() => useTicketDetails('ticket-1'))
    await waitFor(() => {
      expect(result.current.detail).not.toBeNull()
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

    act(() => {
      result.current.applyResolvedTicket(sampleResolveResult())
    })

    expect(result.current.detail?.status).toBe('Resolved')
    expect(result.current.detail?.waitingCustomerSince).toBeNull()

    const authoritative: TicketDetails = {
      ...sampleDetail,
      status: 'CustomerReplied',
      subject: 'Yetkili yenileme',
      waitingCustomerSince: null,
      resolvedAt: null,
      closedByUserId: null,
    }

    await act(async () => {
      refreshPending.resolve(authoritative)
      await refreshPromise!
    })

    expect(result.current.detail).toEqual(authoritative)
  })

  it('preserves the server-confirmed patch when a later refresh fails', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail)
    const { result } = renderHook(() => useTicketDetails('ticket-1'))
    await waitFor(() => {
      expect(result.current.detail).toEqual(sampleDetail)
    })

    const resolveResult = sampleResolveResult()
    act(() => {
      result.current.applyResolvedTicket(resolveResult)
    })
    expect(result.current.detail?.status).toBe('Resolved')

    fetchTicketDetails.mockRejectedValueOnce(new TypeError('offline'))
    await act(async () => {
      await result.current.refresh()
    })

    await waitFor(() => {
      expect(result.current.error).toEqual({ kind: 'network' })
    })
    expect(result.current.detail?.status).toBe('Resolved')
    expect(result.current.detail?.resolvedAt).toBe(resolveResult.resolvedAt)
    expect(result.current.detail?.closedByUserId).toBe(
      resolveResult.closedByUserId,
    )
  })

  it('does not invent a patch when no resolve result is supplied', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail)
    const { result } = renderHook(() => useTicketDetails('ticket-1'))
    await waitFor(() => {
      expect(result.current.detail).toEqual(sampleDetail)
    })

    // 409 / network outcomes supply no result to applyResolvedTicket.
    expect(result.current.detail).toEqual(sampleDetail)
  })

  describe('older message history', () => {
    const pagedDetail: TicketDetails = {
      ...sampleDetail,
      messages: [
        {
          id: 'msg-3',
          senderType: 'Customer',
          userId: null,
          content: 'Üçüncü mesaj',
          isHtml: false,
          createdAt: '2026-07-20T09:30:00.000Z',
        },
        {
          id: 'msg-4',
          senderType: 'Support',
          userId: 'user-1',
          content: 'Dördüncü mesaj',
          isHtml: false,
          createdAt: '2026-07-20T09:45:00.000Z',
        },
      ],
      attachments: [
        {
          id: 'att-4',
          ticketMessageId: 'msg-4',
          fileName: 'son.png',
          contentType: 'image/png',
          fileSize: 10,
          createdAt: '2026-07-20T09:45:01.000Z',
        },
      ],
      nextMessageCursor: 'cursor-1',
      hasMoreMessages: true,
    }

    const olderPage = {
      messages: [
        {
          id: 'msg-1',
          senderType: 'Customer',
          userId: null,
          content: 'Birinci mesaj',
          isHtml: false,
          createdAt: '2026-07-20T09:00:00.000Z',
        },
        {
          id: 'msg-2',
          senderType: 'Support',
          userId: 'user-1',
          content: 'İkinci mesaj',
          isHtml: false,
          createdAt: '2026-07-20T09:15:00.000Z',
        },
        {
          // Overlap with the loaded first page: must be skipped, not re-applied.
          id: 'msg-3',
          senderType: 'Customer',
          userId: null,
          content: 'Üçüncü mesaj (sunucu kopyası)',
          isHtml: false,
          createdAt: '2026-07-20T09:30:00.000Z',
        },
      ],
      attachments: [
        {
          id: 'att-1',
          ticketMessageId: 'msg-1',
          fileName: 'ilk.png',
          contentType: 'image/png',
          fileSize: 5,
          createdAt: '2026-07-20T09:00:01.000Z',
        },
        {
          id: 'att-4',
          ticketMessageId: 'msg-4',
          fileName: 'son.png',
          contentType: 'image/png',
          fileSize: 10,
          createdAt: '2026-07-20T09:45:01.000Z',
        },
      ],
      nextCursor: 'cursor-2',
      hasMore: true,
    }

    it('exposes the bounded first page without fetching older history', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail)

      const { result } = renderHook(() => useTicketDetails('ticket-1'))

      await waitFor(() => {
        expect(result.current.detail).not.toBeNull()
      })

      expect(
        result.current.detail?.messages.map(({ id }) => id),
      ).toEqual(['msg-3', 'msg-4'])
      expect(result.current.detail?.hasMoreMessages).toBe(true)
      expect(result.current.detail?.nextMessageCursor).toBe('cursor-1')
      expect(result.current.isLoadingOlder).toBe(false)
      expect(result.current.olderMessagesError).toBeNull()
      expect(fetchTicketMessages).not.toHaveBeenCalled()
    })

    it('requests the next older page with the active cursor', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail)
      const { result } = renderHook(() => useTicketDetails('ticket-1'))
      await waitFor(() => {
        expect(result.current.detail).not.toBeNull()
      })

      const pending = deferred<typeof olderPage>()
      fetchTicketMessages.mockReturnValueOnce(pending.promise)

      let loadPromise: Promise<void>
      act(() => {
        loadPromise = result.current.loadOlderMessages()
      })

      await waitFor(() => {
        expect(result.current.isLoadingOlder).toBe(true)
      })
      expect(fetchTicketMessages).toHaveBeenCalledTimes(1)
      expect(fetchTicketMessages).toHaveBeenCalledWith('ticket-1', {
        pageSize: 100,
        cursor: 'cursor-1',
        signal: expect.any(AbortSignal),
      })

      await act(async () => {
        pending.resolve(olderPage)
        await loadPromise!
      })

      expect(result.current.isLoadingOlder).toBe(false)
    })

    it('prepends older pages chronologically and deduplicates messages and attachments by id', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail)
      const { result } = renderHook(() => useTicketDetails('ticket-1'))
      await waitFor(() => {
        expect(result.current.detail).not.toBeNull()
      })

      fetchTicketMessages.mockResolvedValueOnce(olderPage)
      await act(async () => {
        await result.current.loadOlderMessages()
      })

      expect(
        result.current.detail?.messages.map(({ id }) => id),
      ).toEqual(['msg-1', 'msg-2', 'msg-3', 'msg-4'])
      expect(
        result.current.detail?.messages.map(({ createdAt }) => createdAt),
      ).toEqual([
        '2026-07-20T09:00:00.000Z',
        '2026-07-20T09:15:00.000Z',
        '2026-07-20T09:30:00.000Z',
        '2026-07-20T09:45:00.000Z',
      ])
      expect(
        result.current.detail?.messages.find(({ id }) => id === 'msg-3')
          ?.content,
      ).toBe('Üçüncü mesaj')
      expect(
        result.current.detail?.attachments.map(({ id }) => id),
      ).toEqual(['att-1', 'att-4'])
      expect(result.current.olderMessagesError).toBeNull()
    })

    it('advances the cursor from each page response and stops at the end', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail)
      const { result } = renderHook(() => useTicketDetails('ticket-1'))
      await waitFor(() => {
        expect(result.current.detail).not.toBeNull()
      })

      fetchTicketMessages.mockResolvedValueOnce(olderPage)
      await act(async () => {
        await result.current.loadOlderMessages()
      })

      expect(result.current.detail?.nextMessageCursor).toBe('cursor-2')
      expect(result.current.detail?.hasMoreMessages).toBe(true)

      const finalPage = {
        messages: [
          {
            id: 'msg-0',
            senderType: 'Customer',
            userId: null,
            content: 'En eski mesaj',
            isHtml: false,
            createdAt: '2026-07-20T08:30:00.000Z',
          },
        ],
        attachments: [],
        nextCursor: null,
        hasMore: false,
      }
      fetchTicketMessages.mockResolvedValueOnce(finalPage)
      await act(async () => {
        await result.current.loadOlderMessages()
      })

      expect(fetchTicketMessages).toHaveBeenLastCalledWith('ticket-1', {
        pageSize: 100,
        cursor: 'cursor-2',
        signal: expect.any(AbortSignal),
      })
      expect(
        result.current.detail?.messages.map(({ id }) => id),
      ).toEqual(['msg-0', 'msg-1', 'msg-2', 'msg-3', 'msg-4'])
      expect(result.current.detail?.nextMessageCursor).toBeNull()
      expect(result.current.detail?.hasMoreMessages).toBe(false)

      await act(async () => {
        await result.current.loadOlderMessages()
      })
      expect(fetchTicketMessages).toHaveBeenCalledTimes(2)
    })

    it('preserves the loaded timeline on an older-page failure and retries it', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail)
      const { result } = renderHook(() => useTicketDetails('ticket-1'))
      await waitFor(() => {
        expect(result.current.detail).not.toBeNull()
      })

      fetchTicketMessages.mockRejectedValueOnce(new TypeError('Failed to fetch'))
      await act(async () => {
        await result.current.loadOlderMessages()
      })

      expect(result.current.olderMessagesError).toEqual({ kind: 'network' })
      expect(result.current.isLoadingOlder).toBe(false)
      expect(
        result.current.detail?.messages.map(({ id }) => id),
      ).toEqual(['msg-3', 'msg-4'])
      expect(result.current.error).toBeNull()

      fetchTicketMessages.mockResolvedValueOnce(olderPage)
      await act(async () => {
        await result.current.loadOlderMessages()
      })

      expect(result.current.olderMessagesError).toBeNull()
      expect(
        result.current.detail?.messages.map(({ id }) => id),
      ).toEqual(['msg-1', 'msg-2', 'msg-3', 'msg-4'])
    })

    it('classifies older-page server failures and keeps the workspace intact', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail)
      const { result } = renderHook(() => useTicketDetails('ticket-1'))
      await waitFor(() => {
        expect(result.current.detail).not.toBeNull()
      })

      fetchTicketMessages.mockRejectedValueOnce(new ApiError(500, 'boom'))
      await act(async () => {
        await result.current.loadOlderMessages()
      })

      expect(result.current.olderMessagesError).toEqual({ kind: 'server' })
      expect(result.current.detail?.ticketNumber).toBe('VS-000001')
      expect(
        result.current.detail?.messages.map(({ id }) => id),
      ).toEqual(['msg-3', 'msg-4'])
    })

    it('issues a single request for repeated triggers while an older page loads', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail)
      const { result } = renderHook(() => useTicketDetails('ticket-1'))
      await waitFor(() => {
        expect(result.current.detail).not.toBeNull()
      })

      const pending = deferred<typeof olderPage>()
      fetchTicketMessages.mockReturnValueOnce(pending.promise)

      let first: Promise<void>
      let second: Promise<void>
      act(() => {
        first = result.current.loadOlderMessages()
        second = result.current.loadOlderMessages()
      })

      await waitFor(() => {
        expect(fetchTicketMessages).toHaveBeenCalledTimes(1)
      })
      await waitFor(() => {
        expect(result.current.isLoadingOlder).toBe(true)
      })

      await act(async () => {
        pending.resolve(olderPage)
        await Promise.all([first!, second!])
      })

      expect(result.current.isLoadingOlder).toBe(false)
      expect(
        result.current.detail?.messages.map(({ id }) => id),
      ).toEqual(['msg-1', 'msg-2', 'msg-3', 'msg-4'])
    })

    it('aborts an in-flight older request when the route ticket id changes', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail)
      const { result, rerender } = renderHook(
        ({ id }: { id: string | undefined }) => useTicketDetails(id),
        { initialProps: { id: 'ticket-1' } },
      )
      await waitFor(() => {
        expect(result.current.detail).not.toBeNull()
      })

      const pending = deferred<typeof olderPage>()
      fetchTicketMessages.mockReturnValueOnce(pending.promise)
      act(() => {
        void result.current.loadOlderMessages()
      })
      await waitFor(() => {
        expect(fetchTicketMessages).toHaveBeenCalledTimes(1)
      })
      const olderSignal = fetchTicketMessages.mock.calls[0]?.[1]?.signal as
        | AbortSignal
        | undefined

      const nextDetail: TicketDetails = {
        ...sampleDetail,
        id: 'ticket-2',
        ticketNumber: 'VS-000002',
        messages: [
          {
            id: 'msg-9',
            senderType: 'Customer',
            userId: null,
            content: 'İkinci talebin mesajı',
            isHtml: false,
            createdAt: '2026-07-21T09:00:00.000Z',
          },
        ],
      }
      fetchTicketDetails.mockResolvedValueOnce(nextDetail)
      rerender({ id: 'ticket-2' })

      await waitFor(() => {
        expect(result.current.detail?.id).toBe('ticket-2')
      })
      expect(olderSignal?.aborted).toBe(true)

      await act(async () => {
        pending.resolve(olderPage)
      })

      expect(result.current.detail?.messages.map(({ id }) => id)).toEqual([
        'msg-9',
      ])
      expect(result.current.isLoadingOlder).toBe(false)
    })

    it('aborts an in-flight older request on unmount', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail)
      const { result, unmount } = renderHook(() => useTicketDetails('ticket-1'))
      await waitFor(() => {
        expect(result.current.detail).not.toBeNull()
      })

      const pending = deferred<typeof olderPage>()
      fetchTicketMessages.mockReturnValueOnce(pending.promise)
      act(() => {
        void result.current.loadOlderMessages()
      })
      await waitFor(() => {
        expect(fetchTicketMessages).toHaveBeenCalledTimes(1)
      })
      const olderSignal = fetchTicketMessages.mock.calls[0]?.[1]?.signal as
        | AbortSignal
        | undefined

      unmount()

      expect(olderSignal?.aborted).toBe(true)
      await act(async () => {
        pending.resolve(olderPage)
      })
    })

    it('refresh replaces accumulated older history with the server first page', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail)
      const { result } = renderHook(() => useTicketDetails('ticket-1'))
      await waitFor(() => {
        expect(result.current.detail).not.toBeNull()
      })

      fetchTicketMessages.mockResolvedValueOnce(olderPage)
      await act(async () => {
        await result.current.loadOlderMessages()
      })
      expect(
        result.current.detail?.messages.map(({ id }) => id),
      ).toEqual(['msg-1', 'msg-2', 'msg-3', 'msg-4'])

      const freshFirstPage: TicketDetails = {
        ...sampleDetail,
        messages: [
          {
            id: 'msg-10',
            senderType: 'Support',
            userId: 'user-1',
            content: 'Yeni ilk sayfa',
            isHtml: false,
            createdAt: '2026-07-21T10:00:00.000Z',
          },
        ],
        attachments: [],
        nextMessageCursor: 'cursor-9',
        hasMoreMessages: true,
      }
      const refreshPending = deferred<TicketDetails>()
      fetchTicketDetails.mockReturnValueOnce(refreshPending.promise)

      let refreshPromise: Promise<void>
      act(() => {
        refreshPromise = result.current.refresh()
      })

      await waitFor(() => {
        expect(result.current.isRefreshing).toBe(true)
      })
      expect(
        result.current.detail?.messages.map(({ id }) => id),
      ).toEqual(['msg-1', 'msg-2', 'msg-3', 'msg-4'])

      await act(async () => {
        refreshPending.resolve(freshFirstPage)
        await refreshPromise!
      })

      expect(result.current.detail?.messages.map(({ id }) => id)).toEqual([
        'msg-10',
      ])
      expect(result.current.detail?.nextMessageCursor).toBe('cursor-9')
      expect(result.current.detail?.hasMoreMessages).toBe(true)
    })

    it('ignores an older response whose cursor is no longer current', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail)
      const { result } = renderHook(() => useTicketDetails('ticket-1'))
      await waitFor(() => {
        expect(result.current.detail).not.toBeNull()
      })

      const pending = deferred<typeof olderPage>()
      fetchTicketMessages.mockReturnValueOnce(pending.promise)
      act(() => {
        void result.current.loadOlderMessages()
      })
      await waitFor(() => {
        expect(fetchTicketMessages).toHaveBeenCalledTimes(1)
      })

      const freshFirstPage: TicketDetails = {
        ...sampleDetail,
        messages: [
          {
            id: 'msg-10',
            senderType: 'Support',
            userId: 'user-1',
            content: 'Yeni ilk sayfa',
            isHtml: false,
            createdAt: '2026-07-21T10:00:00.000Z',
          },
        ],
        attachments: [],
        nextMessageCursor: 'cursor-9',
        hasMoreMessages: true,
      }
      fetchTicketDetails.mockResolvedValueOnce(freshFirstPage)
      await act(async () => {
        await result.current.refresh()
      })
      await waitFor(() => {
        expect(result.current.detail?.nextMessageCursor).toBe('cursor-9')
      })

      await act(async () => {
        pending.resolve(olderPage)
      })

      expect(result.current.detail?.messages.map(({ id }) => id)).toEqual([
        'msg-10',
      ])
      expect(result.current.detail?.nextMessageCursor).toBe('cursor-9')
    })
  })
})
