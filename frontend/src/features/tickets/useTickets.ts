import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { fetchTickets } from '../../api/ticketsApi'
import type { TicketListItem } from '../../api/types'

export type TicketLoadErrorKind = 'network' | 'server'

export type UseTicketsResult = {
  tickets: readonly TicketListItem[]
  hasLoaded: boolean
  isInitialLoading: boolean
  isRefreshing: boolean
  error: TicketLoadErrorKind | null
  refresh: () => Promise<void>
}

type TicketRequestState = {
  tickets: TicketListItem[]
  hasLoaded: boolean
  isLoading: boolean
  error: TicketLoadErrorKind | null
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

export function useTickets(): UseTicketsResult {
  const [state, setState] = useState<TicketRequestState>({
    tickets: [],
    hasLoaded: false,
    isLoading: false,
    error: null,
  })
  const requestSequence = useRef(0)
  const activeController = useRef<AbortController | null>(null)

  const load = useCallback(async () => {
    const sequence = ++requestSequence.current
    activeController.current?.abort()
    const controller = new AbortController()
    activeController.current = controller
    setState((current) => ({ ...current, isLoading: true, error: null }))

    try {
      const tickets = await fetchTickets({ signal: controller.signal })
      if (sequence === requestSequence.current) {
        setState({
          tickets,
          hasLoaded: true,
          isLoading: false,
          error: null,
        })
      }
    } catch (error) {
      if (sequence !== requestSequence.current || isAbortError(error)) {
        return
      }
      if (error instanceof ApiError && error.status === 401) {
        return
      }
      setState((current) => ({
        ...current,
        hasLoaded: true,
        isLoading: false,
        error: error instanceof TypeError ? 'network' : 'server',
      }))
    }
  }, [])

  useEffect(() => {
    void load()
    return () => {
      activeController.current?.abort()
      requestSequence.current += 1
    }
  }, [load])

  return {
    tickets: state.tickets,
    hasLoaded: state.hasLoaded,
    isInitialLoading: state.isLoading && !state.hasLoaded,
    isRefreshing: state.isLoading && state.hasLoaded,
    error: state.error,
    refresh: load,
  }
}
