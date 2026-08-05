import type { ReactElement } from 'react'
import { Link } from 'react-router'
import type { TicketListItem } from '../../api/types'
import { formatTicketActivity } from './ticketListModel'
import { TicketStatusBadge } from './TicketStatusBadge'

export type TicketTableProps = {
  tickets: readonly TicketListItem[]
}

function detailPath(ticketId: string): string {
  return `/tickets/${encodeURIComponent(ticketId)}`
}

export function TicketTable(props: TicketTableProps): ReactElement {
  const { tickets } = props

  return (
    <div className="ticket-table-view">
      <table className="ticket-table">
        <caption className="visually-hidden">Destek talepleri</caption>
        <thead>
          <tr>
            <th scope="col">Numara</th>
            <th scope="col">Konu</th>
            <th scope="col">Müşteri</th>
            <th scope="col">Durum</th>
            <th scope="col">Son hareket</th>
          </tr>
        </thead>
        <tbody>
          {tickets.map((ticket) => {
            const path = detailPath(ticket.id)
            return (
              <tr key={ticket.id}>
                <td className="ticket-number" data-label="Numara">
                  <Link
                    to={path}
                    className="ticket-link"
                    aria-label={`${ticket.ticketNumber} talebini aç`}
                  >
                    {ticket.ticketNumber}
                  </Link>
                </td>
                <td data-label="Konu">
                  <Link
                    to={path}
                    className="ticket-link"
                    aria-label={`${ticket.ticketNumber}: ${ticket.subject}`}
                  >
                    {ticket.subject}
                  </Link>
                </td>
                <td data-label="Müşteri">
                  <span className="ticket-customer">
                    <strong>{ticket.customerName}</strong>
                    <span className="ticket-customer-email">
                      {ticket.customerEmail}
                    </span>
                  </span>
                </td>
                <td data-label="Durum">
                  <TicketStatusBadge status={ticket.status} />
                </td>
                <td data-label="Son hareket">
                  <time dateTime={ticket.lastActivityAt}>
                    {formatTicketActivity(ticket.lastActivityAt)}
                  </time>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
