import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { SupportReplyResult } from '../../api/types'
import { TicketReplyForm } from './TicketReplyForm'
import {
  REPLY_OUTCOME_MESSAGES,
  SUPPORT_REPLY_MAX_LENGTH,
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

function renderForm(onRefresh = vi.fn().mockResolvedValue(undefined)) {
  return {
    onRefresh,
    user: userEvent.setup(),
    ...render(
      <TicketReplyForm ticketId="ticket-1" onRefresh={onRefresh} />,
    ),
  }
}

describe('TicketReplyForm', () => {
  beforeEach(() => {
    replyToTicket.mockReset()
  })

  it('shows composer heading, label, remaining count, and submit', () => {
    renderForm()

    expect(
      screen.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Yanıtınız')).toBeInTheDocument()
    expect(screen.getByText(/Kalan karakter:/)).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Yanıtı gönder' }),
    ).toBeInTheDocument()
  })

  it('uses a plain-text multiline textarea with a matching label', async () => {
    const { user } = renderForm()
    const textarea = screen.getByLabelText('Yanıtınız')

    expect(textarea.tagName).toBe('TEXTAREA')
    await user.type(textarea, 'Satır 1{enter}Satır 2')
    expect(textarea).toHaveValue('Satır 1\nSatır 2')
  })

  it('blank local validation makes no request, preserves draft, and focuses textarea', async () => {
    const { user } = renderForm()
    const textarea = screen.getByLabelText('Yanıtınız')

    await user.type(textarea, '   ')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    expect(replyToTicket).not.toHaveBeenCalled()
    expect(textarea).toHaveValue('   ')
    expect(textarea).toHaveFocus()
    expect(screen.getByRole('alert')).toHaveTextContent(
      REPLY_OUTCOME_MESSAGES.validationRequired,
    )
    expect(textarea).toHaveAttribute('aria-invalid', 'true')
  })

  it('oversized local validation makes no request, preserves draft, and focuses textarea', async () => {
    const { user } = renderForm()
    const textarea = screen.getByLabelText('Yanıtınız')
    const tooLong = 'x'.repeat(SUPPORT_REPLY_MAX_LENGTH + 1)

    await user.click(textarea)
    await user.paste(tooLong)
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    expect(replyToTicket).not.toHaveBeenCalled()
    expect(textarea).toHaveValue(tooLong)
    expect(textarea).toHaveFocus()
    expect(screen.getByRole('alert')).toHaveTextContent(
      REPLY_OUTCOME_MESSAGES.validationTooLong,
    )
  })

  it('busy state disables composer, shows sending label, and posts once', async () => {
    const pending = deferred<SupportReplyResult>()
    replyToTicket.mockReturnValueOnce(pending.promise)
    const { user, onRefresh } = renderForm()

    await user.type(screen.getByLabelText('Yanıtınız'), 'Merhaba')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    const section = document.querySelector('.ticket-reply')
    expect(section).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByLabelText('Yanıtınız')).toBeDisabled()
    expect(
      screen.getByRole('button', { name: 'Yanıt gönderiliyor…' }),
    ).toBeDisabled()

    await user.click(screen.getByRole('button', { name: 'Yanıt gönderiliyor…' }))
    expect(replyToTicket).toHaveBeenCalledTimes(1)

    pending.resolve(sampleReply())
    await waitFor(() => {
      expect(onRefresh).toHaveBeenCalledTimes(1)
    })
    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.delivered),
    ).toBeInTheDocument()
  })

  it('clears draft, awaits refresh, and focuses notice for delivered outcome', async () => {
    const refreshPending = deferred<void>()
    const onRefresh = vi.fn().mockReturnValue(refreshPending.promise)
    replyToTicket.mockResolvedValueOnce(sampleReply())
    const { user } = renderForm(onRefresh)

    await user.type(screen.getByLabelText('Yanıtınız'), 'Gönderildi')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    await waitFor(() => {
      expect(replyToTicket).toHaveBeenCalledTimes(1)
    })
    expect(screen.getByLabelText('Yanıtınız')).toHaveValue('')
    expect(screen.queryByText(REPLY_OUTCOME_MESSAGES.delivered)).not.toBeInTheDocument()
    expect(onRefresh).toHaveBeenCalledTimes(1)

    refreshPending.resolve()
    const notice = await screen.findByText(REPLY_OUTCOME_MESSAGES.delivered)
    expect(notice).toHaveAttribute('role', 'status')
    await waitFor(() => {
      expect(notice).toHaveFocus()
    })
  })

  it('shows exact Turkish notices for smtp-failed and state-conflict and clears draft', async () => {
    const { user, onRefresh } = renderForm()

    replyToTicket.mockResolvedValueOnce(
      sampleReply({
        emailDelivered: false,
        ticketStateUpdated: false,
        noticeCode: 'smtp-delivery-failed',
      }),
    )
    await user.type(screen.getByLabelText('Yanıtınız'), 'SMTP')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.smtpFailed),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Yanıtınız')).toHaveValue('')
    expect(onRefresh).toHaveBeenCalledTimes(1)

    replyToTicket.mockResolvedValueOnce(
      sampleReply({
        emailDelivered: true,
        ticketStateUpdated: false,
        noticeCode: 'ticket-state-conflict',
      }),
    )
    await user.type(screen.getByLabelText('Yanıtınız'), 'Durum')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.stateConflict),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Yanıtınız')).toHaveValue('')
  })

  it('preserves draft for validation, 409, network, not-found, and server outcomes', async () => {
    const { user, onRefresh } = renderForm()
    const textarea = screen.getByLabelText('Yanıtınız')

    await user.type(textarea, '   ')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect(textarea).toHaveValue('   ')

    await user.clear(textarea)
    await user.type(textarea, 'Taslak metin')

    replyToTicket.mockRejectedValueOnce(new ApiError(409, 'conflict'))
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.preSendConflict),
    ).toBeInTheDocument()
    expect(textarea).toHaveValue('Taslak metin')
    expect(onRefresh).toHaveBeenCalledTimes(1)

    replyToTicket.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.networkAmbiguous),
    ).toBeInTheDocument()
    expect(textarea).toHaveValue('Taslak metin')
    expect(onRefresh).toHaveBeenCalledTimes(1)

    replyToTicket.mockRejectedValueOnce(new ApiError(404, 'missing'))
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.notFound),
    ).toBeInTheDocument()
    expect(textarea).toHaveValue('Taslak metin')

    replyToTicket.mockRejectedValueOnce(
      new ApiError(500, 'upstream boom', { title: 'Internal' }),
    )
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    const serverAlert = await screen.findByText(REPLY_OUTCOME_MESSAGES.serverError)
    expect(serverAlert).toBeInTheDocument()
    expect(serverAlert).not.toHaveTextContent('upstream')
    expect(serverAlert).not.toHaveTextContent('Internal')
    expect(textarea).toHaveValue('Taslak metin')
  })

  it('does not auto-refresh on network ambiguity', async () => {
    const { user, onRefresh } = renderForm()
    replyToTicket.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    await user.type(screen.getByLabelText('Yanıtınız'), 'Belirsiz')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    await screen.findByText(REPLY_OUTCOME_MESSAGES.networkAmbiguous)
    expect(onRefresh).not.toHaveBeenCalled()
    expect(replyToTicket).toHaveBeenCalledTimes(1)
  })

  it('keeps saved notice when post-save refresh fails', async () => {
    const onRefresh = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'))
    replyToTicket.mockResolvedValueOnce(sampleReply())
    const { user } = renderForm(onRefresh)

    await user.type(screen.getByLabelText('Yanıtınız'), 'Kaydedildi')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.delivered),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Yanıtınız')).toHaveValue('')
    expect(onRefresh).toHaveBeenCalledTimes(1)
  })

  it('maps unrecognized HTTP 200 with the fixed saved-but-unknown Turkish copy', async () => {
    replyToTicket.mockResolvedValueOnce(
      sampleReply({
        emailDelivered: false,
        ticketStateUpdated: true,
        noticeCode: 'weird',
      }),
    )
    const { user, onRefresh } = renderForm()

    await user.type(screen.getByLabelText('Yanıtınız'), 'Bilinmeyen')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.unrecognizedSaved),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Yanıtınız')).toHaveValue('')
    expect(onRefresh).toHaveBeenCalledTimes(1)
  })

  it('does not expose backend body text for 400 validation codes', async () => {
    replyToTicket.mockRejectedValueOnce(
      new ApiError(400, 'English backend title', {
        code: 'reply-content-required',
        title: 'Bad Request',
        traceId: 'abc-123',
      }),
    )
    const { user } = renderForm()

    await user.type(screen.getByLabelText('Yanıtınız'), 'x')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(REPLY_OUTCOME_MESSAGES.validationRequired)
    expect(alert).not.toHaveTextContent('English')
    expect(alert).not.toHaveTextContent('traceId')
    expect(alert).not.toHaveTextContent('Bad Request')
    expect(within(document.body).queryByText(/abc-123/)).not.toBeInTheDocument()
  })
})
