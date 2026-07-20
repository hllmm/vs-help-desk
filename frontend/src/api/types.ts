/** Mirrors WebAPI JSON (camelCase) contracts. */

export type LoginRequest = {
  username: string
  password: string
}

export type LoginResponse = {
  accessToken: string
  userId: string
  fullName: string
  username: string
}

export type TicketListItem = {
  id: string
  ticketNumber: string
  subject: string
  customerName: string
  customerEmail: string
  status: string
  lastActivityAt: string
  assignedUserId: string | null
}
