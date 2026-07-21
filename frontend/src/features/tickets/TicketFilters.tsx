import type { ChangeEvent, ReactElement } from 'react'
import {
  getTicketFilterLabel,
  TICKET_STATUS_FILTERS,
  type TicketStatusFilter,
} from './ticketListModel'

export type TicketFiltersProps = {
  query: string
  status: TicketStatusFilter
  resultCount: number
  isBusy: boolean
  onQueryChange(value: string): void
  onStatusChange(value: TicketStatusFilter): void
  onRefresh(): void
}

export function TicketFilters(props: TicketFiltersProps): ReactElement {
  const {
    query,
    status,
    resultCount,
    isBusy,
    onQueryChange,
    onStatusChange,
    onRefresh,
  } = props

  function handleStatusChange(event: ChangeEvent<HTMLSelectElement>) {
    onStatusChange(event.target.value as TicketStatusFilter)
  }

  return (
    <div className="ticket-toolbar">
      <div className="ticket-toolbar__search">
        <label className="ticket-field" htmlFor="ticket-search">
          <span className="ticket-field__label">Taleplerde ara</span>
          <input
            id="ticket-search"
            type="search"
            value={query}
            onChange={(event) => onQueryChange(event.target.value)}
            placeholder="Numara, konu veya müşteri ara"
            autoComplete="off"
          />
        </label>
      </div>

      <div className="ticket-toolbar__status">
        <label className="ticket-field" htmlFor="ticket-status">
          <span className="ticket-field__label">Durum</span>
          <select
            id="ticket-status"
            value={status}
            onChange={handleStatusChange}
          >
            {TICKET_STATUS_FILTERS.map((filter) => (
              <option key={filter} value={filter}>
                {getTicketFilterLabel(filter)}
              </option>
            ))}
          </select>
        </label>
      </div>

      <p className="ticket-result-count" aria-live="polite">
        {resultCount} sonuç
      </p>

      <button
        type="button"
        className="button button--quiet ticket-refresh"
        onClick={onRefresh}
        disabled={isBusy}
      >
        {isBusy ? 'Yenileniyor…' : 'Yenile'}
      </button>
    </div>
  )
}
