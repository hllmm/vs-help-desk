import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from 'react'
import { login as loginApi } from '../api/authApi'
import { ApiError } from '../api/client'
import {
  clearSession,
  getAccessToken,
  getStoredUser,
  setSession,
  type StoredUser,
} from './tokenStorage'

type AuthContextValue = {
  user: StoredUser | null
  isAuthenticated: boolean
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<StoredUser | null>(() =>
    getAccessToken() ? getStoredUser() : null,
  )

  const login = useCallback(async (username: string, password: string) => {
    try {
      const result = await loginApi({ username, password })
      const stored: StoredUser = {
        userId: result.userId,
        fullName: result.fullName,
        username: result.username,
      }
      setSession(result.accessToken, stored)
      setUser(stored)
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        throw new Error('Invalid username or password.')
      }
      throw error instanceof Error ? error : new Error('Login failed.')
    }
  }, [])

  const logout = useCallback(() => {
    clearSession()
    setUser(null)
  }, [])

  const value = useMemo(
    () => ({
      user,
      isAuthenticated: Boolean(user && getAccessToken()),
      login,
      logout,
    }),
    [user, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth must be used within AuthProvider')
  }
  return ctx
}
