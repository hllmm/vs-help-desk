import { apiRequest } from './client'
import type { TicketListItem } from './types'

export function fetchTickets(status?: string): Promise<TicketListItem[]> {
  const query = status ? `?status=${encodeURIComponent(status)}` : ''
  return apiRequest<TicketListItem[]>(`/api/tickets${query}`)
}
