import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { SupportReplyResult } from '../../api/types'
import {
  SUPPORT_REPLY_MAX_LENGTH,
  useTicketReply,
  type ReplySubmissionOutcome,
} from './useTicketReply'

const replyToTicket = vi.hoisted(() => vi.fn())

vi.mock('../../api/ticketsApi', () => ({
  replyToTicket,
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

function sampleReply(
  overrides: Partial<SupportReplyResult> = {},
): SupportReplyResult {
  return {
    ticketId: 'ticket-1',
    ticketNumber: 'VS-000001',
    messageId: 'msg-1',
    status: 'WaitingCustomerReply',
    emailDelivered: true,
    ticketStateUpdated: true,
    noticeCode: null,
    ...overrides,
  }
}

describe('useTicketReply', () => {
  beforeEach(() => {
    replyToTicket.mockReset()
  })

  it('rejects blank and whitespace without a request', async () => {
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit('   \n\t  ')
    })

    expect(outcome).toEqual({
      kind: 'validation-required',
      messageSaved: false,
    })
    expect(replyToTicket).not.toHaveBeenCalled()
    expect(result.current.isSubmitting).toBe(false)
  })

  it('rejects 65,537 trimmed UTF-16 units without a request', async () => {
    const { result } = renderHook(() => useTicketReply('ticket-1'))
    const tooLong = 'x'.repeat(SUPPORT_REPLY_MAX_LENGTH + 1)

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit(`  ${tooLong}  `)
    })

    expect(outcome).toEqual({
      kind: 'validation-too-long',
      messageSaved: false,
    })
    expect(replyToTicket).not.toHaveBeenCalled()
  })

  it('accepts exactly 65,536 trimmed UTF-16 units', async () => {
    replyToTicket.mockResolvedValueOnce(sampleReply())
    const { result } = renderHook(() => useTicketReply('ticket-1'))
    const exact = 'y'.repeat(SUPPORT_REPLY_MAX_LENGTH)

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit(`  ${exact}  `)
    })

    expect(outcome).toEqual({ kind: 'delivered', messageSaved: true, messageId: 'msg-1' })
    expect(replyToTicket).toHaveBeenCalledWith('ticket-1', { content: exact })
    const body = replyToTicket.mock.calls[0]?.[1] as { content: string }
    expect(body.content.length).toBe(SUPPORT_REPLY_MAX_LENGTH)
    expect(body).not.toHaveProperty('isHtml')
  })

  it('maps delivered + state updated to delivered with messageSaved true', async () => {
    replyToTicket.mockResolvedValueOnce(sampleReply())
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit('  Merhaba  ')
    })

    expect(outcome).toEqual({ kind: 'delivered', messageSaved: true, messageId: 'msg-1' })
    expect(replyToTicket).toHaveBeenCalledWith('ticket-1', {
      content: 'Merhaba',
    })
  })

  it('maps smtp-delivery-failed to smtp-failed with messageSaved true', async () => {
    replyToTicket.mockResolvedValueOnce(
      sampleReply({
        emailDelivered: false,
        ticketStateUpdated: false,
        noticeCode: 'smtp-delivery-failed',
        status: 'New',
      }),
    )
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit('x'.repeat(200))
    })

    expect(outcome).toEqual({ kind: 'smtp-failed', messageSaved: true, messageId: 'msg-1' })
  })

  it('maps ticket-state-conflict to state-conflict with messageSaved true', async () => {
    replyToTicket.mockResolvedValueOnce(
      sampleReply({
        emailDelivered: true,
        ticketStateUpdated: false,
        noticeCode: 'ticket-state-conflict',
        status: 'CustomerReplied',
      }),
    )
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit('x'.repeat(200))
    })

    expect(outcome).toEqual({ kind: 'state-conflict', messageSaved: true, messageId: 'msg-1' })
  })

  it('maps 409 to pre-send-conflict with messageSaved false', async () => {
    replyToTicket.mockRejectedValueOnce(new ApiError(409, 'Conflict'))
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit('x'.repeat(200))
    })

    expect(outcome).toEqual({
      kind: 'pre-send-conflict',
      messageSaved: false,
    })
  })

  it('maps network TypeError to network-ambiguous with messageSaved false', async () => {
    replyToTicket.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit('x'.repeat(200))
    })

    expect(outcome).toEqual({
      kind: 'network-ambiguous',
      messageSaved: false,
    })
  })

  it('maps 400 reply-content-required to validation-required', async () => {
    replyToTicket.mockRejectedValueOnce(
      new ApiError(400, 'required', { code: 'reply-content-required' }),
    )
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit('x'.repeat(200))
    })

    expect(outcome).toEqual({
      kind: 'validation-required',
      messageSaved: false,
    })
  })

  it('maps 400 reply-content-too-long to validation-too-long', async () => {
    replyToTicket.mockRejectedValueOnce(
      new ApiError(400, 'too long', { code: 'reply-content-too-long' }),
    )
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit('x'.repeat(200))
    })

    expect(outcome).toEqual({
      kind: 'validation-too-long',
      messageSaved: false,
    })
  })

  it('maps 404 to not-found with messageSaved false', async () => {
    replyToTicket.mockRejectedValueOnce(new ApiError(404, 'missing'))
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit('x'.repeat(200))
    })

    expect(outcome).toEqual({ kind: 'not-found', messageSaved: false })
  })

  it('maps other 4xx/5xx to server-error with messageSaved false', async () => {
    replyToTicket.mockRejectedValueOnce(new ApiError(500, 'boom', { title: 'Err' }))
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit('x'.repeat(200))
    })

    expect(outcome).toEqual({ kind: 'server-error', messageSaved: false })
  })

  it('maps unexpected HTTP 200 combinations to server-error with messageSaved true', async () => {
    replyToTicket.mockResolvedValueOnce(
      sampleReply({
        emailDelivered: false,
        ticketStateUpdated: true,
        noticeCode: 'future-unknown-code',
      }),
    )
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      outcome = await result.current.submit('x'.repeat(200))
    })

    expect(outcome).toEqual({ kind: 'server-error', messageSaved: true, messageId: 'msg-1' })
  })

  it('returns the same in-flight promise and posts only once while submitting', async () => {
    const pending = deferred<SupportReplyResult>()
    replyToTicket.mockReturnValueOnce(pending.promise)
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    let first!: Promise<ReplySubmissionOutcome>
    let second!: Promise<ReplySubmissionOutcome>

    act(() => {
      first = result.current.submit('Merhaba')
      second = result.current.submit('Merhaba tekrar')
    })

    expect(first).toBe(second)
    await waitFor(() => {
      expect(result.current.isSubmitting).toBe(true)
    })
    expect(replyToTicket).toHaveBeenCalledTimes(1)

    let outcome: ReplySubmissionOutcome | undefined
    await act(async () => {
      pending.resolve(sampleReply())
      outcome = await first
    })

    expect(outcome).toEqual({ kind: 'delivered', messageSaved: true, messageId: 'msg-1' })
    expect(replyToTicket).toHaveBeenCalledTimes(1)
    expect(result.current.isSubmitting).toBe(false)
  })

  it('does not invent a local outcome for protected 401', async () => {
    replyToTicket.mockRejectedValueOnce(new ApiError(401, 'Unauthorized'))
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    await expect(
      act(async () => {
        await result.current.submit('x'.repeat(200))
      }),
    ).rejects.toMatchObject({ status: 401, name: 'ApiError' })
  })

  it('trims once before POST and never sends isHtml', async () => {
    replyToTicket.mockResolvedValueOnce(sampleReply())
    const { result } = renderHook(() => useTicketReply('ticket-1'))

    await act(async () => {
      await result.current.submit('\n  Satır 1\nSatır 2  \t')
    })

    expect(replyToTicket).toHaveBeenCalledWith('ticket-1', {
      content: 'Satır 1\nSatır 2',
    })
    expect(Object.keys(replyToTicket.mock.calls[0]?.[1] as object)).toEqual([
      'content',
    ])
  })
})
