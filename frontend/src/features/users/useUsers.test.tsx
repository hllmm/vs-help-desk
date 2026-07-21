import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { UserListItem } from '../../api/types'
import { useUsers } from './useUsers'

const listUsers = vi.hoisted(() => vi.fn())
const createUser = vi.hoisted(() => vi.fn())
const updateUser = vi.hoisted(() => vi.fn())
const setUserPassword = vi.hoisted(() => vi.fn())

vi.mock('../../api/usersApi', () => ({
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
]

describe('useUsers', () => {
  beforeEach(() => {
    listUsers.mockReset()
    createUser.mockReset()
    updateUser.mockReset()
    setUserPassword.mockReset()
  })

  it('moves from initial loading to loaded data', async () => {
    const pending = deferred<UserListItem[]>()
    listUsers.mockReturnValueOnce(pending.promise)

    const { result } = renderHook(() => useUsers())

    expect(result.current.isInitialLoading).toBe(true)
    expect(result.current.hasLoaded).toBe(false)
    expect(result.current.users).toEqual([])

    await act(async () => {
      pending.resolve(sampleUsers)
    })

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    expect(result.current.isInitialLoading).toBe(false)
    expect(result.current.users).toEqual(sampleUsers)
    expect(result.current.error).toBeNull()
  })

  it('classifies TypeError as network and ApiError as server', async () => {
    listUsers.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    const { result } = renderHook(() => useUsers())

    await waitFor(() => {
      expect(result.current.error).toBe('network')
    })

    listUsers.mockRejectedValueOnce(new ApiError(500, 'Server exploded'))

    await act(async () => {
      await result.current.refresh()
    })

    await waitFor(() => {
      expect(result.current.error).toBe('server')
    })
  })

  it('maps last-admin-required from update ApiError body code', async () => {
    listUsers.mockResolvedValueOnce(sampleUsers)
    updateUser.mockRejectedValueOnce(
      new ApiError(400, 'A domain rule was violated.', {
        code: 'last-admin-required',
      }),
    )

    const { result } = renderHook(() => useUsers())
    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    let mutationResult: Awaited<ReturnType<typeof result.current.update>>
    await act(async () => {
      mutationResult = await result.current.update(sampleUsers[0]!.id, {
        fullName: sampleUsers[0]!.fullName,
        email: sampleUsers[0]!.email,
        role: 'Support',
        isActive: true,
      })
    })

    expect(mutationResult!).toEqual({
      ok: false,
      error: 'last-admin-required',
    })
  })

  it('appends created user and applies update to list', async () => {
    listUsers.mockResolvedValueOnce(sampleUsers)
    const created: UserListItem = {
      ...sampleUsers[0]!,
      id: '22222222-2222-2222-2222-222222222222',
      username: 'support2',
      fullName: 'Yeni Destek',
      role: 'Support',
      lastLoginAt: null,
    }
    createUser.mockResolvedValueOnce(created)
    const updated: UserListItem = { ...created, isActive: false }
    updateUser.mockResolvedValueOnce(updated)

    const { result } = renderHook(() => useUsers())
    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    await act(async () => {
      await result.current.create({
        fullName: created.fullName,
        username: created.username,
        email: created.email,
        password: 'Password12345!',
        role: 'Support',
      })
    })

    expect(result.current.users).toEqual([...sampleUsers, created])

    await act(async () => {
      await result.current.update(created.id, {
        fullName: created.fullName,
        email: created.email,
        role: 'Support',
        isActive: false,
      })
    })

    expect(result.current.users).toEqual([...sampleUsers, updated])
  })
})
