import { apiRequest } from './client'
import type {
  CreateUserRequest,
  SetUserPasswordRequest,
  UpdateUserRequest,
  UserListItem,
} from './types'

export type ListUsersOptions = {
  signal?: AbortSignal
}

export type CreateUserOptions = {
  signal?: AbortSignal
}

export type UpdateUserOptions = {
  signal?: AbortSignal
}

export type SetUserPasswordOptions = {
  signal?: AbortSignal
}

export function listUsers(
  options: ListUsersOptions = {},
): Promise<UserListItem[]> {
  return apiRequest<UserListItem[]>('/api/users', {
    signal: options.signal,
  })
}

export function createUser(
  body: CreateUserRequest,
  options: CreateUserOptions = {},
): Promise<UserListItem> {
  return apiRequest<UserListItem>('/api/users', {
    method: 'POST',
    body,
    signal: options.signal,
  })
}

export function updateUser(
  id: string,
  body: UpdateUserRequest,
  options: UpdateUserOptions = {},
): Promise<UserListItem> {
  return apiRequest<UserListItem>(
    `/api/users/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      body,
      signal: options.signal,
    },
  )
}

export function setUserPassword(
  id: string,
  body: SetUserPasswordRequest,
  options: SetUserPasswordOptions = {},
): Promise<void> {
  return apiRequest<void>(
    `/api/users/${encodeURIComponent(id)}/password`,
    {
      method: 'POST',
      body,
      signal: options.signal,
    },
  )
}
