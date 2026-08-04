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
  phase: 'initial' | 'replacement',
  hasRows = false,
): string {
  if (phase === 'replacement') {
    if (hasRows) {
      return kind === 'network'
        ? 'Destek hizmetine ulaşılamadı. Mevcut listeyi görüntülemeye devam edebilir ve yeniden deneyebilirsiniz.'
        : 'Destek talepleri güncellenemedi. Mevcut listeyi görüntülemeye devam edebilirsiniz.'
    }
    return kind === 'network'
      ? 'Destek hizmetine ulaşılamadı. Yeniden deneyebilirsiniz.'
      : 'Destek talepleri güncellenemedi. Yeniden deneyebilirsiniz.'
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
    hasInitialized,
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
    if (trimmed.length < 2 || trimmed.length > 100) {
      // Out-of-contract lengths stay local (hint copy lives in TicketFilters).
      return
    }

    const timeout = window.setTimeout(() => {
      setServerQuery(trimmed)
    }, 300)
    return () => window.clearTimeout(timeout)
  }, [query])

  const lifecycleCounts: LifecycleCounts = {
    all: counts?.all ?? 0,
    New: counts?.new ?? 0,
    WaitingCustomerReply: counts?.waitingCustomerReply ?? 0,
    CustomerReplied: counts?.customerReplied ?? 0,
    Resolved: counts?.resolved ?? 0,
  }
  const hasRows = tickets.length > 0
  const isInitialLoading = isLoading && !hasInitialized
  const isBusy = isLoading || isLoadingMore
  const showInitialError = error?.source === 'list' && !hasInitialized
  const showRefreshError = error?.source === 'list' && hasInitialized
  const showLoadMoreError = error?.source === 'loadMore'
  const showControls = hasInitialized
  const hasServerFilter = serverQuery.length >= 2 || selectedStatus !== 'all'
  const showTrueEmpty =
    hasInitialized &&
    !isLoading &&
    error === null &&
    !hasRows &&
    !hasServerFilter
  const showFilterEmpty =
    hasInitialized &&
    !isLoading &&
    error === null &&
    !hasRows &&
    hasServerFilter

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
          <p>{listErrorMessage(error.kind, 'initial')}</p>
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
            hasActiveFilters={query.trim() !== '' || selectedStatus !== 'all'}
            onQueryChange={setQuery}
            onStatusChange={setSelectedStatus}
            onClear={() => {
              setQuery('')
              setSelectedStatus('all')
            }}
            onRefresh={() => void refresh()}
          />

          <section
            className="ticket-lifecycle-region"
            aria-labelledby="ticket-lifecycle-title"
          >
            <div className="ticket-lifecycle-region__heading">
              <h2 id="ticket-lifecycle-title">
                Yüklenen taleplerin durum dağılımı
              </h2>
              <p>Sayılar şu anda yüklenen talepleri gösterir.</p>
            </div>
            <div className="ticket-lifecycle-scroll">
              <TicketLifecycleRail
                counts={lifecycleCounts}
                value={selectedStatus}
                onChange={setSelectedStatus}
              />
            </div>
          </section>
        </>
      ) : null}

      {showRefreshError && error ? (
        <div className="ticket-state ticket-state--error" role="alert">
          <p>{listErrorMessage(error.kind, 'replacement', hasRows)}</p>
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
