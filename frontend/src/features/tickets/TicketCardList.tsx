import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'
import type { TicketListItem } from '../../api/types'
import { formatTicketActivity } from './ticketListModel'
import { TicketStatusBadge } from './TicketStatusBadge'

export type TicketCardListProps = {
  tickets: readonly TicketListItem[]
}

function detailPath(ticketId: string): string {
  return `/tickets/${encodeURIComponent(ticketId)}`
}

export function TicketCardList(props: TicketCardListProps): ReactElement {
  const { tickets } = props

  return (
    <ul className="ticket-card-list" aria-label="Destek talepleri">
      {tickets.map((ticket) => (
        <li key={ticket.id}>
          <article className="ticket-card">
            <div className="ticket-card__meta">
              <Link
                to={detailPath(ticket.id)}
                className="ticket-card__primary-link"
                aria-label={`${ticket.ticketNumber} — ${ticket.subject}`}
              >
                <strong className="ticket-number">{ticket.ticketNumber}</strong>
                <h2 className="ticket-card__subject">{ticket.subject}</h2>
              </Link>
              <TicketStatusBadge status={ticket.status} />
            </div>
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
