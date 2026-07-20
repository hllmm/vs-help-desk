import { useCallback, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { replyToTicket } from '../../api/ticketsApi'
import type { SupportReplyResult } from '../../api/types'

export const SUPPORT_REPLY_MAX_LENGTH = 65_536

export type ReplyOutcomeKind =
  | 'delivered'
  | 'smtp-failed'
  | 'state-conflict'
  | 'pre-send-conflict'
  | 'network-ambiguous'
  | 'validation-required'
  | 'validation-too-long'
  | 'not-found'
  | 'server-error'

export type ReplySubmissionOutcome = {
  kind: ReplyOutcomeKind
  messageSaved: boolean
}

export const REPLY_OUTCOME_MESSAGES = {
  delivered: 'Yanıt kaydedildi ve müşteriye gönderildi.',
  smtpFailed: 'Yanıt kaydedildi ancak e-posta müşteriye gönderilemedi.',
  stateConflict:
    'E-posta gönderildi ancak talep durumu güncellenemedi. Güncel durumu kontrol edin.',
  preSendConflict:
    'Talep başka bir işlemle güncellendi. Yanıt gönderilmedi; sayfa yenilendi.',
  networkAmbiguous:
    'Bağlantı kesildi. Yanıtın kaydedilip kaydedilmediğini kontrol etmek için sayfayı yenileyin; yanıtı hemen tekrar göndermeyin.',
  unrecognizedSaved:
    'Yanıt kaydedildi ancak gönderim sonucu doğrulanamadı. Güncel durumu kontrol edin.',
  validationRequired: 'Yanıt metni zorunludur.',
  validationTooLong: 'Yanıt en fazla 65.536 karakter olabilir.',
  notFound: 'Destek talebi bulunamadı.',
  serverError: 'Yanıt gönderilemedi. Lütfen yeniden deneyin.',
} as const

export function messageForReplyOutcome(
  kind: ReplyOutcomeKind,
  messageSaved = false,
): string {
  if (kind === 'server-error' && messageSaved) {
    return REPLY_OUTCOME_MESSAGES.unrecognizedSaved
  }

  switch (kind) {
    case 'delivered':
      return REPLY_OUTCOME_MESSAGES.delivered
    case 'smtp-failed':
      return REPLY_OUTCOME_MESSAGES.smtpFailed
    case 'state-conflict':
      return REPLY_OUTCOME_MESSAGES.stateConflict
    case 'pre-send-conflict':
      return REPLY_OUTCOME_MESSAGES.preSendConflict
    case 'network-ambiguous':
      return REPLY_OUTCOME_MESSAGES.networkAmbiguous
    case 'validation-required':
      return REPLY_OUTCOME_MESSAGES.validationRequired
    case 'validation-too-long':
      return REPLY_OUTCOME_MESSAGES.validationTooLong
    case 'not-found':
      return REPLY_OUTCOME_MESSAGES.notFound
    case 'server-error':
      return REPLY_OUTCOME_MESSAGES.serverError
  }
}

function getErrorCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || !('code' in body)) {
    return null
  }
  const code = (body as { code: unknown }).code
  return typeof code === 'string' ? code : null
}

function mapSuccessResult(result: SupportReplyResult): ReplySubmissionOutcome {
  const notice = result.noticeCode ?? null

  if (
    result.emailDelivered === true &&
    result.ticketStateUpdated === true &&
    notice === null
  ) {
    return { kind: 'delivered', messageSaved: true }
  }

  if (
    notice === 'smtp-delivery-failed' &&
    result.emailDelivered === false &&
    result.ticketStateUpdated === false
  ) {
    return { kind: 'smtp-failed', messageSaved: true }
  }

  if (
    notice === 'ticket-state-conflict' &&
    result.emailDelivered === true &&
    result.ticketStateUpdated === false
  ) {
    return { kind: 'state-conflict', messageSaved: true }
  }

  // HTTP 200 proves the message was saved even when the combination is unexpected.
  return { kind: 'server-error', messageSaved: true }
}

function mapThrownError(error: unknown): ReplySubmissionOutcome {
  if (error instanceof ApiError && error.status === 401) {
    // Session expiry is owned by the API client (redirect); do not invent a local outcome.
    throw error
  }

  if (error instanceof TypeError) {
    return { kind: 'network-ambiguous', messageSaved: false }
  }

  if (error instanceof ApiError) {
    if (error.status === 409) {
      return { kind: 'pre-send-conflict', messageSaved: false }
    }

    if (error.status === 404) {
      return { kind: 'not-found', messageSaved: false }
    }

    if (error.status === 400) {
      const code = getErrorCode(error.body)
      if (code === 'reply-content-required') {
        return { kind: 'validation-required', messageSaved: false }
      }
      if (code === 'reply-content-too-long') {
        return { kind: 'validation-too-long', messageSaved: false }
      }
      return { kind: 'server-error', messageSaved: false }
    }

    return { kind: 'server-error', messageSaved: false }
  }

  return { kind: 'server-error', messageSaved: false }
}

export function useTicketReply(ticketId: string): {
  isSubmitting: boolean
  submit: (content: string) => Promise<ReplySubmissionOutcome>
} {
  const [isSubmitting, setIsSubmitting] = useState(false)
  const inFlightRef = useRef<Promise<ReplySubmissionOutcome> | null>(null)

  const submit = useCallback(
    (content: string): Promise<ReplySubmissionOutcome> => {
      if (inFlightRef.current) {
        return inFlightRef.current
      }

      const trimmed = content.trim()

      if (trimmed.length === 0) {
        return Promise.resolve({
          kind: 'validation-required',
          messageSaved: false,
        })
      }

      if (trimmed.length > SUPPORT_REPLY_MAX_LENGTH) {
        return Promise.resolve({
          kind: 'validation-too-long',
          messageSaved: false,
        })
      }

      const promise = (async (): Promise<ReplySubmissionOutcome> => {
        setIsSubmitting(true)
        try {
          const result = await replyToTicket(ticketId, { content: trimmed })
          return mapSuccessResult(result)
        } catch (error) {
          return mapThrownError(error)
        } finally {
          setIsSubmitting(false)
          inFlightRef.current = null
        }
      })()

      inFlightRef.current = promise
      return promise
    },
    [ticketId],
  )

  return { isSubmitting, submit }
}
