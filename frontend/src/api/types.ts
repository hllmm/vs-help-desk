/** Mirrors WebAPI JSON (camelCase) contracts. */

export type LoginRequest = {
  username: string
  password: string
}

export type UserRole = 'Support' | 'Admin'

/** UC-001 login body — JWT is HttpOnly cookie; no accessToken. */
export type LoginResponse = {
  userId: string
  fullName: string
  username: string
  role: UserRole
}

/** GET /api/auth/me — mirrors CurrentUserResponse. */
export type CurrentUser = {
  userId: string
  fullName: string
  username: string
  role: UserRole
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

/** Active internal user eligible for BR-011 ticket assignment. */
export type AssignableUser = {
  id: string
  fullName: string
  username: string
}

/** PUT /api/tickets/{id}/assignee response. */
export type AssignTicketResult = {
  ticketId: string
  assignedUserId: string | null
  updatedAt: string
  changed: boolean
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

/** Parameter change audit row. Mirrors GET /api/parameters/audit. */
export type ParameterChangeLog = {
  id: string
  parameterKey: string
  oldValue: string
  newValue: string
  changedByUserId: string
  changedByUsername: string | null
  changedAt: string
}

/** GET /api/users list item — no password hash. */
export type UserListItem = {
  id: string
  fullName: string
  username: string
  email: string
  role: UserRole
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
}

/** POST /api/users body. */
export type CreateUserRequest = {
  fullName: string
  username: string
  email: string
  password: string
  role: UserRole
}

/** PUT /api/users/{id} body — username is fixed in v1. */
export type UpdateUserRequest = {
  fullName: string
  email: string
  role: UserRole
  isActive: boolean
}

/** POST /api/users/{id}/password body. */
export type SetUserPasswordRequest = {
  password: string
}
