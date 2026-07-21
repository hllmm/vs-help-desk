/**
 * Auth storage (Faz 2 cookie auth):
 * - JWT lives in HttpOnly cookie `vshd.auth` — never readable from JS.
 * - Optional profile cache in sessionStorage for UI (name/handle) only.
 * - No access token in sessionStorage/localStorage.
 * - 401 on protected REST → clear local user cache and redirect to /login.
 */

const USER_KEY = 'vshd.user'
/** Legacy key from Bearer era — always cleared. */
const LEGACY_TOKEN_KEY = 'vshd.accessToken'

import type { UserRole } from '../api/types'

export type StoredUser = {
  userId: string
  fullName: string
  username: string
  role: UserRole
}

export function getStoredUser(): StoredUser | null {
  const raw = sessionStorage.getItem(USER_KEY)
  if (!raw) {
    return null
  }

  try {
    return JSON.parse(raw) as StoredUser
  } catch {
    return null
  }
}

export function setStoredUser(user: StoredUser): void {
  sessionStorage.setItem(USER_KEY, JSON.stringify(user))
}

export function clearSession(): void {
  sessionStorage.removeItem(USER_KEY)
  sessionStorage.removeItem(LEGACY_TOKEN_KEY)
}
