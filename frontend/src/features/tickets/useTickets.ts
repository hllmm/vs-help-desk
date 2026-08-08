import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { fetchTickets } from '../../api/ticketsApi'
import type {
  TicketListItem,
  TicketStatus,
  TicketStatusCounts,
} from '../../api/types'

export type TicketLoadErrorKind = 'network' | 'server'
export type TicketLoadError = {
  kind: TicketLoadErrorKind
  source: 'list' | 'loadMore'
}

export type UseTicketsOptions = {
  query: string
  status: TicketStatus | 'All'
}

export type UseTicketsResult = {
  tickets: readonly TicketListItem[]
  counts: TicketStatusCounts
  hasMore: boolean
  hasInitialized: boolean
  isLoading: boolean
  isLoadingMore: boolean
  error: TicketLoadError | null
  loadMore: () => Promise<void>
  refresh: () => Promise<void>
}

type TicketRequestState = {
  tickets: TicketListItem[]
  counts: TicketStatusCounts
  hasMore: boolean
  hasInitialized: boolean
  isLoading: boolean
  isLoadingMore: boolean
  error: TicketLoadError | null
}

const EMPTY_COUNTS: TicketStatusCounts = {
  all: 0,
  new: 0,
  waitingCustomerReply: 0,
  customerReplied: 0,
  resolved: 0,
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function classifyError(error: unknown, source: TicketLoadError['source']): TicketLoadError {
  return {
    kind: error instanceof TypeError ? 'network' : 'server',
    source,
  }
}

function validSearch(query: string): string | undefined {
  const trimmed = query.trim()
  return trimmed.length >= 2 ? trimmed : undefined
}

export function useTickets(options: UseTicketsOptions): UseTicketsResult {
  const search = validSearch(options.query)
  const status = options.status === 'All' ? undefined : options.status
  const [state, setState] = useState<TicketRequestState>({
    tickets: [],
    counts: EMPTY_COUNTS,
    hasMore: false,
    hasInitialized: false,
    isLoading: true,
    isLoadingMore: false,
    error: null,
  })
  const requestSequence = useRef(0)
  const activeController = useRef<AbortController | null>(null)
  const activeCursor = useRef<string | null>(null)
  const appendOwner = useRef<AbortController | null>(null)

  const replace = useCallback(
    async (preserveTickets: boolean) => {
      const sequence = ++requestSequence.current
      activeController.current?.abort()
      appendOwner.current = null
      activeCursor.current = null
      const controller = new AbortController()
      activeController.current = controller
      const startedAt = Date.now()
      setState((current) => ({
        tickets: preserveTickets ? current.tickets : [],
        counts: preserveTickets ? current.counts : EMPTY_COUNTS,
        hasMore: false,
        hasInitialized: current.hasInitialized,
        isLoading: true,
        isLoadingMore: false,
        error: null,
      }))

      try {
        const page = await fetchTickets({
          signal: controller.signal,
          pageSize: 50,
          search,
          status,
        })
        if (sequence !== requestSequence.current || controller.signal.aborted) {
          return
        }
        activeCursor.current = page.nextCursor
        const elapsed = Date.now() - startedAt
        const minVisible = preserveTickets ? 420 : 0
        const delay = Math.max(0, minVisible - elapsed)
        if (delay > 0) {
          await new Promise<void>((resolve) => {
            const t = setTimeout(resolve, delay)
            controller.signal.addEventListener('abort', () => clearTimeout(t), { once: true })
          })
          if (sequence !== requestSequence.current || controller.signal.aborted) {
            return
          }
        }
        setState({
          tickets: page.items,
          counts: page.counts,
          hasMore: page.hasMore,
          hasInitialized: true,
          isLoading: false,
          isLoadingMore: false,
          error: null,
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
          isLoading: false,
          error: classifyError(error, 'list'),
        }))
      } finally {
        if (activeController.current === controller) {
          activeController.current = null
        }
      }
    },
    [search, status],
  )

  useEffect(() => {
    const id = setTimeout(() => void replace(true), 0)
    return () => {
      clearTimeout(id)
      activeController.current?.abort()
      requestSequence.current += 1
      appendOwner.current = null
    }
  }, [replace])

  const loadMore = useCallback(async () => {
    const cursor = activeCursor.current
    if (!cursor || appendOwner.current !== null) {
      return
    }

    const sequence = requestSequence.current
    const controller = new AbortController()
    appendOwner.current = controller
    activeController.current = controller
    setState((current) => ({
      ...current,
      isLoadingMore: true,
      error: null,
    }))

    try {
      const page = await fetchTickets({
        signal: controller.signal,
        pageSize: 50,
        search,
        status,
        cursor,
      })
      if (
        sequence !== requestSequence.current ||
        activeCursor.current !== cursor ||
        controller.signal.aborted
      ) {
        return
      }

      activeCursor.current = page.nextCursor
      setState((current) => {
        const knownIds = new Set(current.tickets.map(({ id }) => id))
        const uniqueItems = page.items.filter(({ id }) => {
          if (knownIds.has(id)) {
            return false
          }
          knownIds.add(id)
          return true
        })
        return {
          tickets: [...current.tickets, ...uniqueItems],
          counts: page.counts,
          hasMore: page.hasMore,
          hasInitialized: current.hasInitialized,
          isLoading: false,
          isLoadingMore: false,
          error: null,
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
        isLoadingMore: false,
        error: classifyError(error, 'loadMore'),
      }))
    } finally {
      if (activeController.current === controller) {
        activeController.current = null
      }
      if (appendOwner.current === controller) {
        appendOwner.current = null
      }
    }
  }, [search, status])

  const refresh = useCallback(() => replace(true), [replace])

  return {
    tickets: state.tickets,
    counts: state.counts,
    hasMore: state.hasMore,
    hasInitialized: state.hasInitialized,
    isLoading: state.isLoading,
    isLoadingMore: state.isLoadingMore,
    error: state.error,
    loadMore,
    refresh,
  }
}
