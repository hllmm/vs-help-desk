import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { ApiError } from '../api/client'
import type { UserListItem } from '../api/types'
import { setStoredUser } from '../auth/tokenStorage'

const listUsers = vi.hoisted(() => vi.fn())
const createUser = vi.hoisted(() => vi.fn())
const updateUser = vi.hoisted(() => vi.fn())
const setUserPassword = vi.hoisted(() => vi.fn())

vi.mock('../api/usersApi', () => ({
  listUsers,
  createUser,
  updateUser,
  setUserPassword,
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const sampleUsers: UserListItem[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    fullName: 'Admin Kullanıcısı',
    username: 'admin',
    email: 'admin@vshelpdesk.local',
    role: 'Admin',
    isActive: true,
    createdAt: '2026-07-21T10:00:00.000Z',
    lastLoginAt: '2026-07-21T12:00:00.000Z',
  },
  {
    id: '22222222-2222-2222-2222-222222222222',
    fullName: 'Destek Kullanıcısı',
    username: 'support',
    email: 'support@vshelpdesk.local',
    role: 'Support',
    isActive: true,
    createdAt: '2026-07-21T10:00:00.000Z',
    lastLoginAt: null,
  },
]

function seedSession() {
  const user = {
    userId: 'user-1',
    fullName: 'Admin Kullanıcısı',
    username: 'admin',
    role: 'Admin' as const,
  }
  setStoredUser(user)
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = String(input)
      if (url.includes('/api/auth/me')) {
        return Promise.resolve(
          new Response(JSON.stringify(user), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        )
      }
      if (url.includes('/api/auth/logout')) {
        return Promise.resolve(new Response(null, { status: 204 }))
      }
      return Promise.resolve(
        new Response(JSON.stringify({ message: 'not found' }), {
          status: 404,
          headers: { 'Content-Type': 'application/json' },
        }),
      )
    }),
  )
}

function renderUsersPage() {
  seedSession()
  window.history.pushState({}, '', '/users')
  return render(<App />)
}

describe('UsersPage', () => {
  beforeEach(() => {
    listUsers.mockReset()
    createUser.mockReset()
    updateUser.mockReset()
    setUserPassword.mockReset()
    sessionStorage.clear()
    window.history.replaceState({}, '', '/')
  })

  it('shows Turkish initial loading with a polite status', async () => {
    const pending = deferred<UserListItem[]>()
    listUsers.mockReturnValueOnce(pending.promise)

    renderUsersPage()

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'Kullanıcılar yükleniyor…',
      )
    })

    pending.resolve(sampleUsers)
    await screen.findByRole('table')
  })

  it('renders user list columns and rows', async () => {
    listUsers.mockResolvedValueOnce(sampleUsers)
    renderUsersPage()

    const table = await screen.findByRole('table')
    for (const heading of [
      'Ad soyad',
      'Kullanıcı adı',
      'E-posta',
      'Rol',
      'Aktif',
      'Son giriş',
      'İşlemler',
    ]) {
      expect(
        within(table).getByRole('columnheader', { name: heading }),
      ).toBeInTheDocument()
    }

    expect(within(table).getByText('Admin Kullanıcısı')).toBeInTheDocument()
    expect(within(table).getByText('admin')).toBeInTheDocument()
    expect(within(table).getByText('Destek Kullanıcısı')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Kullanıcı ekle' }),
    ).toBeInTheDocument()
  })

  it('uses text and semantic metadata for role and activity state', async () => {
    listUsers.mockResolvedValueOnce(sampleUsers)
    renderUsersPage()

    expect(
      await screen.findByRole('heading', { name: 'Kullanıcı listesi' }),
    ).toBeInTheDocument()
    const role = screen.getByLabelText('admin rolü')
    expect(role).toHaveAttribute('data-role', 'Admin')

    const adminRow = screen.getByRole('row', {
      name: /Admin Kullanıcısı/,
    })
    const activeText = within(adminRow).getByText('Aktif')
    expect(activeText.closest('label')).toHaveAttribute(
      'data-state',
      'active',
    )
  })

  it('opens create dialog and submits a new user', async () => {
    const user = userEvent.setup()
    listUsers.mockResolvedValueOnce(sampleUsers)
    const created: UserListItem = {
      id: '33333333-3333-3333-3333-333333333333',
      fullName: 'Yeni Destek',
      username: 'support2',
      email: 'support2@example.test',
      role: 'Support',
      isActive: true,
      createdAt: '2026-07-21T13:00:00.000Z',
      lastLoginAt: null,
    }
    createUser.mockResolvedValueOnce(created)

    renderUsersPage()
    await screen.findByRole('table')

    await user.click(screen.getByRole('button', { name: 'Kullanıcı ekle' }))
    const dialog = await screen.findByRole('dialog', { name: 'Kullanıcı ekle' })

    await user.type(within(dialog).getByLabelText('Ad soyad'), 'Yeni Destek')
    await user.type(within(dialog).getByLabelText('Kullanıcı adı'), 'support2')
    await user.type(
      within(dialog).getByLabelText('E-posta'),
      'support2@example.test',
    )
    await user.type(
      within(dialog).getByLabelText('Parola'),
      'Password12345!',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => {
      expect(createUser).toHaveBeenCalledWith(
        {
          fullName: 'Yeni Destek',
          username: 'support2',
          email: 'support2@example.test',
          password: 'Password12345!',
          role: 'Support',
        },
        expect.objectContaining({ signal: expect.any(AbortSignal) }),
      )
    })

    expect(await screen.findByText('Kullanıcı eklendi.')).toBeInTheDocument()
    expect(screen.getByText('Yeni Destek')).toBeInTheDocument()
  })

  it('shows Turkish last-admin-required message on demote failure', async () => {
    const user = userEvent.setup()
    listUsers.mockResolvedValueOnce(sampleUsers)
    updateUser.mockRejectedValueOnce(
      new ApiError(400, 'A domain rule was violated.', {
        code: 'last-admin-required',
      }),
    )

    renderUsersPage()
    await screen.findByRole('table')

    const roleSelect = screen.getByLabelText('admin rolü')
    await user.selectOptions(roleSelect, 'Support')

    expect(
      await screen.findByRole('alert'),
    ).toHaveTextContent('Sistemde en az bir aktif yönetici kalmalıdır.')
    expect(roleSelect).toHaveValue('Admin')
  })

  it('waits for cookie bootstrap and preserves a direct Admin route', async () => {
    const me = deferred<Response>()
    listUsers.mockResolvedValueOnce(sampleUsers)
    sessionStorage.clear()
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        if (String(input).includes('/api/auth/me')) {
          return me.promise
        }
        return Promise.resolve(new Response(null, { status: 404 }))
      }),
    )
    window.history.pushState({}, '', '/users')

    render(<App />)

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Oturum doğrulanıyor…',
    )
    expect(window.location.pathname).toBe('/users')
    expect(
      screen.queryByRole('heading', { name: 'Kullanıcılar' }),
    ).not.toBeInTheDocument()

    me.resolve(
      new Response(
        JSON.stringify({
          userId: 'user-1',
          fullName: 'Admin Kullanıcısı',
          username: 'admin',
          role: 'Admin',
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )

    expect(
      await screen.findByRole('heading', { name: 'Kullanıcılar' }),
    ).toBeInTheDocument()
    expect(window.location.pathname).toBe('/users')
  })

  it('redirects a direct Admin route only after bootstrap rejects the cookie', async () => {
    const me = deferred<Response>()
    sessionStorage.clear()
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        if (String(input).includes('/api/auth/me')) {
          return me.promise
        }
        return Promise.resolve(new Response(null, { status: 404 }))
      }),
    )
    window.history.pushState({}, '', '/users')

    render(<App />)

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Oturum doğrulanıyor…',
    )
    expect(window.location.pathname).toBe('/users')

    me.resolve(
      new Response(JSON.stringify({ message: 'Unauthorized' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' },
      }),
    )

    expect(
      await screen.findByRole('heading', { name: 'Hesabınıza giriş yapın' }),
    ).toBeInTheDocument()
    expect(window.location.pathname).toBe('/login')
  })
})
