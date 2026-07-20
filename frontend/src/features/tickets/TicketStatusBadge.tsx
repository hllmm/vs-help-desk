import type { ReactElement } from 'react'
import { getTicketStatusMeta } from './ticketListModel'

export type TicketStatusBadgeProps = {
  status: string
}

export function TicketStatusBadge(
  props: TicketStatusBadgeProps,
): ReactElement {
  const meta = getTicketStatusMeta(props.status)
  return (
    <span
      className="ticket-status-badge"
      data-tone={meta.tone}
      title={meta.tone === 'unknown' ? props.status : undefined}
    >
      {meta.label}
    </span>
  )
}
