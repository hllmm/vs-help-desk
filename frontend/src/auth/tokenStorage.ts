/**
 * Auth storage choice (Day 14):
 * - Store JWT access token in sessionStorage (tab-scoped; cleared when tab closes).
 * - Prefer sessionStorage over localStorage for a shared internship workstation.
 * - No refresh token (UC-001 out of scope). Logout clears the token.
 * - 401 on any protected REST call → clear token and redirect to /login.
 */

const TOKEN_KEY = 'vshd.accessToken'
const USER_KEY = 'vshd.user'

export type StoredUser = {
  userId: string
  fullName: string
  username: string
}

export function getAccessToken(): string | null {
  return sessionStorage.getItem(TOKEN_KEY)
}

export function setSession(token: string, user: StoredUser): void {
  sessionStorage.setItem(TOKEN_KEY, token)
  sessionStorage.setItem(USER_KEY, JSON.stringify(user))
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

export function clearSession(): void {
  sessionStorage.removeItem(TOKEN_KEY)
  sessionStorage.removeItem(USER_KEY)
}
