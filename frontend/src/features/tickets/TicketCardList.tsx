import type { ReactElement } from 'react'
import type { TicketListItem } from '../../api/types'
import { formatTicketActivity } from './ticketListModel'
import { TicketStatusBadge } from './TicketStatusBadge'

export type TicketCardListProps = {
  tickets: readonly TicketListItem[]
}

export function TicketCardList(props: TicketCardListProps): ReactElement {
  const { tickets } = props

  return (
    <ul className="ticket-card-list" aria-label="Destek talepleri">
      {tickets.map((ticket) => (
        <li key={ticket.id}>
          <article className="ticket-card">
            <div className="ticket-card__meta">
              <strong className="ticket-number">{ticket.ticketNumber}</strong>
              <TicketStatusBadge status={ticket.status} />
            </div>
            <h2>{ticket.subject}</h2>
            <p>{ticket.customerName}</p>
            <p className="ticket-card__email">{ticket.customerEmail}</p>
            <time dateTime={ticket.lastActivityAt}>
              {formatTicketActivity(ticket.lastActivityAt)}
            </time>
          </article>
        </li>
      ))}
    </ul>
  )
}
