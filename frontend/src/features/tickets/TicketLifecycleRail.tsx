import type { ReactElement } from 'react'
import {
  getTicketFilterLabel,
  TICKET_STATUS_FILTERS,
  type LifecycleCounts,
  type TicketStatusFilter,
} from './ticketListModel'

export type TicketLifecycleRailProps = {
  counts: LifecycleCounts
  value: TicketStatusFilter
  onChange(value: TicketStatusFilter): void
}

export function TicketLifecycleRail(
  props: TicketLifecycleRailProps,
): ReactElement {
  const { counts, value, onChange } = props

  return (
    <div
      className="ticket-lifecycle"
      role="group"
      aria-label="Destek talebi durumları"
    >
      {TICKET_STATUS_FILTERS.map((filter) => (
        <button
          key={filter}
          type="button"
          className="ticket-lifecycle__segment"
          data-status={filter}
          aria-pressed={value === filter}
          onClick={() => onChange(filter)}
        >
          <span className="ticket-lifecycle__label">
            {getTicketFilterLabel(filter)}
          </span>
          <strong className="ticket-lifecycle__count">{counts[filter]}</strong>
        </button>
      ))}
    </div>
  )
}
