import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { fetchTicketDetails } from '../../api/ticketsApi'
import type { ResolveTicketResult, TicketDetails } from '../../api/types'

export type TicketDetailErrorKind = 'network' | 'not-found' | 'server'

export type UseTicketDetailsResult = {
  detail: TicketDetails | null
  hasLoaded: boolean
  isInitialLoading: boolean
  isRefreshing: boolean
  error: { kind: TicketDetailErrorKind } | null
  refresh: () => Promise<void>
  applyResolvedTicket: (result: ResolveTicketResult) => void
}

type TicketDetailRequestState = {
  detail: TicketDetails | null
  activeTicketId: string | undefined
  hasLoaded: boolean
  isLoading: boolean
  error: { kind: TicketDetailErrorKind } | null
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function classifyError(error: unknown): TicketDetailErrorKind {
  if (error instanceof ApiError && error.status === 404) {
    return 'not-found'
  }
  if (error instanceof TypeError) {
    return 'network'
  }
  return 'server'
}

function isUsableTicketId(ticketId: string | undefined): ticketId is string {
  return typeof ticketId === 'string' && ticketId.trim().length > 0
}

export function useTicketDetails(
  ticketId: string | undefined,
): UseTicketDetailsResult {
  const [state, setState] = useState<TicketDetailRequestState>({
    detail: null,
    activeTicketId: undefined,
    hasLoaded: false,
    isLoading: false,
    error: null,
  })
  const requestSequence = useRef(0)
  const activeController = useRef<AbortController | null>(null)

  const load = useCallback(async () => {
    if (!isUsableTicketId(ticketId)) {
      activeController.current?.abort()
      requestSequence.current += 1
      setState({
        detail: null,
        activeTicketId: ticketId,
        hasLoaded: true,
        isLoading: false,
        error: { kind: 'not-found' },
      })
      return
    }

    const sequence = ++requestSequence.current
    activeController.current?.abort()
    const controller = new AbortController()
    activeController.current = controller

    setState((current) => {
      const retaining =
        current.hasLoaded &&
        current.activeTicketId === ticketId &&
        current.detail !== null
      return {
        detail: retaining ? current.detail : null,
        activeTicketId: ticketId,
        hasLoaded: retaining,
        isLoading: true,
        error: null,
      }
    })

    try {
      const detail = await fetchTicketDetails(ticketId, {
        signal: controller.signal,
      })
      if (sequence !== requestSequence.current) {
        return
      }
      setState({
        detail,
        activeTicketId: ticketId,
        hasLoaded: true,
        isLoading: false,
        error: null,
      })
    } catch (error) {
      if (sequence !== requestSequence.current || isAbortError(error)) {
        return
      }
      if (error instanceof ApiError && error.status === 401) {
        return
      }

      const kind = classifyError(error)
      setState((current) => {
        // Refresh failures keep last good detail; initial not-found has none to keep.
        const retaining =
          current.activeTicketId === ticketId && current.detail !== null
        return {
          detail: retaining ? current.detail : null,
          activeTicketId: ticketId,
          hasLoaded: true,
          isLoading: false,
          error: { kind },
        }
      })
    }
  }, [ticketId])

  useEffect(() => {
    void load()
    return () => {
      activeController.current?.abort()
      requestSequence.current += 1
    }
  }, [load])

  const applyResolvedTicket = useCallback((result: ResolveTicketResult) => {
    setState((current) => {
      if (
        current.detail === null ||
        current.detail.id !== result.ticketId
      ) {
        return current
      }

      return {
        ...current,
        detail: {
          ...current.detail,
          status: result.status,
          resolvedAt: result.resolvedAt,
          updatedAt: result.updatedAt,
          lastActivityAt: result.lastActivityAt,
          closedByUserId: result.closedByUserId,
          waitingCustomerSince: null,
        },
      }
    })
  }, [])

  const forActiveTicket = state.activeTicketId === ticketId
  const usableTicketId = isUsableTicketId(ticketId)

  return {
    detail: forActiveTicket ? state.detail : null,
    hasLoaded: forActiveTicket ? state.hasLoaded : false,
    isInitialLoading:
      usableTicketId &&
      (!forActiveTicket || (state.isLoading && !state.hasLoaded)),
    isRefreshing:
      usableTicketId && forActiveTicket && state.isLoading && state.hasLoaded,
    error: forActiveTicket ? state.error : null,
    refresh: load,
    applyResolvedTicket,
  }
}
