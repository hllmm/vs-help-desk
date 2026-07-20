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

export type TicketMessageItem = {
  id: string
  senderType: string
  userId: string | null
  content: string
  isHtml: boolean
  createdAt: string
}

export type TicketAttachmentMeta = {
  id: string
  ticketMessageId: string
  fileName: string
  contentType: string
  fileSize: number
  createdAt: string
}

export type TicketDetails = {
  id: string
  ticketNumber: string
  subject: string
  customerName: string
  customerEmail: string
  status: string
  assignedUserId: string | null
  createdAt: string
  updatedAt: string
  lastActivityAt: string
  waitingCustomerSince: string | null
  resolvedAt: string | null
  closedByUserId: string | null
  messages: TicketMessageItem[]
  attachments: TicketAttachmentMeta[]
}

export type SupportReplyResult = {
  ticketId: string
  ticketNumber: string
  messageId: string
  status: string
  emailDelivered: boolean
  ticketStateUpdated: boolean
  noticeCode: string | null
}

export type ResolveTicketResult = {
  ticketId: string
  ticketNumber: string
  status: string
  resolvedAt: string
  updatedAt: string
  lastActivityAt: string
  closedByUserId: string | null
  changed: boolean
}

/** Application parameter (UC-010 / BR-016). Mirrors GET/PUT /api/parameters. */
export type Parameter = {
  key: string
  value: string
  description: string
  updatedAt: string
}
