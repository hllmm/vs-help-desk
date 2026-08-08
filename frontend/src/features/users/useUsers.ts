import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import {
  createUser,
  listUsers,
  setUserPassword,
  updateUser,
} from '../../api/usersApi'
import type {
  CreateUserRequest,
  SetUserPasswordRequest,
  UpdateUserRequest,
  UserListItem,
} from '../../api/types'

export type UserLoadErrorKind = 'network' | 'server'

export type UserMutationErrorKind =
  | 'last-admin-required'
  | 'validation'
  | 'not-found'
  | 'network'
  | 'server'

export type UserMutationResult =
  | { ok: true; user?: UserListItem }
  | { ok: false; error: UserMutationErrorKind | null }

export type UseUsersResult = {
  users: readonly UserListItem[]
  hasLoaded: boolean
  isInitialLoading: boolean
  isRefreshing: boolean
  error: UserLoadErrorKind | null
  refresh: () => Promise<void>
  mutatingUserId: string | null
  isCreating: boolean
  create: (body: CreateUserRequest) => Promise<UserMutationResult>
  update: (
    id: string,
    body: UpdateUserRequest,
  ) => Promise<UserMutationResult>
  setPassword: (
    id: string,
    body: SetUserPasswordRequest,
  ) => Promise<UserMutationResult>
}

type UsersRequestState = {
  users: UserListItem[]
  hasLoaded: boolean
  isLoading: boolean
  error: UserLoadErrorKind | null
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function getErrorCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || !('code' in body)) {
    return null
  }
  const code = (body as { code: unknown }).code
  return typeof code === 'string' ? code : null
}

function mapMutationError(error: unknown): UserMutationErrorKind | null {
  if (isAbortError(error)) {
    return null
  }
  if (error instanceof ApiError && error.status === 401) {
    return null
  }
  if (error instanceof TypeError) {
    return 'network'
  }
  if (error instanceof ApiError) {
    if (error.status === 400) {
      if (getErrorCode(error.body) === 'last-admin-required') {
        return 'last-admin-required'
      }
      return 'validation'
    }
    if (error.status === 404) {
      return 'not-found'
    }
    return 'server'
  }
  return 'server'
}

export function useUsers(): UseUsersResult {
  const [state, setState] = useState<UsersRequestState>({
    users: [],
    hasLoaded: false,
    isLoading: false,
    error: null,
  })
  const [mutatingUserId, setMutatingUserId] = useState<string | null>(null)
  const [isCreating, setIsCreating] = useState(false)
  const requestSequence = useRef(0)
  const mutationSequence = useRef(0)
  const activeController = useRef<AbortController | null>(null)
  const mutationController = useRef<AbortController | null>(null)

  const load = useCallback(async () => {
    const sequence = ++requestSequence.current
    activeController.current?.abort()
    const controller = new AbortController()
    activeController.current = controller
    setState((current) => ({ ...current, isLoading: true, error: null }))

    try {
      const users = await listUsers({ signal: controller.signal })
      if (sequence === requestSequence.current) {
        setState({
          users,
          hasLoaded: true,
          isLoading: false,
          error: null,
        })
      }
    } catch (error) {
      if (sequence !== requestSequence.current || isAbortError(error)) {
        return
      }
      if (error instanceof ApiError && error.status === 401) {
        return
      }
      setState((current) => ({
        ...current,
        hasLoaded: true,
        isLoading: false,
        error: error instanceof TypeError ? 'network' : 'server',
      }))
    }
  }, [])

  useEffect(() => {
    const id = setTimeout(() => void load(), 0)
    return () => {
      clearTimeout(id)
      activeController.current?.abort()
      mutationController.current?.abort()
      requestSequence.current += 1
    }
  }, [load])

  const create = useCallback(
    async (body: CreateUserRequest): Promise<UserMutationResult> => {
      const sequence = ++mutationSequence.current
      mutationController.current?.abort()
      const controller = new AbortController()
      mutationController.current = controller
      setIsCreating(true)

      try {
        const user = await createUser(body, { signal: controller.signal })
        if (sequence === mutationSequence.current) {
          setState((current) => ({
            ...current,
            users: [...current.users, user],
          }))
        }
        return { ok: true, user }
      } catch (error) {
        return { ok: false, error: mapMutationError(error) }
      } finally {
        if (sequence === mutationSequence.current) {
          mutationController.current = null
          setIsCreating(false)
        }
      }
    },
    [],
  )

  const update = useCallback(
    async (
      id: string,
      body: UpdateUserRequest,
    ): Promise<UserMutationResult> => {
      const sequence = ++mutationSequence.current
      mutationController.current?.abort()
      const controller = new AbortController()
      mutationController.current = controller
      setMutatingUserId(id)

      try {
        const user = await updateUser(id, body, { signal: controller.signal })
        if (sequence === mutationSequence.current) {
          setState((current) => ({
            ...current,
            users: current.users.map((item) =>
              item.id === id ? user : item,
            ),
          }))
        }
        return { ok: true, user }
      } catch (error) {
        return { ok: false, error: mapMutationError(error) }
      } finally {
        if (sequence === mutationSequence.current) {
          mutationController.current = null
          setMutatingUserId(null)
        }
      }
    },
    [],
  )

  const setPassword = useCallback(
    async (
      id: string,
      body: SetUserPasswordRequest,
    ): Promise<UserMutationResult> => {
      const sequence = ++mutationSequence.current
      mutationController.current?.abort()
      const controller = new AbortController()
      mutationController.current = controller
      setMutatingUserId(id)

      try {
        await setUserPassword(id, body, { signal: controller.signal })
        return { ok: true }
      } catch (error) {
        return { ok: false, error: mapMutationError(error) }
      } finally {
        if (sequence === mutationSequence.current) {
          mutationController.current = null
          setMutatingUserId(null)
        }
      }
    },
    [],
  )

  return {
    users: state.users,
    hasLoaded: state.hasLoaded,
    isInitialLoading: state.isLoading && !state.hasLoaded,
    isRefreshing: state.isLoading && state.hasLoaded,
    error: state.error,
    refresh: load,
    mutatingUserId,
    isCreating,
    create,
    update,
    setPassword,
  }
}
