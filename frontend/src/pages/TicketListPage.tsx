import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../api/client'
import { fetchTickets } from '../api/ticketsApi'
import type { TicketListItem } from '../api/types'

type LoadState =
  | { kind: 'loading' }
  | { kind: 'empty' }
  | { kind: 'error'; message: string }
  | { kind: 'ready'; items: TicketListItem[] }

function formatActivity(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) {
    return iso
  }
  return date.toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
}

function statusClass(status: string): string {
  switch (status) {
    case 'New':
      return 'badge badge-new'
    case 'WaitingCustomerReply':
      return 'badge badge-waiting'
    case 'CustomerReplied':
      return 'badge badge-replied'
    case 'Resolved':
      return 'badge badge-resolved'
    default:
      return 'badge'
  }
}

function humanStatus(status: string): string {
  switch (status) {
    case 'WaitingCustomerReply':
      return 'Waiting customer'
    case 'CustomerReplied':
      return 'Customer replied'
    default:
      return status
  }
}

export function TicketListPage() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })

  const load = useCallback(async () => {
    setState({ kind: 'loading' })
    try {
      const items = await fetchTickets()
      if (items.length === 0) {
        setState({ kind: 'empty' })
      } else {
        setState({ kind: 'ready', items })
      }
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        // client redirects to /login
        return
      }
      setState({
        kind: 'error',
        message: error instanceof Error ? error.message : 'Failed to load tickets.',
      })
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  return (
    <section className="page">
      <div className="page-header">
        <div>
          <h1>Tickets</h1>
          <p className="muted">Ordered by last activity (UC-003).</p>
        </div>
        <button type="button" className="btn btn-ghost" onClick={() => void load()}>
          Refresh
        </button>
      </div>

      {state.kind === 'loading' ? (
        <p className="state-message" aria-live="polite">
          Loading tickets…
        </p>
      ) : null}

      {state.kind === 'empty' ? (
        <div className="state-panel">
          <p>No tickets yet.</p>
          <p className="muted">New mail creates tickets via the process-incoming job.</p>
        </div>
      ) : null}

      {state.kind === 'error' ? (
        <div className="state-panel alert alert-error" role="alert">
          <p>{state.message}</p>
          <button type="button" className="btn btn-primary" onClick={() => void load()}>
            Retry
          </button>
        </div>
      ) : null}

      {state.kind === 'ready' ? (
        <div className="table-wrap">
          <table className="ticket-table">
            <thead>
              <tr>
                <th>Number</th>
                <th>Subject</th>
                <th>Customer</th>
                <th>Status</th>
                <th>Last activity</th>
              </tr>
            </thead>
            <tbody>
              {state.items.map((ticket) => (
                <tr key={ticket.id}>
                  <td>
                    <span className="ticket-link">{ticket.ticketNumber}</span>
                  </td>
                  <td>{ticket.subject}</td>
                  <td>
                    <div>{ticket.customerName}</div>
                    <div className="muted small">{ticket.customerEmail}</div>
                  </td>
                  <td>
                    <span className={statusClass(ticket.status)}>
                      {humanStatus(ticket.status)}
                    </span>
                  </td>
                  <td>{formatActivity(ticket.lastActivityAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  )
}
