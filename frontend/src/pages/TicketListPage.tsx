import { useState, type ReactElement } from 'react'
import { TicketCardList } from '../features/tickets/TicketCardList'
import { TicketFilters } from '../features/tickets/TicketFilters'
import { TicketLifecycleRail } from '../features/tickets/TicketLifecycleRail'
import {
  countTicketsByStatus,
  filterTicketsByStatus,
  searchTickets,
  type TicketStatusFilter,
} from '../features/tickets/ticketListModel'
import { TicketTable } from '../features/tickets/TicketTable'
import { useTickets } from '../features/tickets/useTickets'

function errorMessage(
  kind: 'network' | 'server',
  hasRows: boolean,
): string {
  if (hasRows) {
    return kind === 'network'
      ? 'Destek hizmetine ulaşılamadı. Mevcut listeyi görüntülemeye devam edebilir ve yeniden deneyebilirsiniz.'
      : 'Destek talepleri güncellenemedi. Mevcut listeyi görüntülemeye devam edebilirsiniz.'
  }

  return kind === 'network'
    ? 'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.'
    : 'Destek talepleri yüklenemedi. Lütfen yeniden deneyin.'
}

export function TicketListPage(): ReactElement {
  const {
    tickets,
    hasLoaded,
    isInitialLoading,
    isRefreshing,
    error,
    refresh,
  } = useTickets()
  const [query, setQuery] = useState('')
  const [selectedStatus, setSelectedStatus] =
    useState<TicketStatusFilter>('all')

  const searchedTickets = searchTickets(tickets, query)
  const lifecycleCounts = countTicketsByStatus(searchedTickets)
  const visibleTickets = filterTicketsByStatus(
    searchedTickets,
    selectedStatus,
  )

  const isBusy = isInitialLoading || isRefreshing
  const hasRows = tickets.length > 0
  const showWorkspaceControls = (hasLoaded && error === null) || hasRows
  const showTrueEmpty =
    hasLoaded && error === null && tickets.length === 0
  const showFilterEmpty =
    hasLoaded &&
    error === null &&
    tickets.length > 0 &&
    visibleTickets.length === 0
  const showResults =
    hasLoaded && (error === null || hasRows) && visibleTickets.length > 0
  const showInitialError = hasLoaded && error !== null && !hasRows
  const showRefreshError = error !== null && hasRows

  return (
    <section
      className="ticket-workspace"
      aria-labelledby="ticket-list-title"
      aria-busy={isBusy}
      role="region"
    >
      <header className="ticket-workspace__header">
        <div>
          <h1 id="ticket-list-title">Destek talepleri</h1>
          <p className="ticket-workspace__lede">
            Son hareketi en yeni olan talepler önce gösterilir.
          </p>
        </div>
      </header>

      {isInitialLoading ? (
        <p className="ticket-state ticket-state--loading" role="status">
          Destek talepleri yükleniyor…
        </p>
      ) : null}

      {showInitialError && error ? (
        <div className="ticket-state ticket-state--error" role="alert">
          <p>{errorMessage(error, false)}</p>
          <button
            type="button"
            className="button button--primary"
            onClick={() => void refresh()}
          >
            Yeniden dene
          </button>
        </div>
      ) : null}

      {showWorkspaceControls ? (
        <>
          <TicketFilters
            query={query}
            status={selectedStatus}
            resultCount={visibleTickets.length}
            isBusy={isBusy}
            onQueryChange={setQuery}
            onStatusChange={setSelectedStatus}
            onRefresh={() => void refresh()}
          />

          <div className="ticket-lifecycle-scroll">
            <TicketLifecycleRail
              counts={lifecycleCounts}
              value={selectedStatus}
              onChange={setSelectedStatus}
            />
          </div>
        </>
      ) : null}

      {showRefreshError && error ? (
        <div className="ticket-state ticket-state--error" role="alert">
          <p>{errorMessage(error, true)}</p>
        </div>
      ) : null}

      {showTrueEmpty ? (
        <div className="ticket-state ticket-state--empty">
          <p>Henüz destek talebi yok.</p>
          <p className="ticket-state__hint">
            Yeni e-postalar geldiğinde destek talepleri burada görünür.
          </p>
        </div>
      ) : null}

      {showFilterEmpty ? (
        <div className="ticket-state ticket-state--empty">
          <p>Aramanızla eşleşen destek talebi bulunamadı.</p>
          <p className="ticket-state__hint">
            Arama metnini veya durum filtresini değiştirin.
          </p>
        </div>
      ) : null}

      {showResults ? (
        <div className="ticket-results">
          <TicketTable tickets={visibleTickets} />
          <TicketCardList tickets={visibleTickets} />
        </div>
      ) : null}
    </section>
  )
}
