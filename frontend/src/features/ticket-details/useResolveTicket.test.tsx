import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { ResolveTicketResult } from '../../api/types'
import {
  useResolveTicket,
  type ResolveTicketOutcome,
} from './useResolveTicket'

const resolveTicket = vi.hoisted(() => vi.fn())

vi.mock('../../api/ticketsApi', () => ({
  resolveTicket,
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

function sampleResult(
  overrides: Partial<ResolveTicketResult> = {},
): ResolveTicketResult {
  return {
    ticketId: 'ticket-1',
    ticketNumber: 'VS-000001',
    status: 'Resolved',
    resolvedAt: '2026-07-20T12:00:00.000Z',
    updatedAt: '2026-07-20T12:00:00.000Z',
    lastActivityAt: '2026-07-20T12:00:00.000Z',
    closedByUserId: 'user-1',
    changed: true,
    ...overrides,
  }
}

describe('useResolveTicket', () => {
  beforeEach(() => {
    resolveTicket.mockReset()
  })

  it('maps 200 changed=true to resolved with result', async () => {
    resolveTicket.mockResolvedValueOnce(sampleResult({ changed: true }))
    const { result } = renderHook(() => useResolveTicket('ticket-1'))

    let outcome: ResolveTicketOutcome | null | undefined
    await act(async () => {
      outcome = await result.current.resolve()
    })

    expect(outcome).toEqual({
      kind: 'resolved',
      result: sampleResult({ changed: true }),
    })
    expect(resolveTicket).toHaveBeenCalledWith('ticket-1', {
      signal: expect.any(AbortSignal),
    })
  })

  it('maps 200 changed=false to already-resolved with result', async () => {
    resolveTicket.mockResolvedValueOnce(sampleResult({ changed: false }))
    const { result } = renderHook(() => useResolveTicket('ticket-1'))

    let outcome: ResolveTicketOutcome | null | undefined
    await act(async () => {
      outcome = await result.current.resolve()
    })

    expect(outcome).toEqual({
      kind: 'already-resolved',
      result: sampleResult({ changed: false }),
    })
  })

  it('maps 409 to conflict with null result', async () => {
    resolveTicket.mockRejectedValueOnce(new ApiError(409, 'Conflict body'))
    const { result } = renderHook(() => useResolveTicket('ticket-1'))

    let outcome: ResolveTicketOutcome | null | undefined
    await act(async () => {
      outcome = await result.current.resolve()
    })

    expect(outcome).toEqual({ kind: 'conflict', result: null })
  })

  it('maps network TypeError to network-ambiguous with null result', async () => {
    resolveTicket.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    const { result } = renderHook(() => useResolveTicket('ticket-1'))

    let outcome: ResolveTicketOutcome | null | undefined
    await act(async () => {
      outcome = await result.current.resolve()
    })

    expect(outcome).toEqual({ kind: 'network-ambiguous', result: null })
  })

  it('maps 404 to not-found with null result', async () => {
    resolveTicket.mockRejectedValueOnce(new ApiError(404, 'missing body'))
    const { result } = renderHook(() => useResolveTicket('ticket-1'))

    let outcome: ResolveTicketOutcome | null | undefined
    await act(async () => {
      outcome = await result.current.resolve()
    })

    expect(outcome).toEqual({ kind: 'not-found', result: null })
  })

  it('maps 5xx to server-error with null result', async () => {
    resolveTicket.mockRejectedValueOnce(
      new ApiError(500, 'upstream-raw', { title: 'Err' }),
    )
    const { result } = renderHook(() => useResolveTicket('ticket-1'))

    let outcome: ResolveTicketOutcome | null | undefined
    await act(async () => {
      outcome = await result.current.resolve()
    })

    expect(outcome).toEqual({ kind: 'server-error', result: null })
  })

  it('maps invalid 200 payloads to server-error with null result', async () => {
    resolveTicket.mockResolvedValueOnce({
      ticketId: 'ticket-1',
      ticketNumber: 'VS-000001',
      status: 'New',
      resolvedAt: 'not-a-date',
      updatedAt: '2026-07-20T12:00:00.000Z',
      lastActivityAt: '2026-07-20T12:00:00.000Z',
      closedByUserId: 'user-1',
      changed: true,
    })
    const { result } = renderHook(() => useResolveTicket('ticket-1'))

    let outcome: ResolveTicketOutcome | null | undefined
    await act(async () => {
      outcome = await result.current.resolve()
    })

    expect(outcome).toEqual({ kind: 'server-error', result: null })
  })

  it('returns null for protected 401 without inventing a local outcome', async () => {
    resolveTicket.mockRejectedValueOnce(new ApiError(401, 'Unauthorized'))
    const { result } = renderHook(() => useResolveTicket('ticket-1'))

    let outcome: ResolveTicketOutcome | null | undefined
    await act(async () => {
      outcome = await result.current.resolve()
    })

    expect(outcome).toBeNull()
  })

  it('returns the same in-flight promise and posts only once while resolving', async () => {
    const pending = deferred<ResolveTicketResult>()
    resolveTicket.mockReturnValueOnce(pending.promise)
    const { result } = renderHook(() => useResolveTicket('ticket-1'))

    let first!: Promise<ResolveTicketOutcome | null>
    let second!: Promise<ResolveTicketOutcome | null>

    act(() => {
      first = result.current.resolve()
      second = result.current.resolve()
    })

    expect(first).toBe(second)
    await waitFor(() => {
      expect(result.current.isResolving).toBe(true)
    })
    expect(resolveTicket).toHaveBeenCalledTimes(1)

    let outcome: ResolveTicketOutcome | null | undefined
    await act(async () => {
      pending.resolve(sampleResult())
      outcome = await first
    })

    expect(outcome).toEqual({
      kind: 'resolved',
      result: sampleResult(),
    })
    expect(resolveTicket).toHaveBeenCalledTimes(1)
    expect(result.current.isResolving).toBe(false)
  })

  it('creates a new request after the previous flight settles', async () => {
    resolveTicket
      .mockResolvedValueOnce(sampleResult({ changed: true }))
      .mockResolvedValueOnce(sampleResult({ changed: false }))
    const { result } = renderHook(() => useResolveTicket('ticket-1'))

    await act(async () => {
      await result.current.resolve()
    })
    await act(async () => {
      await result.current.resolve()
    })

    expect(resolveTicket).toHaveBeenCalledTimes(2)
  })

  it('aborts the active request on unmount without a server-error outcome', async () => {
    const pending = deferred<ResolveTicketResult>()
    resolveTicket.mockReturnValueOnce(pending.promise)
    const { result, unmount } = renderHook(() => useResolveTicket('ticket-1'))

    let outcomePromise!: Promise<ResolveTicketOutcome | null>
    act(() => {
      outcomePromise = result.current.resolve()
    })

    await waitFor(() => {
      expect(resolveTicket).toHaveBeenCalledTimes(1)
    })

    const signal = resolveTicket.mock.calls[0]?.[1]?.signal as AbortSignal
    expect(signal.aborted).toBe(false)

    unmount()
    expect(signal.aborted).toBe(true)

    await act(async () => {
      pending.reject(
        new DOMException('The operation was aborted.', 'AbortError'),
      )
    })

    const outcome = await outcomePromise
    expect(outcome).toBeNull()
  })
})
