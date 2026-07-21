import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  createUser,
  listUsers,
  setUserPassword,
  updateUser,
} from './usersApi'
import type { UserListItem } from './types'

const sampleUser: UserListItem = {
  id: '11111111-1111-1111-1111-111111111111',
  fullName: 'Admin Kullanıcısı',
  username: 'admin',
  email: 'admin@vshelpdesk.local',
  role: 'Admin',
  isActive: true,
  createdAt: '2026-07-21T10:00:00.000Z',
  lastLoginAt: '2026-07-21T12:00:00.000Z',
}

function clearCsrfCookie() {
  document.cookie = 'vshd.csrf=; Max-Age=0; path=/'
}

describe('users API', () => {
  beforeEach(() => {
    sessionStorage.clear()
    clearCsrfCookie()
    vi.unstubAllGlobals()
  })

  it('listUsers uses GET /api/users with AbortSignal and credentials', async () => {
    const controller = new AbortController()
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([sampleUser]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await listUsers({ signal: controller.signal })

    expect(result).toEqual([sampleUser])
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/users')
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'GET',
      signal: controller.signal,
      credentials: 'include',
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.has('Content-Type')).toBe(false)
    expect(headers.get('Authorization')).toBeNull()
  })

  it('createUser posts create body with CSRF when cookie present', async () => {
    document.cookie = 'vshd.csrf=user-csrf'
    const created: UserListItem = {
      ...sampleUser,
      id: '22222222-2222-2222-2222-222222222222',
      fullName: 'Yeni Destek',
      username: 'support2',
      email: 'support2@example.test',
      role: 'Support',
      lastLoginAt: null,
    }
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(created), {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const body = {
      fullName: 'Yeni Destek',
      username: 'support2',
      email: 'support2@example.test',
      password: 'Password12345!',
      role: 'Support' as const,
    }
    const result = await createUser(body)

    expect(result).toEqual(created)
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/users')
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'POST',
      body: JSON.stringify(body),
      credentials: 'include',
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('X-CSRF-Token')).toBe('user-csrf')
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(headers.get('Authorization')).toBeNull()
  })

  it('updateUser puts profile/role/active to encoded /api/users/{id}', async () => {
    document.cookie = 'vshd.csrf=update-csrf'
    const updated: UserListItem = {
      ...sampleUser,
      role: 'Support',
      isActive: false,
    }
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(updated), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const body = {
      fullName: sampleUser.fullName,
      email: sampleUser.email,
      role: 'Support' as const,
      isActive: false,
    }
    const result = await updateUser('id/with spaces', body)

    expect(result).toEqual(updated)
    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      `/api/users/${encodeURIComponent('id/with spaces')}`,
    )
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'PUT',
      body: JSON.stringify(body),
      credentials: 'include',
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('X-CSRF-Token')).toBe('update-csrf')
  })

  it('setUserPassword posts { password } and accepts 204', async () => {
    document.cookie = 'vshd.csrf=pwd-csrf'
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(null, { status: 204 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await setUserPassword(sampleUser.id, {
      password: 'NewPassword123!',
    })

    expect(result).toBeUndefined()
    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      `/api/users/${encodeURIComponent(sampleUser.id)}/password`,
    )
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'POST',
      body: JSON.stringify({ password: 'NewPassword123!' }),
      credentials: 'include',
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('X-CSRF-Token')).toBe('pwd-csrf')
  })
})
