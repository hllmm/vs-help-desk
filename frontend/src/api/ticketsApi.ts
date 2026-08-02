import { apiRequest } from './client'
import type {
  AssignableUser,
  AssignTicketResult,
  ResolveTicketResult,
  SupportReplyResult,
  TicketDetails,
  TicketListPage,
  TicketMessagePage,
  TicketStatus,
} from './types'

export type FetchTicketsOptions = {
  signal?: AbortSignal
  pageSize?: number
  search?: string
  status?: TicketStatus
  cursor?: string
}

export type FetchTicketMessagesOptions = {
  signal?: AbortSignal
  pageSize?: number
  cursor?: string
}

export type TicketMutationOptions = {
  signal?: AbortSignal
}

export function fetchTickets(
  options: FetchTicketsOptions = {},
): Promise<TicketListPage> {
  const params = new URLSearchParams()
  if (options.pageSize !== undefined) {
    params.set('pageSize', String(options.pageSize))
  }
  if (options.search) {
    params.set('search', options.search)
  }
  if (options.status) {
    params.set('status', options.status)
  }
  if (options.cursor) {
    params.set('cursor', options.cursor)
  }
  const query = params.toString()

  return apiRequest<TicketListPage>(`/api/tickets${query ? `?${query}` : ''}`, {
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

export function fetchTicketMessages(
  ticketId: string,
  options: FetchTicketMessagesOptions = {},
): Promise<TicketMessagePage> {
  const params = new URLSearchParams()
  if (options.pageSize !== undefined) {
    params.set('pageSize', String(options.pageSize))
  }
  if (options.cursor) {
    params.set('cursor', options.cursor)
  }
  const query = params.toString()

  return apiRequest<TicketMessagePage>(
    `/api/tickets/${encodeURIComponent(ticketId)}/messages${query ? `?${query}` : ''}`,
    { signal: options.signal },
  )
}

export function fetchAssignableUsers(
  options: FetchTicketsOptions = {},
): Promise<AssignableUser[]> {
  return apiRequest<AssignableUser[]>('/api/tickets/assignees', {
    signal: options.signal,
  })
}

export function assignTicket(
  ticketId: string,
  userId: string | null,
  options: TicketMutationOptions = {},
): Promise<AssignTicketResult> {
  return apiRequest<AssignTicketResult>(
    `/api/tickets/${encodeURIComponent(ticketId)}/assignee`,
    {
      method: 'PUT',
      body: { userId },
      signal: options.signal,
    },
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
