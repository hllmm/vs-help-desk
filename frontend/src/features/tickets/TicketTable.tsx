import type { ReactElement } from 'react'
import type { TicketListItem } from '../../api/types'
import { formatTicketActivity } from './ticketListModel'
import { TicketStatusBadge } from './TicketStatusBadge'

export type TicketTableProps = {
  tickets: readonly TicketListItem[]
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
          {tickets.map((ticket) => (
            <tr key={ticket.id}>
              <td className="ticket-number">{ticket.ticketNumber}</td>
              <td>{ticket.subject}</td>
              <td>
                <strong>{ticket.customerName}</strong>
                <span className="ticket-customer-email">
                  {ticket.customerEmail}
                </span>
              </td>
              <td>
                <TicketStatusBadge status={ticket.status} />
              </td>
              <td>
                <time dateTime={ticket.lastActivityAt}>
                  {formatTicketActivity(ticket.lastActivityAt)}
                </time>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
