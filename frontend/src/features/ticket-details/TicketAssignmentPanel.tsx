import {
  useCallback,
  useEffect,
  useId,
  useRef,
  useState,
  type FormEvent,
  type ReactElement,
} from 'react'
import { ApiError } from '../../api/client'
import { assignTicket, fetchAssignableUsers } from '../../api/ticketsApi'
import type { AssignTicketResult, AssignableUser } from '../../api/types'

type TicketAssignmentPanelProps = {
  ticketId: string
  status: string
  assignedUserId: string | null
  onApplyAssignment: (result: AssignTicketResult) => void
}

type Notice = {
  role: 'status' | 'alert'
  message: string
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function domainCode(error: ApiError): string | null {
  if (typeof error.body !== 'object' || error.body === null) {
    return null
  }
  const code = (error.body as { code?: unknown }).code
  return typeof code === 'string' ? code : null
}

function mutationErrorNotice(error: unknown): Notice | null {
  if (isAbortError(error) || (error instanceof ApiError && error.status === 401)) {
    return null
  }
  if (error instanceof TypeError) {
    return {
      role: 'alert',
      message:
        'Sorumlu güncellenemedi. Bağlantınızı kontrol edip yeniden deneyin.',
    }
  }
  if (error instanceof ApiError) {
    const code = domainCode(error)
    if (code === 'assignee-not-available') {
      return {
        role: 'alert',
        message: 'Seçilen kullanıcı artık aktif değil. Listeyi yeniden yükleyin.',
      }
    }
    if (code === 'ticket-resolved') {
      return {
        role: 'alert',
        message: 'Çözülmüş taleplerde sorumlu değiştirilemez.',
      }
    }
    if (error.status === 404) {
      return { role: 'alert', message: 'Destek talebi bulunamadı.' }
    }
    if (error.status === 409) {
      return {
        role: 'alert',
        message: 'Talep başka bir işlemle güncellendi. Sayfayı yenileyin.',
      }
    }
  }
  return {
    role: 'alert',
    message: 'Sorumlu güncellenemedi. Lütfen yeniden deneyin.',
  }
}

export function TicketAssignmentPanel({
  ticketId,
  status,
  assignedUserId,
  onApplyAssignment,
}: TicketAssignmentPanelProps): ReactElement {
  const [users, setUsers] = useState<AssignableUser[]>([])
  const [selectedValue, setSelectedValue] = useState(assignedUserId ?? '')
  const [savedValue, setSavedValue] = useState(assignedUserId ?? '')
  const [isLoading, setIsLoading] = useState(true)
  const [isSaving, setIsSaving] = useState(false)
  const [loadError, setLoadError] = useState(false)
  const [notice, setNotice] = useState<Notice | null>(null)
  const loadSequence = useRef(0)
  const loadController = useRef<AbortController | null>(null)
  const saveController = useRef<AbortController | null>(null)
  const selectId = useId()
  const headingId = `${selectId}-heading`

  useEffect(() => {
    const next = assignedUserId ?? ''
    setSelectedValue(next)
    setSavedValue(next)
  }, [assignedUserId, ticketId])

  const loadUsers = useCallback(async () => {
    const sequence = ++loadSequence.current
    loadController.current?.abort()
    const controller = new AbortController()
    loadController.current = controller
    setIsLoading(true)
    setLoadError(false)

    try {
      const result = await fetchAssignableUsers({ signal: controller.signal })
      if (sequence !== loadSequence.current) return
      setUsers(result)
    } catch (error) {
      if (
        sequence !== loadSequence.current ||
        isAbortError(error) ||
        (error instanceof ApiError && error.status === 401)
      ) {
        return
      }
      setLoadError(true)
    } finally {
      if (sequence === loadSequence.current) {
        setIsLoading(false)
      }
    }
  }, [])

  useEffect(() => {
    void loadUsers()
    return () => {
      loadSequence.current += 1
      loadController.current?.abort()
      saveController.current?.abort()
    }
  }, [loadUsers, ticketId])

  const isResolved = status === 'Resolved'
  const hasStaleSelection =
    savedValue !== '' && !users.some((user) => user.id === savedValue)
  const canSave =
    !isResolved &&
    !isLoading &&
    !loadError &&
    !isSaving &&
    selectedValue !== savedValue

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canSave) return

    saveController.current?.abort()
    const controller = new AbortController()
    saveController.current = controller
    setIsSaving(true)
    setNotice(null)

    try {
      const result = await assignTicket(
        ticketId,
        selectedValue === '' ? null : selectedValue,
        { signal: controller.signal },
      )
      if (result.ticketId !== ticketId) {
        setNotice({
          role: 'alert',
          message: 'Sorumlu güncellenemedi. Lütfen yeniden deneyin.',
        })
        return
      }

      const next = result.assignedUserId ?? ''
      setSavedValue(next)
      setSelectedValue(next)
      onApplyAssignment(result)
      setNotice({ role: 'status', message: 'Sorumlu güncellendi.' })
    } catch (error) {
      setNotice(mutationErrorNotice(error))
    } finally {
      if (saveController.current === controller) {
        saveController.current = null
      }
      setIsSaving(false)
    }
  }

  return (
    <section
      className="ticket-assignment"
      aria-labelledby={headingId}
      aria-busy={isLoading || isSaving || undefined}
    >
      <h2 id={headingId} className="ticket-assignment__heading">
        Sorumlu
      </h2>

      {isLoading ? (
        <p className="ticket-assignment__loading" role="status">
          Sorumlu listesi yükleniyor…
        </p>
      ) : null}

      {loadError ? (
        <div className="ticket-assignment__load-error">
          <p className="notice notice--error" role="alert">
            Sorumlu listesi yüklenemedi. Bağlantınızı kontrol edip yeniden deneyin.
          </p>
          <button
            type="button"
            className="button button--quiet"
            onClick={() => void loadUsers()}
          >
            Listeyi yeniden yükle
          </button>
        </div>
      ) : null}

      {!isLoading && !loadError ? (
        <form className="ticket-assignment__form" onSubmit={handleSubmit}>
          <label className="ticket-assignment__label" htmlFor={selectId}>
            Atanan destek personeli
          </label>
          <select
            id={selectId}
            className="ticket-assignment__select"
            value={selectedValue}
            onChange={(event) => {
              setSelectedValue(event.target.value)
              setNotice(null)
            }}
            disabled={isResolved || isSaving}
          >
            <option value="">Atanmamış</option>
            {hasStaleSelection ? (
              <option value={savedValue} disabled>
                Mevcut atama (aktif değil)
              </option>
            ) : null}
            {users.map((user) => (
              <option key={user.id} value={user.id}>
                {user.fullName} (@{user.username})
              </option>
            ))}
          </select>

          {isResolved ? (
            <p className="ticket-assignment__locked">
              Çözülmüş taleplerde sorumlu değiştirilemez.
            </p>
          ) : null}

          <button
            type="submit"
            className="button button--quiet ticket-assignment__submit"
            disabled={!canSave}
          >
            {isSaving ? 'Atama kaydediliyor…' : 'Atamayı kaydet'}
          </button>
        </form>
      ) : null}

      {notice ? (
        <p
          className={
            notice.role === 'status'
              ? 'notice notice--info ticket-assignment__notice'
              : 'notice notice--error ticket-assignment__notice'
          }
          role={notice.role}
        >
          {notice.message}
        </p>
      ) : null}
    </section>
  )
}
