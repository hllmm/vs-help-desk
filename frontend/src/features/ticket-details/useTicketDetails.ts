import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { fetchTicketDetails, fetchTicketMessages } from '../../api/ticketsApi'
import type {
  AssignTicketResult,
  ResolveTicketResult,
  TicketDetails,
} from '../../api/types'

export type TicketDetailErrorKind = 'network' | 'not-found' | 'server'
export type OlderMessagesErrorKind = 'network' | 'server'

export type UseTicketDetailsResult = {
  detail: TicketDetails | null
  hasLoaded: boolean
  isInitialLoading: boolean
  isRefreshing: boolean
  isLoadingOlder: boolean
  error: { kind: TicketDetailErrorKind } | null
  olderMessagesError: { kind: OlderMessagesErrorKind } | null
  refresh: () => Promise<void>
  loadOlderMessages: () => Promise<void>
  applyResolvedTicket: (result: ResolveTicketResult) => void
  applyAssignment: (result: AssignTicketResult) => void
}

type TicketDetailRequestState = {
  detail: TicketDetails | null
  activeTicketId: string | undefined
  hasLoaded: boolean
  isLoading: boolean
  isLoadingOlder: boolean
  error: { kind: TicketDetailErrorKind } | null
  olderMessagesError: { kind: OlderMessagesErrorKind } | null
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

function classifyOlderError(error: unknown): OlderMessagesErrorKind {
  return error instanceof TypeError ? 'network' : 'server'
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
    isLoadingOlder: false,
    error: null,
    olderMessagesError: null,
  })
  const requestSequence = useRef(0)
  const activeController = useRef<AbortController | null>(null)
  const olderController = useRef<AbortController | null>(null)
  // Cursor entries are scoped to the ticket whose detail produced them, so a
  // stale cursor from a previous route can never drive a new ticket's pages.
  const activeMessageCursor = useRef<{
    ticketId: string
    cursor: string | null
  } | null>(null)

  const load = useCallback(async () => {
    if (!isUsableTicketId(ticketId)) {
      activeController.current?.abort()
      olderController.current?.abort()
      requestSequence.current += 1
      activeMessageCursor.current = null
      setState({
        detail: null,
        activeTicketId: ticketId,
        hasLoaded: true,
        isLoading: false,
        isLoadingOlder: false,
        error: { kind: 'not-found' },
        olderMessagesError: null,
      })
      return
    }

    const sequence = ++requestSequence.current
    activeController.current?.abort()
    olderController.current?.abort()
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
        isLoadingOlder: false,
        error: null,
        olderMessagesError: null,
      }
    })

    try {
      const detail = await fetchTicketDetails(ticketId, {
        signal: controller.signal,
      })
      if (sequence !== requestSequence.current) {
        return
      }
      activeMessageCursor.current = {
        ticketId,
        cursor: detail.nextMessageCursor,
      }
      setState({
        detail,
        activeTicketId: ticketId,
        hasLoaded: true,
        isLoading: false,
        isLoadingOlder: false,
        error: null,
        olderMessagesError: null,
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
          ...current,
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
      olderController.current?.abort()
      requestSequence.current += 1
    }
  }, [load])

  const loadOlderMessages = useCallback(async (): Promise<void> => {
    if (!isUsableTicketId(ticketId) || olderController.current !== null) {
      return
    }
    const entry = activeMessageCursor.current
    if (entry === null || entry.ticketId !== ticketId || entry.cursor === null) {
      return
    }
    const cursor = entry.cursor

    const sequence = requestSequence.current
    const controller = new AbortController()
    olderController.current = controller

    setState((current) => ({
      ...current,
      isLoadingOlder: true,
      olderMessagesError: null,
    }))

    try {
      const page = await fetchTicketMessages(ticketId, {
        signal: controller.signal,
        pageSize: 100,
        cursor,
      })
      if (
        sequence !== requestSequence.current ||
        controller.signal.aborted ||
        activeMessageCursor.current === null ||
        activeMessageCursor.current.ticketId !== ticketId ||
        activeMessageCursor.current.cursor !== cursor
      ) {
        return
      }

      activeMessageCursor.current = { ticketId, cursor: page.nextCursor }
      setState((current) => {
        if (current.activeTicketId !== ticketId || current.detail === null) {
          return current
        }

        const knownMessageIds = new Set(
          current.detail.messages.map(({ id }) => id),
        )
        const olderMessages = page.messages.filter(({ id }) => {
          if (knownMessageIds.has(id)) {
            return false
          }
          knownMessageIds.add(id)
          return true
        })
        const knownAttachmentIds = new Set(
          current.detail.attachments.map(({ id }) => id),
        )
        const olderAttachments = page.attachments.filter(({ id }) => {
          if (knownAttachmentIds.has(id)) {
            return false
          }
          knownAttachmentIds.add(id)
          return true
        })

        return {
          ...current,
          detail: {
            ...current.detail,
            messages: [...olderMessages, ...current.detail.messages],
            attachments: [
              ...olderAttachments,
              ...current.detail.attachments,
            ],
            nextMessageCursor: page.nextCursor,
            hasMoreMessages: page.hasMore,
          },
          isLoadingOlder: false,
          olderMessagesError: null,
        }
      })
    } catch (error) {
      if (
        sequence !== requestSequence.current ||
        isAbortError(error) ||
        controller.signal.aborted
      ) {
        return
      }
      if (error instanceof ApiError && error.status === 401) {
        return
      }
      setState((current) => ({
        ...current,
        isLoadingOlder: false,
        olderMessagesError: { kind: classifyOlderError(error) },
      }))
    } finally {
      if (olderController.current === controller) {
        olderController.current = null
      }
    }
  }, [ticketId])

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

  const applyAssignment = useCallback((result: AssignTicketResult) => {
    setState((current) => {
      if (current.detail === null || current.detail.id !== result.ticketId) {
        return current
      }

      return {
        ...current,
        detail: {
          ...current.detail,
          assignedUserId: result.assignedUserId,
          updatedAt: result.updatedAt,
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
    isLoadingOlder: forActiveTicket && state.isLoadingOlder,
    error: forActiveTicket ? state.error : null,
    olderMessagesError: forActiveTicket ? state.olderMessagesError : null,
    refresh: load,
    loadOlderMessages,
    applyResolvedTicket,
    applyAssignment,
  }
}
