import { useMemo, type ReactElement } from 'react'
import { Link, useParams } from 'react-router-dom'
import { TicketReplyForm } from '../features/ticket-details/TicketReplyForm'
import { TicketAssignmentPanel } from '../features/ticket-details/TicketAssignmentPanel'
import { TicketResolutionPanel } from '../features/ticket-details/TicketResolutionPanel'
import { TicketTimeline } from '../features/ticket-details/TicketTimeline'
import {
  formatTicketDetailDate,
  groupAttachmentsByMessage,
} from '../features/ticket-details/ticketDetailModel'
import { useAttachmentDownload } from '../features/ticket-details/useAttachmentDownload'
import { useTicketDetails } from '../features/ticket-details/useTicketDetails'
import { TicketStatusBadge } from '../features/tickets/TicketStatusBadge'

function initialErrorMessage(kind: 'network' | 'not-found' | 'server'): string {
  switch (kind) {
    case 'not-found':
      return 'Destek talebi bulunamadı.'
    case 'network':
      return 'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.'
    case 'server':
      return 'Talep ayrıntıları yüklenemedi. Lütfen yeniden deneyin.'
  }
}

function downloadErrorMessage(
  kind: 'not-found' | 'network' | 'server',
): string {
  switch (kind) {
    case 'not-found':
      return 'Ek dosya bulunamadı.'
    case 'network':
      return 'Ek dosya indirilemedi. Bağlantınızı kontrol edip yeniden deneyin.'
    case 'server':
      return 'Ek dosya indirilemedi. Lütfen yeniden deneyin.'
  }
}

function olderMessagesErrorMessage(kind: 'network' | 'server'): string {
  switch (kind) {
    case 'network':
      return 'Daha eski mesajlar yüklenemedi. Bağlantınızı kontrol edip yeniden deneyin.'
    case 'server':
      return 'Daha eski mesajlar yüklenemedi. Lütfen yeniden deneyin.'
  }
}

export function TicketDetailPage(): ReactElement {
  const { ticketId } = useParams<{ ticketId: string }>()
  const {
    detail,
    hasLoaded,
    isInitialLoading,
    isRefreshing,
    isLoadingOlder,
    error,
    olderMessagesError,
    refresh,
    loadOlderMessages,
    applyResolvedTicket,
    applyAssignment,
  } = useTicketDetails(ticketId)
  const {
    activeAttachmentId,
    error: downloadError,
    download,
    clearError: clearDownloadError,
  } = useAttachmentDownload()

  const attachmentsByMessage = useMemo(
    () => groupAttachmentsByMessage(detail?.attachments ?? []),
    [detail?.attachments],
  )

  const isBusy = isInitialLoading || isRefreshing
  const showInitialError = hasLoaded && error !== null && detail === null
  const showRefreshError = error !== null && detail !== null
  const showReady = detail !== null

  return (
    <section
      className="ticket-detail"
      aria-labelledby={showReady ? 'ticket-detail-title' : undefined}
      aria-busy={isBusy}
      role="region"
    >
      <p className="ticket-detail__back">
        <Link to="/tickets" className="ticket-detail__back-link">
          Destek taleplerine dön
        </Link>
      </p>

      {isInitialLoading ? (
        <p className="ticket-state ticket-state--loading" role="status">
          Talep ayrıntıları yükleniyor…
        </p>
      ) : null}

      {showInitialError && error ? (
        <div className="ticket-state ticket-state--error" role="alert">
          <p>{initialErrorMessage(error.kind)}</p>
          {error.kind !== 'not-found' ? (
            <button
              type="button"
              className="button button--primary"
              onClick={() => void refresh()}
            >
              Yeniden dene
            </button>
          ) : null}
        </div>
      ) : null}

      {showReady && detail ? (
        <div className="ticket-detail__layout">
          <div className="ticket-detail__main">
            <header className="ticket-detail__header">
              <div className="ticket-detail__identity">
                <p className="ticket-detail__number">{detail.ticketNumber}</p>
                <TicketStatusBadge status={detail.status} />
              </div>
              <h1
                id="ticket-detail-title"
                className="ticket-detail__subject ticket-detail__subject--wrap"
              >
                {detail.subject}
              </h1>
              <div className="ticket-detail__activity">
                <span className="ticket-detail__label">Son hareket</span>
                <time dateTime={detail.lastActivityAt}>
                  {formatTicketDetailDate(detail.lastActivityAt)}
                </time>
              </div>
              <div className="ticket-detail__actions">
                <button
                  type="button"
                  className="button button--quiet"
                  onClick={() => void refresh()}
                  disabled={isBusy}
                >
                  {isRefreshing ? 'Yenileniyor…' : 'Yenile'}
                </button>
                {showRefreshError && error ? (
                  <p
                    className="ticket-detail__refresh-alert notice notice--error"
                    role="alert"
                  >
                    {initialErrorMessage(error.kind)}
                  </p>
                ) : null}
              </div>
            </header>

            <dl className="ticket-detail__customer">
              <div>
                <dt className="ticket-detail__label">Müşteri</dt>
                <dd className="ticket-detail__name ticket-detail__name--wrap">
                  {detail.customerName}
                </dd>
              </div>
              <div>
                <dt className="ticket-detail__label">E-posta</dt>
                <dd className="ticket-detail__email ticket-detail__email--wrap">
                  {detail.customerEmail}
                </dd>
              </div>
            </dl>

            {downloadError ? (
              <div className="ticket-detail__download-alert" role="alert">
                <p className="notice notice--error">
                  {downloadErrorMessage(downloadError.kind)}
                </p>
                <button
                  type="button"
                  className="button button--quiet"
                  onClick={clearDownloadError}
                >
                  Uyarıyı kapat
                </button>
              </div>
            ) : null}

            <section
              className="ticket-detail__timeline-section"
              aria-labelledby="ticket-timeline-heading"
            >
              <h2 id="ticket-timeline-heading">Mesaj geçmişi</h2>
              {olderMessagesError ? (
                <div className="ticket-detail__older" role="alert">
                  <p className="notice notice--error">
                    {olderMessagesErrorMessage(olderMessagesError.kind)}
                  </p>
                  <button
                    type="button"
                    className="button button--quiet"
                    onClick={() => void loadOlderMessages()}
                  >
                    Yeniden dene
                  </button>
                </div>
              ) : detail.hasMoreMessages ? (
                <div className="ticket-detail__older">
                  <button
                    type="button"
                    className="button button--quiet"
                    disabled={isLoadingOlder}
                    aria-busy={isLoadingOlder}
                    onClick={() => void loadOlderMessages()}
                  >
                    {isLoadingOlder
                      ? 'Eski mesajlar yükleniyor…'
                      : 'Daha eski mesajları yükle'}
                  </button>
                </div>
              ) : null}
              <TicketTimeline
                messages={detail.messages}
                attachmentsByMessage={attachmentsByMessage}
                activeAttachmentId={activeAttachmentId}
                onDownload={(attachment) => {
                  void download(attachment)
                }}
              />
            </section>
          </div>

          <aside className="ticket-detail__reply-slot">
            <TicketAssignmentPanel
              ticketId={detail.id}
              status={detail.status}
              assignedUserId={detail.assignedUserId}
              onApplyAssignment={applyAssignment}
            />
            <TicketResolutionPanel
              ticketId={detail.id}
              status={detail.status}
              onApplyResolved={applyResolvedTicket}
              onRefresh={refresh}
            />
            {detail.status !== 'Resolved' ? (
              <TicketReplyForm ticketId={detail.id} onRefresh={refresh} />
            ) : null}
          </aside>
        </div>
      ) : null}
    </section>
  )
}
