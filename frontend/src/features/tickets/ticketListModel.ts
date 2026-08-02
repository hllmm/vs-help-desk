export const TICKET_STATUS_FILTERS = [
  'all',
  'New',
  'WaitingCustomerReply',
  'CustomerReplied',
  'Resolved',
] as const

export type TicketStatusFilter =
  (typeof TICKET_STATUS_FILTERS)[number]
export type KnownTicketStatus = Exclude<TicketStatusFilter, 'all'>
export type TicketStatusTone =
  | 'new'
  | 'waiting'
  | 'replied'
  | 'resolved'
  | 'unknown'
export type TicketStatusMeta = {
  label: string
  tone: TicketStatusTone
}
export type LifecycleCounts = Record<TicketStatusFilter, number>

const KNOWN_STATUS_META: Record<KnownTicketStatus, TicketStatusMeta> = {
  New: { label: 'Yeni', tone: 'new' },
  WaitingCustomerReply: { label: 'Müşteri Bekleniyor', tone: 'waiting' },
  CustomerReplied: { label: 'Müşteri Yanıtladı', tone: 'replied' },
  Resolved: { label: 'Çözüldü', tone: 'resolved' },
}

const FILTER_LABELS: Record<TicketStatusFilter, string> = {
  all: 'Tümü',
  New: 'Yeni',
  WaitingCustomerReply: 'Müşteri Bekleniyor',
  CustomerReplied: 'Müşteri Yanıtladı',
  Resolved: 'Çözüldü',
}

function isKnownTicketStatus(status: string): status is KnownTicketStatus {
  return Object.hasOwn(KNOWN_STATUS_META, status)
}

export function getTicketStatusMeta(status: string): TicketStatusMeta {
  if (isKnownTicketStatus(status)) {
    return KNOWN_STATUS_META[status]
  }
  // Keep raw value for operators via title attributes in UI; visible label is Turkish.
  return { label: 'Bilinmeyen durum', tone: 'unknown' }
}

export function getTicketFilterLabel(
  filter: TicketStatusFilter,
): string {
  return FILTER_LABELS[filter]
}

const activityFormatter = new Intl.DateTimeFormat('tr-TR', {
  dateStyle: 'medium',
  timeStyle: 'short',
})

export function formatTicketActivity(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) {
    return iso
  }
  return activityFormatter.format(date)
}
