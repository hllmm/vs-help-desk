import type { ReactElement } from 'react'
import type {
  TicketAttachmentMeta,
  TicketMessageItem,
} from '../../api/types'
import {
  formatAttachmentSize,
  formatTicketDetailDate,
  getMessageSenderLabel,
} from './ticketDetailModel'

export type TicketTimelineProps = {
  messages: readonly TicketMessageItem[]
  attachmentsByMessage: ReadonlyMap<string, readonly TicketAttachmentMeta[]>
  activeAttachmentId: string | null
  onDownload: (attachment: TicketAttachmentMeta) => void
}

export function TicketTimeline(props: TicketTimelineProps): ReactElement {
  const { messages, attachmentsByMessage, activeAttachmentId, onDownload } =
    props

  if (messages.length === 0) {
    return (
      <p className="ticket-timeline__empty">Bu talepte henüz mesaj yok.</p>
    )
  }

  return (
    <ol className="ticket-timeline" aria-label="Mesaj geçmişi">
      {messages.map((message) => {
        const attachments = attachmentsByMessage.get(message.id) ?? []
        return (
          <li key={message.id} className="ticket-timeline__item">
            <article className="ticket-timeline__message">
              <header className="ticket-timeline__meta">
                <span className="ticket-timeline__sender">
                  {getMessageSenderLabel(message.senderType)}
                </span>
                <time dateTime={message.createdAt}>
                  {formatTicketDetailDate(message.createdAt)}
                </time>
              </header>
              <p className="ticket-timeline__body ticket-timeline__body--wrap">
                {message.content}
              </p>
              {attachments.length > 0 ? (
                <section
                  className="ticket-timeline__attachments"
                  aria-label="Ekler"
                >
                  <h3 className="ticket-timeline__attachments-heading">
                    Ekler
                  </h3>
                  <ul className="ticket-timeline__attachment-list">
                    {attachments.map((attachment) => {
                      const busy = activeAttachmentId === attachment.id
                      const label = busy
                        ? `${attachment.fileName} indiriliyor`
                        : `${attachment.fileName} dosyasını indir`
                      return (
                        <li key={attachment.id}>
                          <button
                            type="button"
                            className="button button--quiet ticket-timeline__download"
                            onClick={() => onDownload(attachment)}
                            disabled={activeAttachmentId !== null}
                            aria-busy={busy}
                            aria-label={label}
                          >
                            <span className="ticket-timeline__file-name ticket-timeline__file-name--wrap">
                              {attachment.fileName}
                            </span>
                            <span className="ticket-timeline__file-size">
                              {formatAttachmentSize(attachment.fileSize)}
                            </span>
                            <span className="ticket-timeline__download-label">
                              {busy ? 'İndiriliyor…' : 'İndir'}
                            </span>
                          </button>
                        </li>
                      )
                    })}
                  </ul>
                </section>
              ) : null}
            </article>
          </li>
        )
      })}
    </ol>
  )
}
