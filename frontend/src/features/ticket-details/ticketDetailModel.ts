import type { TicketAttachmentMeta } from '../../api/types'

export function groupAttachmentsByMessage(
  attachments: readonly TicketAttachmentMeta[],
): ReadonlyMap<string, readonly TicketAttachmentMeta[]> {
  const groups = new Map<string, TicketAttachmentMeta[]>()

  for (const attachment of attachments) {
    const existing = groups.get(attachment.ticketMessageId)
    if (existing) {
      existing.push(attachment)
    } else {
      groups.set(attachment.ticketMessageId, [attachment])
    }
  }

  return groups
}

export function getMessageSenderLabel(senderType: string): string {
  switch (senderType) {
    case 'Customer':
      return 'Müşteri'
    case 'Support':
      return 'Destek ekibi'
    default:
      return 'Gönderen bilgisi yok'
  }
}

const detailDateFormatter = new Intl.DateTimeFormat('tr-TR', {
  dateStyle: 'medium',
  timeStyle: 'short',
})

export function formatTicketDetailDate(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) {
    return iso
  }
  return detailDateFormatter.format(date)
}

function formatSizeNumber(value: number): string {
  return Number.isInteger(value)
    ? String(value)
    : value.toLocaleString('tr-TR', {
        maximumFractionDigits: 1,
        minimumFractionDigits: 0,
      })
}

export function formatAttachmentSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) {
    return '—'
  }

  if (bytes < 1024) {
    return `${Math.round(bytes)} B`
  }

  if (bytes < 1024 * 1024) {
    return `${formatSizeNumber(bytes / 1024)} KiB`
  }

  return `${formatSizeNumber(bytes / (1024 * 1024))} MiB`
}
