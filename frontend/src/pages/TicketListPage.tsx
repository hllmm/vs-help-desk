import { useEffect, useState, type ReactElement } from 'react'
import { TicketFilters } from '../features/tickets/TicketFilters'
import { TicketLifecycleRail } from '../features/tickets/TicketLifecycleRail'
import type {
  LifecycleCounts,
  TicketStatusFilter,
} from '../features/tickets/ticketListModel'
import { TicketTable } from '../features/tickets/TicketTable'
import {
  useTickets,
  type TicketLoadErrorKind,
} from '../features/tickets/useTickets'

function listErrorMessage(
  kind: TicketLoadErrorKind,
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

function countForStatus(
  counts: LifecycleCounts,
  status: TicketStatusFilter,
): number {
  return counts[status]
}

export function TicketListPage(): ReactElement {
  const [query, setQuery] = useState('')
  const [serverQuery, setServerQuery] = useState('')
  const [selectedStatus, setSelectedStatus] =
    useState<TicketStatusFilter>('all')
  const {
    tickets,
    counts,
    hasMore,
    isLoading,
    isLoadingMore,
    error,
    loadMore,
    refresh,
  } = useTickets({
    query: serverQuery,
    status: selectedStatus === 'all' ? 'All' : selectedStatus,
  })

  useEffect(() => {
    const trimmed = query.trim()
    if (trimmed.length === 0) {
      setServerQuery('')
      return
    }
    if (trimmed.length < 2) {
      return
    }

    const timeout = window.setTimeout(() => {
      setServerQuery(trimmed)
    }, 300)
    return () => window.clearTimeout(timeout)
  }, [query])

  const lifecycleCounts: LifecycleCounts = {
    all: counts.all,
    New: counts.new,
    WaitingCustomerReply: counts.waitingCustomerReply,
    CustomerReplied: counts.customerReplied,
    Resolved: counts.resolved,
  }
  const hasRows = tickets.length > 0
  const isInitialLoading = isLoading && !hasRows
  const isBusy = isLoading || isLoadingMore
  const showInitialError = error?.source === 'list' && !hasRows
  const showRefreshError = error?.source === 'list' && hasRows
  const showLoadMoreError = error?.source === 'loadMore'
  const showControls = !isInitialLoading && !showInitialError
  const hasServerFilter = serverQuery.length >= 2 || selectedStatus !== 'all'
  const showTrueEmpty =
    !isLoading && error === null && !hasRows && !hasServerFilter
  const showFilterEmpty =
    !isLoading && error === null && !hasRows && hasServerFilter

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
          <p>{listErrorMessage(error.kind, false)}</p>
          <button
            type="button"
            className="button button--primary"
            onClick={() => void refresh()}
          >
            Yeniden dene
          </button>
        </div>
      ) : null}

      {showControls ? (
        <>
          <TicketFilters
            query={query}
            status={selectedStatus}
            resultCount={countForStatus(lifecycleCounts, selectedStatus)}
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
          <p>{listErrorMessage(error.kind, true)}</p>
          <button
            type="button"
            className="button button--quiet"
            onClick={() => void refresh()}
          >
            Yeniden dene
          </button>
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

      {hasRows ? (
        <div className="ticket-results">
          <TicketTable tickets={tickets} />

          {showLoadMoreError ? (
            <div
              className="ticket-state ticket-state--error ticket-pagination"
              role="alert"
            >
              <p>Daha fazla destek talebi yüklenemedi. Mevcut liste korunuyor.</p>
              <button
                type="button"
                className="button button--quiet"
                onClick={() => void loadMore()}
              >
                Yeniden dene
              </button>
            </div>
          ) : hasMore ? (
            <div className="ticket-pagination">
              <button
                type="button"
                className="button button--quiet"
                disabled={isLoadingMore}
                onClick={() => void loadMore()}
              >
                {isLoadingMore ? 'Yükleniyor…' : 'Daha fazla yükle'}
              </button>
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
