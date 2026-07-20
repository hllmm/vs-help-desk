import {
  useEffect,
  useId,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type ReactElement,
} from 'react'
import type { ResolveTicketResult } from '../../api/types'
import {
  RESOLUTION_COPY,
  useResolveTicket,
  type ResolveTicketOutcome,
  type ResolveTicketOutcomeKind,
} from './useResolveTicket'

export type TicketResolutionPanelProps = {
  ticketId: string
  status: string
  onApplyResolved: (result: ResolveTicketResult) => void
  onRefresh: () => Promise<void>
}

type NoticeKind = ResolveTicketOutcomeKind

function messageForOutcome(kind: NoticeKind): string {
  switch (kind) {
    case 'resolved':
      return RESOLUTION_COPY.resolved
    case 'already-resolved':
      return RESOLUTION_COPY.alreadyResolved
    case 'conflict':
      return RESOLUTION_COPY.conflict
    case 'network-ambiguous':
      return RESOLUTION_COPY.network
    case 'not-found':
      return RESOLUTION_COPY.notFound
    case 'server-error':
      return RESOLUTION_COPY.serverError
  }
}

function noticeRole(kind: NoticeKind): 'status' | 'alert' {
  return kind === 'resolved' || kind === 'already-resolved' ? 'status' : 'alert'
}

function noticeClassName(kind: NoticeKind): string {
  if (kind === 'resolved' || kind === 'already-resolved') {
    return 'notice notice--info ticket-resolution__notice'
  }
  if (kind === 'conflict' || kind === 'network-ambiguous') {
    return 'notice notice--warning ticket-resolution__notice'
  }
  return 'notice notice--error ticket-resolution__notice'
}

function getFocusableElements(container: HTMLElement): HTMLElement[] {
  const nodes = container.querySelectorAll<HTMLElement>(
    'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
  )
  return Array.from(nodes).filter(
    (el) => !el.hasAttribute('disabled') && el.getAttribute('aria-hidden') !== 'true',
  )
}

export function TicketResolutionPanel({
  ticketId,
  status,
  onApplyResolved,
  onRefresh,
}: TicketResolutionPanelProps): ReactElement {
  const { isResolving, resolve } = useResolveTicket(ticketId)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [notice, setNotice] = useState<{
    kind: NoticeKind
    message: string
  } | null>(null)
  const [focusNotice, setFocusNotice] = useState(false)

  const triggerRef = useRef<HTMLButtonElement>(null)
  const dialogRef = useRef<HTMLDivElement>(null)
  const cancelRef = useRef<HTMLButtonElement>(null)
  const noticeRef = useRef<HTMLParagraphElement>(null)
  const returnFocusRef = useRef(false)
  const isResolvingRef = useRef(isResolving)
  isResolvingRef.current = isResolving

  const baseId = useId()
  const titleId = `${baseId}-title`
  const descriptionId = `${baseId}-description`
  const noticeId = `${baseId}-notice`

  const isResolved = status === 'Resolved'
  const showTrigger = !isResolved

  useEffect(() => {
    if (!dialogOpen) {
      return
    }

    const triggerElement = triggerRef.current
    cancelRef.current?.focus()

    function onDocumentKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Escape') {
        return
      }
      if (isResolvingRef.current) {
        event.preventDefault()
        event.stopPropagation()
        return
      }
      event.preventDefault()
      returnFocusRef.current = true
      setDialogOpen(false)
    }

    document.addEventListener('keydown', onDocumentKeyDown)
    return () => {
      document.removeEventListener('keydown', onDocumentKeyDown)
      if (returnFocusRef.current) {
        returnFocusRef.current = false
        // Focus after the dialog unmounts from the tree.
        queueMicrotask(() => {
          triggerElement?.focus()
        })
      }
    }
  }, [dialogOpen])

  useEffect(() => {
    if (!focusNotice) {
      return
    }
    setFocusNotice(false)
    noticeRef.current?.focus()
  }, [focusNotice, notice])

  function openDialog() {
    setNotice(null)
    setDialogOpen(true)
  }

  function closeDialog(returnFocus: boolean) {
    if (isResolving) {
      return
    }
    returnFocusRef.current = returnFocus
    setDialogOpen(false)
  }

  function handleDialogKeyDown(event: ReactKeyboardEvent<HTMLDivElement>) {
    if (event.key !== 'Tab' || !dialogRef.current) {
      return
    }

    const focusable = getFocusableElements(dialogRef.current)
    if (focusable.length === 0) {
      event.preventDefault()
      return
    }

    const first = focusable[0]!
    const last = focusable[focusable.length - 1]!
    const active = document.activeElement as HTMLElement | null

    if (event.shiftKey) {
      if (active === first || !dialogRef.current.contains(active)) {
        event.preventDefault()
        last.focus()
      }
      return
    }

    if (active === last) {
      event.preventDefault()
      first.focus()
    }
  }

  async function handleConfirm() {
    if (isResolving) {
      return
    }

    const outcome: ResolveTicketOutcome | null = await resolve()
    if (outcome === null) {
      setDialogOpen(false)
      return
    }

    if (outcome.kind === 'resolved' || outcome.kind === 'already-resolved') {
      if (outcome.result) {
        onApplyResolved(outcome.result)
      }
      setDialogOpen(false)
      try {
        await onRefresh()
      } catch {
        // Keep the server-confirmed patch and outcome notice if refresh fails.
      }
      setNotice({
        kind: outcome.kind,
        message: messageForOutcome(outcome.kind),
      })
      setFocusNotice(true)
      return
    }

    if (
      outcome.kind === 'conflict' ||
      outcome.kind === 'network-ambiguous'
    ) {
      setDialogOpen(false)
      try {
        await onRefresh()
      } catch {
        // Fixed notice still applies.
      }
      setNotice({
        kind: outcome.kind,
        message: messageForOutcome(outcome.kind),
      })
      setFocusNotice(true)
      return
    }

    // 404 / server-error: do not patch; keep detail as-is.
    setDialogOpen(false)
    setNotice({
      kind: outcome.kind,
      message: messageForOutcome(outcome.kind),
    })
    setFocusNotice(true)
  }

  return (
    <section
      className="ticket-resolution"
      aria-labelledby={`${baseId}-heading`}
      aria-busy={isResolving || undefined}
    >
      <h2 id={`${baseId}-heading`} className="ticket-resolution__heading">
        Talep kapanışı
      </h2>

      {isResolved ? (
        <p className="ticket-resolution__closure notice notice--info">
          {RESOLUTION_COPY.closureNote}
        </p>
      ) : null}

      {showTrigger ? (
        <button
          ref={triggerRef}
          type="button"
          className="button button--quiet ticket-resolution__trigger"
          onClick={openDialog}
          disabled={isResolving}
        >
          {RESOLUTION_COPY.trigger}
        </button>
      ) : null}

      {notice ? (
        <p
          id={noticeId}
          ref={noticeRef}
          className={noticeClassName(notice.kind)}
          role={noticeRole(notice.kind)}
          tabIndex={-1}
        >
          {notice.message}
        </p>
      ) : null}

      {dialogOpen ? (
        <div className="ticket-resolution__overlay">
          <div
            ref={dialogRef}
            className="ticket-resolution__dialog"
            role="alertdialog"
            aria-modal="true"
            aria-labelledby={titleId}
            aria-describedby={descriptionId}
            onKeyDown={handleDialogKeyDown}
          >
            <h3 id={titleId} className="ticket-resolution__dialog-title">
              {RESOLUTION_COPY.dialogTitle}
            </h3>
            <p id={descriptionId} className="ticket-resolution__dialog-body">
              {RESOLUTION_COPY.dialogDescription}
            </p>
            <div className="ticket-resolution__dialog-actions">
              <button
                ref={cancelRef}
                type="button"
                className="button button--quiet ticket-resolution__dialog-cancel"
                onClick={() => closeDialog(true)}
                disabled={isResolving}
              >
                {RESOLUTION_COPY.cancel}
              </button>
              <button
                type="button"
                className="button button--primary ticket-resolution__dialog-confirm"
                onClick={() => void handleConfirm()}
                disabled={isResolving}
              >
                {isResolving ? RESOLUTION_COPY.busy : RESOLUTION_COPY.confirm}
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </section>
  )
}
