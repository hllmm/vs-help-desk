import {
  useEffect,
  useId,
  useRef,
  useState,
  type FormEvent,
  type ReactElement,
} from 'react'
import type { UserListItem, UserRole } from '../api/types'
import {
  useUsers,
  type UserLoadErrorKind,
  type UserMutationErrorKind,
} from '../features/users/useUsers'
import { formatTicketActivity } from '../features/tickets/ticketListModel'

const LAST_ADMIN_MESSAGE =
  'Sistemde en az bir aktif yönetici kalmalıdır.'

function loadErrorMessage(
  kind: UserLoadErrorKind,
  hasRows: boolean,
): string {
  if (hasRows) {
    return kind === 'network'
      ? 'Destek hizmetine ulaşılamadı. Mevcut kullanıcıları görüntülemeye devam edebilir ve yeniden deneyebilirsiniz.'
      : 'Kullanıcı listesi güncellenemedi. Mevcut listeyi görüntülemeye devam edebilirsiniz.'
  }

  return kind === 'network'
    ? 'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.'
    : 'Kullanıcılar yüklenemedi. Lütfen yeniden deneyin.'
}

function mutationErrorMessage(kind: UserMutationErrorKind): string {
  switch (kind) {
    case 'last-admin-required':
      return LAST_ADMIN_MESSAGE
    case 'validation':
      return 'Girilen bilgiler geçersiz. Lütfen kontrol edip yeniden deneyin.'
    case 'not-found':
      return 'Kullanıcı bulunamadı.'
    case 'network':
      return 'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.'
    case 'server':
      return 'İşlem tamamlanamadı. Lütfen yeniden deneyin.'
  }
}

function roleLabel(role: UserRole): string {
  return role === 'Admin' ? 'Yönetici' : 'Destek'
}

function formatLastLogin(value: string | null): string {
  if (!value) {
    return '—'
  }
  return formatTicketActivity(value)
}

type CreateFormState = {
  fullName: string
  username: string
  email: string
  password: string
  role: UserRole
}

const emptyCreateForm: CreateFormState = {
  fullName: '',
  username: '',
  email: '',
  password: '',
  role: 'Support',
}

type PasswordFormState = {
  userId: string
  username: string
  password: string
}

export function UsersPage(): ReactElement {
  const {
    users,
    hasLoaded,
    isInitialLoading,
    isRefreshing,
    error,
    refresh,
    mutatingUserId,
    isCreating,
    create,
    update,
    setPassword,
  } = useUsers()

  const [createOpen, setCreateOpen] = useState(false)
  const [createForm, setCreateForm] = useState<CreateFormState>(emptyCreateForm)
  const [createError, setCreateError] = useState<string | null>(null)
  const [passwordForm, setPasswordForm] = useState<PasswordFormState | null>(
    null,
  )
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [rowError, setRowError] = useState<{
    id: string
    message: string
  } | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)

  const createTitleId = useId()
  const passwordTitleId = useId()
  const createDialogRef = useRef<HTMLDivElement>(null)
  const passwordDialogRef = useRef<HTMLDivElement>(null)
  const createFirstFieldRef = useRef<HTMLInputElement>(null)
  const passwordFirstFieldRef = useRef<HTMLInputElement>(null)

  const isBusy = isInitialLoading || isRefreshing
  const hasRows = users.length > 0
  const showInitialError = hasLoaded && error !== null && !hasRows
  const showRefreshError = error !== null && hasRows
  const showResults = hasLoaded && (error === null || hasRows) && hasRows
  const showTrueEmpty = hasLoaded && error === null && !hasRows
  const mutationBusy = isCreating || mutatingUserId !== null

  useEffect(() => {
    if (!createOpen) {
      return
    }
    createFirstFieldRef.current?.focus()
  }, [createOpen])

  useEffect(() => {
    if (!passwordForm) {
      return
    }
    passwordFirstFieldRef.current?.focus()
  }, [passwordForm])

  function openCreate() {
    setCreateForm(emptyCreateForm)
    setCreateError(null)
    setSuccessMessage(null)
    setCreateOpen(true)
  }

  function closeCreate() {
    if (isCreating) {
      return
    }
    setCreateOpen(false)
    setCreateError(null)
  }

  function openPassword(user: UserListItem) {
    setPasswordForm({
      userId: user.id,
      username: user.username,
      password: '',
    })
    setPasswordError(null)
    setSuccessMessage(null)
  }

  function closePassword() {
    if (mutatingUserId !== null) {
      return
    }
    setPasswordForm(null)
    setPasswordError(null)
  }

  async function handleCreate(event: FormEvent) {
    event.preventDefault()
    setCreateError(null)
    setSuccessMessage(null)

    const result = await create({
      fullName: createForm.fullName.trim(),
      username: createForm.username.trim(),
      email: createForm.email.trim(),
      password: createForm.password,
      role: createForm.role,
    })

    if (result.ok) {
      setCreateOpen(false)
      setCreateForm(emptyCreateForm)
      setSuccessMessage('Kullanıcı eklendi.')
      return
    }
    if (result.error === null) {
      return
    }
    setCreateError(mutationErrorMessage(result.error))
  }

  async function handleRoleChange(user: UserListItem, role: UserRole) {
    if (role === user.role) {
      return
    }
    setRowError(null)
    setSuccessMessage(null)

    const result = await update(user.id, {
      fullName: user.fullName,
      email: user.email,
      role,
      isActive: user.isActive,
    })

    if (result.ok) {
      setSuccessMessage('Kullanıcı güncellendi.')
      return
    }
    if (result.error === null) {
      return
    }
    setRowError({ id: user.id, message: mutationErrorMessage(result.error) })
  }

  async function handleActiveToggle(user: UserListItem) {
    setRowError(null)
    setSuccessMessage(null)

    const result = await update(user.id, {
      fullName: user.fullName,
      email: user.email,
      role: user.role,
      isActive: !user.isActive,
    })

    if (result.ok) {
      setSuccessMessage('Kullanıcı güncellendi.')
      return
    }
    if (result.error === null) {
      return
    }
    setRowError({ id: user.id, message: mutationErrorMessage(result.error) })
  }

  async function handlePasswordSubmit(event: FormEvent) {
    event.preventDefault()
    if (!passwordForm) {
      return
    }
    setPasswordError(null)
    setSuccessMessage(null)

    const result = await setPassword(passwordForm.userId, {
      password: passwordForm.password,
    })

    if (result.ok) {
      setPasswordForm(null)
      setSuccessMessage('Parola güncellendi.')
      return
    }
    if (result.error === null) {
      return
    }
    setPasswordError(mutationErrorMessage(result.error))
  }

  return (
    <section
      className="users-workspace"
      aria-labelledby="users-title"
      aria-busy={isBusy || mutationBusy}
      role="region"
    >
      <header className="users-workspace__header">
        <div>
          <h1 id="users-title">Kullanıcılar</h1>
          <p className="users-workspace__lede">
            Portal hesaplarını yönetin: rol, aktiflik ve parola işlemleri.
          </p>
        </div>
        <div className="users-workspace__actions">
          {hasLoaded && hasRows ? (
            <button
              type="button"
              className="button button--quiet"
              onClick={() => void refresh()}
              disabled={isBusy || mutationBusy}
            >
              {isRefreshing ? 'Yenileniyor…' : 'Yenile'}
            </button>
          ) : null}
          <button
            type="button"
            className="button button--primary"
            onClick={openCreate}
            disabled={isBusy || mutationBusy}
          >
            Kullanıcı ekle
          </button>
        </div>
      </header>

      {successMessage ? (
        <p className="notice notice--info" role="status">
          {successMessage}
        </p>
      ) : null}

      {isInitialLoading ? (
        <p className="ticket-state ticket-state--loading" role="status">
          Kullanıcılar yükleniyor…
        </p>
      ) : null}

      {showInitialError && error ? (
        <div className="ticket-state ticket-state--error" role="alert">
          <p>{loadErrorMessage(error, false)}</p>
          <button
            type="button"
            className="button button--primary"
            onClick={() => void refresh()}
          >
            Yeniden dene
          </button>
        </div>
      ) : null}

      {showRefreshError && error ? (
        <div className="ticket-state ticket-state--error" role="alert">
          <p>{loadErrorMessage(error, true)}</p>
        </div>
      ) : null}

      {showTrueEmpty ? (
        <div className="ticket-state ticket-state--empty">
          <p>Görüntülenecek kullanıcı yok.</p>
        </div>
      ) : null}

      {showResults ? (
        <div className="users-table-view">
          <table className="ticket-table users-table">
            <caption className="visually-hidden">Portal kullanıcıları</caption>
            <thead>
              <tr>
                <th scope="col">Ad soyad</th>
                <th scope="col">Kullanıcı adı</th>
                <th scope="col">E-posta</th>
                <th scope="col">Rol</th>
                <th scope="col">Aktif</th>
                <th scope="col">Son giriş</th>
                <th scope="col">İşlemler</th>
              </tr>
            </thead>
            <tbody>
              {users.map((user) => {
                const isRowBusy = mutatingUserId === user.id
                const showRowError = rowError?.id === user.id

                return (
                  <tr key={user.id}>
                    <th scope="row">{user.fullName}</th>
                    <td>
                      <code className="users-table__username">
                        {user.username}
                      </code>
                    </td>
                    <td>{user.email}</td>
                    <td>
                      <label
                        className="visually-hidden"
                        htmlFor={`user-role-${user.id}`}
                      >
                        {user.username} rolü
                      </label>
                      <select
                        id={`user-role-${user.id}`}
                        className="users-table__select"
                        value={user.role}
                        disabled={isRowBusy || isBusy}
                        onChange={(event) =>
                          void handleRoleChange(
                            user,
                            event.target.value as UserRole,
                          )
                        }
                      >
                        <option value="Support">{roleLabel('Support')}</option>
                        <option value="Admin">{roleLabel('Admin')}</option>
                      </select>
                    </td>
                    <td>
                      <label className="users-table__active">
                        <input
                          type="checkbox"
                          checked={user.isActive}
                          disabled={isRowBusy || isBusy}
                          onChange={() => void handleActiveToggle(user)}
                        />
                        <span>{user.isActive ? 'Aktif' : 'Pasif'}</span>
                      </label>
                    </td>
                    <td>
                      {user.lastLoginAt ? (
                        <time dateTime={user.lastLoginAt}>
                          {formatLastLogin(user.lastLoginAt)}
                        </time>
                      ) : (
                        formatLastLogin(null)
                      )}
                    </td>
                    <td className="users-table__action-cell">
                      <button
                        type="button"
                        className="button button--quiet users-table__password"
                        onClick={() => openPassword(user)}
                        disabled={isRowBusy || isBusy}
                      >
                        Parola sıfırla
                      </button>
                      {showRowError && rowError ? (
                        <p
                          className="notice notice--error users-table__notice"
                          role="alert"
                        >
                          {rowError.message}
                        </p>
                      ) : null}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      ) : null}

      {createOpen ? (
        <div className="users-dialog__overlay">
          <div
            ref={createDialogRef}
            className="users-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby={createTitleId}
          >
            <h2 id={createTitleId} className="users-dialog__title">
              Kullanıcı ekle
            </h2>
            <form className="form" onSubmit={(event) => void handleCreate(event)}>
              <label className="field">
                <span>Ad soyad</span>
                <input
                  ref={createFirstFieldRef}
                  name="fullName"
                  type="text"
                  autoComplete="name"
                  value={createForm.fullName}
                  onChange={(event) =>
                    setCreateForm((current) => ({
                      ...current,
                      fullName: event.target.value,
                    }))
                  }
                  disabled={isCreating}
                  required
                />
              </label>
              <label className="field">
                <span>Kullanıcı adı</span>
                <input
                  name="username"
                  type="text"
                  autoComplete="username"
                  value={createForm.username}
                  onChange={(event) =>
                    setCreateForm((current) => ({
                      ...current,
                      username: event.target.value,
                    }))
                  }
                  disabled={isCreating}
                  required
                />
              </label>
              <label className="field">
                <span>E-posta</span>
                <input
                  name="email"
                  type="email"
                  autoComplete="email"
                  value={createForm.email}
                  onChange={(event) =>
                    setCreateForm((current) => ({
                      ...current,
                      email: event.target.value,
                    }))
                  }
                  disabled={isCreating}
                  required
                />
              </label>
              <label className="field">
                <span>Parola</span>
                <input
                  name="password"
                  type="password"
                  autoComplete="new-password"
                  value={createForm.password}
                  onChange={(event) =>
                    setCreateForm((current) => ({
                      ...current,
                      password: event.target.value,
                    }))
                  }
                  disabled={isCreating}
                  required
                  minLength={12}
                />
              </label>
              <label className="field">
                <span>Rol</span>
                <select
                  name="role"
                  className="users-table__select"
                  value={createForm.role}
                  onChange={(event) =>
                    setCreateForm((current) => ({
                      ...current,
                      role: event.target.value as UserRole,
                    }))
                  }
                  disabled={isCreating}
                >
                  <option value="Support">{roleLabel('Support')}</option>
                  <option value="Admin">{roleLabel('Admin')}</option>
                </select>
              </label>
              {createError ? (
                <p className="notice notice--error" role="alert">
                  {createError}
                </p>
              ) : null}
              <div className="users-dialog__actions">
                <button
                  type="button"
                  className="button button--quiet"
                  onClick={closeCreate}
                  disabled={isCreating}
                >
                  Vazgeç
                </button>
                <button
                  type="submit"
                  className="button button--primary"
                  disabled={isCreating}
                >
                  {isCreating ? 'Ekleniyor…' : 'Kaydet'}
                </button>
              </div>
            </form>
          </div>
        </div>
      ) : null}

      {passwordForm ? (
        <div className="users-dialog__overlay">
          <div
            ref={passwordDialogRef}
            className="users-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby={passwordTitleId}
          >
            <h2 id={passwordTitleId} className="users-dialog__title">
              Parola sıfırla
            </h2>
            <p className="users-dialog__lede">
              @{passwordForm.username} için yeni parola belirleyin.
            </p>
            <form
              className="form"
              onSubmit={(event) => void handlePasswordSubmit(event)}
            >
              <label className="field">
                <span>Yeni parola</span>
                <input
                  ref={passwordFirstFieldRef}
                  name="password"
                  type="password"
                  autoComplete="new-password"
                  value={passwordForm.password}
                  onChange={(event) =>
                    setPasswordForm((current) =>
                      current
                        ? { ...current, password: event.target.value }
                        : current,
                    )
                  }
                  disabled={mutatingUserId === passwordForm.userId}
                  required
                  minLength={12}
                />
              </label>
              {passwordError ? (
                <p className="notice notice--error" role="alert">
                  {passwordError}
                </p>
              ) : null}
              <div className="users-dialog__actions">
                <button
                  type="button"
                  className="button button--quiet"
                  onClick={closePassword}
                  disabled={mutatingUserId === passwordForm.userId}
                >
                  Vazgeç
                </button>
                <button
                  type="submit"
                  className="button button--primary"
                  disabled={mutatingUserId === passwordForm.userId}
                >
                  {mutatingUserId === passwordForm.userId
                    ? 'Kaydediliyor…'
                    : 'Parolayı kaydet'}
                </button>
              </div>
            </form>
          </div>
        </div>
      ) : null}
    </section>
  )
}
