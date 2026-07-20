import {
  useEffect,
  useId,
  useRef,
  useState,
  type FormEvent,
  type ReactElement,
} from 'react'
import { ApiError } from '../../api/client'
import {
  messageForReplyOutcome,
  SUPPORT_REPLY_MAX_LENGTH,
  useTicketReply,
  type ReplyOutcomeKind,
  type ReplySubmissionOutcome,
} from './useTicketReply'

export type TicketReplyFormProps = {
  ticketId: string
  onRefresh: () => Promise<void>
}

type LocalOutcome = {
  kind: ReplyOutcomeKind
  messageSaved: boolean
  message: string
}

function isValidationKind(kind: ReplyOutcomeKind): boolean {
  return kind === 'validation-required' || kind === 'validation-too-long'
}

function outcomeRole(
  kind: ReplyOutcomeKind,
): 'status' | 'alert' {
  return kind === 'delivered' ? 'status' : 'alert'
}

function outcomeClassName(kind: ReplyOutcomeKind, messageSaved: boolean): string {
  if (kind === 'delivered') {
    return 'notice notice--info ticket-reply__notice'
  }
  if (
    kind === 'smtp-failed' ||
    kind === 'state-conflict' ||
    (kind === 'server-error' && messageSaved)
  ) {
    return 'notice notice--warning ticket-reply__notice'
  }
  return 'notice notice--error ticket-reply__notice'
}

export function TicketReplyForm({
  ticketId,
  onRefresh,
}: TicketReplyFormProps): ReactElement {
  const { isSubmitting, submit } = useTicketReply(ticketId)
  const [draft, setDraft] = useState('')
  const [outcome, setOutcome] = useState<LocalOutcome | null>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const noticeRef = useRef<HTMLParagraphElement>(null)
  const focusTargetRef = useRef<'textarea' | 'notice' | null>(null)
  const baseId = useId()
  const labelId = `${baseId}-label`
  const countId = `${baseId}-count`
  const validationId = `${baseId}-validation`
  const noticeId = `${baseId}-notice`

  const remaining = SUPPORT_REPLY_MAX_LENGTH - draft.length
  const validationFailure =
    outcome !== null && isValidationKind(outcome.kind) ? outcome : null
  const transportOutcome =
    outcome !== null && !isValidationKind(outcome.kind) ? outcome : null

  const describedBy = [
    countId,
    validationFailure ? validationId : null,
  ]
    .filter(Boolean)
    .join(' ')

  useEffect(() => {
    const target = focusTargetRef.current
    if (!target) {
      return
    }
    focusTargetRef.current = null
    if (target === 'textarea') {
      textareaRef.current?.focus()
      return
    }
    noticeRef.current?.focus()
  }, [outcome])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (isSubmitting) {
      return
    }

    setOutcome(null)

    let result: ReplySubmissionOutcome
    try {
      result = await submit(draft)
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        return
      }
      focusTargetRef.current = 'notice'
      setOutcome({
        kind: 'server-error',
        messageSaved: false,
        message: messageForReplyOutcome('server-error'),
      })
      return
    }

    const message = messageForReplyOutcome(result.kind, result.messageSaved)

    if (isValidationKind(result.kind)) {
      focusTargetRef.current = 'textarea'
      setOutcome({
        kind: result.kind,
        messageSaved: false,
        message,
      })
      return
    }

    if (result.messageSaved) {
      setDraft('')
      try {
        await onRefresh()
      } catch {
        // Keep the saved/delivery notice even when refresh fails separately.
      }
      focusTargetRef.current = 'notice'
      setOutcome({
        kind: result.kind,
        messageSaved: true,
        message,
      })
      return
    }

    if (result.kind === 'pre-send-conflict') {
      try {
        await onRefresh()
      } catch {
        // Draft is preserved; conflict notice still applies.
      }
      focusTargetRef.current = 'notice'
      setOutcome({
        kind: result.kind,
        messageSaved: false,
        message,
      })
      return
    }

    // network / not-found / server — preserve draft, no auto-retry / no refresh for network
    focusTargetRef.current = 'notice'
    setOutcome({
      kind: result.kind,
      messageSaved: false,
      message,
    })
  }

  return (
    <section
      className="ticket-reply"
      aria-labelledby="ticket-reply-heading"
      aria-busy={isSubmitting || undefined}
    >
      <h2 id="ticket-reply-heading" className="ticket-reply__heading">
        Müşteriye yanıt ver
      </h2>

      <form className="ticket-reply__form" onSubmit={(e) => void handleSubmit(e)}>
        <div className="ticket-reply__field">
          <label id={labelId} htmlFor={`${baseId}-content`} className="ticket-reply__label">
            Yanıtınız
          </label>
          <textarea
            id={`${baseId}-content`}
            ref={textareaRef}
            className="ticket-reply__textarea"
            name="content"
            value={draft}
            onChange={(event) => {
              setDraft(event.target.value)
              if (validationFailure) {
                setOutcome(null)
              }
            }}
            rows={8}
            disabled={isSubmitting}
            aria-labelledby={labelId}
            aria-describedby={describedBy}
            aria-invalid={validationFailure ? true : undefined}
            aria-required="true"
          />
          <p id={countId} className="ticket-reply__count">
            Kalan karakter: {remaining.toLocaleString('tr-TR')}
          </p>
          {validationFailure ? (
            <p
              id={validationId}
              className="ticket-reply__validation notice notice--error"
              role="alert"
            >
              {validationFailure.message}
            </p>
          ) : null}
        </div>

        {transportOutcome ? (
          <p
            id={noticeId}
            ref={noticeRef}
            className={outcomeClassName(
              transportOutcome.kind,
              transportOutcome.messageSaved,
            )}
            role={outcomeRole(transportOutcome.kind)}
            tabIndex={-1}
          >
            {transportOutcome.message}
          </p>
        ) : null}

        <button
          type="submit"
          className="button button--primary ticket-reply__submit"
          disabled={isSubmitting}
        >
          {isSubmitting ? 'Yanıt gönderiliyor…' : 'Yanıtı gönder'}
        </button>
      </form>
    </section>
  )
}
