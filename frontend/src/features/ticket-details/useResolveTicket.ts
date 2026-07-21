import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { resolveTicket } from '../../api/ticketsApi'
import type { ResolveTicketResult } from '../../api/types'

export type ResolveTicketOutcomeKind =
  | 'resolved'
  | 'already-resolved'
  | 'conflict'
  | 'network-ambiguous'
  | 'not-found'
  | 'server-error'

export type ResolveTicketOutcome = {
  kind: ResolveTicketOutcomeKind
  result: ResolveTicketResult | null
}

export const RESOLUTION_COPY = {
  trigger: 'Çözüldü olarak işaretle',
  dialogTitle: 'Talebi çözmek istiyor musunuz?',
  dialogDescription: 'Müşterinin yeni bir e-postası bu talebi yeniden açar.',
  cancel: 'Vazgeç',
  confirm: 'Talebi çöz',
  busy: 'Talep çözülüyor…',
  resolved: 'Talep çözüldü.',
  alreadyResolved: 'Talep zaten çözülmüş. Güncel bilgiler yüklendi.',
  conflict: 'Talep başka bir işlemle güncellendi. Güncel durum yüklendi.',
  network:
    'Bağlantı kesildi. Talebin çözülüp çözülmediğini güncel durumdan kontrol edin.',
  notFound: 'Destek talebi bulunamadı.',
  serverError: 'Talep çözülemedi. Lütfen yeniden deneyin.',
  closureNote:
    'Bu talep çözüldü. Müşteri yeni bir e-posta gönderirse talep yeniden açılır.',
} as const

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function isParseableTimestamp(value: unknown): value is string {
  return typeof value === 'string' && !Number.isNaN(Date.parse(value))
}

function isValidResolveResult(
  value: unknown,
  ticketId: string,
): value is ResolveTicketResult {
  if (typeof value !== 'object' || value === null) {
    return false
  }

  const record = value as Record<string, unknown>

  if (record.ticketId !== ticketId) {
    return false
  }

  if (typeof record.ticketNumber !== 'string') {
    return false
  }

  if (record.status !== 'Resolved') {
    return false
  }

  if (!isParseableTimestamp(record.resolvedAt)) {
    return false
  }

  if (!isParseableTimestamp(record.updatedAt)) {
    return false
  }

  if (!isParseableTimestamp(record.lastActivityAt)) {
    return false
  }

  if (
    record.closedByUserId !== null &&
    typeof record.closedByUserId !== 'string'
  ) {
    return false
  }

  if (typeof record.changed !== 'boolean') {
    return false
  }

  return true
}

function mapSuccess(
  value: unknown,
  ticketId: string,
): ResolveTicketOutcome {
  if (!isValidResolveResult(value, ticketId)) {
    return { kind: 'server-error', result: null }
  }

  if (value.changed) {
    return { kind: 'resolved', result: value }
  }

  return { kind: 'already-resolved', result: value }
}

function mapThrownError(error: unknown): ResolveTicketOutcome | null {
  if (isAbortError(error)) {
    return null
  }

  if (error instanceof ApiError && error.status === 401) {
    return null
  }

  if (error instanceof TypeError) {
    return { kind: 'network-ambiguous', result: null }
  }

  if (error instanceof ApiError) {
    if (error.status === 409) {
      return { kind: 'conflict', result: null }
    }

    if (error.status === 404) {
      return { kind: 'not-found', result: null }
    }

    return { kind: 'server-error', result: null }
  }

  return { kind: 'server-error', result: null }
}

export function useResolveTicket(ticketId: string): {
  isResolving: boolean
  resolve: () => Promise<ResolveTicketOutcome | null>
} {
  const [isResolving, setIsResolving] = useState(false)
  const activePromise = useRef<Promise<ResolveTicketOutcome | null> | null>(
    null,
  )
  const activeController = useRef<AbortController | null>(null)

  useEffect(() => {
    return () => {
      activeController.current?.abort()
      activeController.current = null
      activePromise.current = null
    }
  }, [ticketId])

  const resolve = useCallback((): Promise<ResolveTicketOutcome | null> => {
    if (activePromise.current) {
      return activePromise.current
    }

    const controller = new AbortController()
    activeController.current = controller

    const promise = (async (): Promise<ResolveTicketOutcome | null> => {
      setIsResolving(true)
      try {
        const result = await resolveTicket(ticketId, {
          signal: controller.signal,
        })
        return mapSuccess(result, ticketId)
      } catch (error) {
        return mapThrownError(error)
      } finally {
        setIsResolving(false)
        activePromise.current = null
        if (activeController.current === controller) {
          activeController.current = null
        }
      }
    })()

    activePromise.current = promise
    return promise
  }, [ticketId])

  return { isResolving, resolve }
}
