import { apiRequest } from './client'
import type { TicketListItem } from './types'

export type FetchTicketsOptions = {
  signal?: AbortSignal
}

export function fetchTickets(
  options: FetchTicketsOptions = {},
): Promise<TicketListItem[]> {
  return apiRequest<TicketListItem[]>('/api/tickets', {
    signal: options.signal,
  })
}
