import { apiRequest } from './client'
import type {
  ResolveTicketResult,
  SupportReplyResult,
  TicketDetails,
  TicketListItem,
} from './types'

export type FetchTicketsOptions = {
  signal?: AbortSignal
}

export type TicketMutationOptions = {
  signal?: AbortSignal
}

export function fetchTickets(
  options: FetchTicketsOptions = {},
): Promise<TicketListItem[]> {
  return apiRequest<TicketListItem[]>('/api/tickets', {
    signal: options.signal,
  })
}

export function fetchTicketDetails(
  ticketId: string,
  options: TicketMutationOptions = {},
): Promise<TicketDetails> {
  return apiRequest<TicketDetails>(
    `/api/tickets/${encodeURIComponent(ticketId)}`,
    { signal: options.signal },
  )
}

export function replyToTicket(
  ticketId: string,
  request: { content: string },
  options: TicketMutationOptions = {},
): Promise<SupportReplyResult> {
  return apiRequest<SupportReplyResult>(
    `/api/tickets/${encodeURIComponent(ticketId)}/replies`,
    {
      method: 'POST',
      body: { content: request.content },
      signal: options.signal,
    },
  )
}

export function resolveTicket(
  ticketId: string,
  options: { signal?: AbortSignal } = {},
): Promise<ResolveTicketResult> {
  return apiRequest<ResolveTicketResult>(
    `/api/tickets/${encodeURIComponent(ticketId)}/resolve`,
    { method: 'POST', signal: options.signal },
  )
}
