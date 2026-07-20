import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import {
  fetchCurrentUser,
  login as loginApi,
  logout as logoutApi,
} from '../api/authApi'
import {
  clearSession,
  getStoredUser,
  setStoredUser,
  type StoredUser,
} from './tokenStorage'

type AuthContextValue = {
  user: StoredUser | null
  isAuthenticated: boolean
  login: (username: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

function toStoredUser(user: {
  userId: string
  fullName: string
  username: string
}): StoredUser {
  return {
    userId: String(user.userId),
    fullName: user.fullName,
    username: user.username,
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<StoredUser | null>(() => getStoredUser())
  /** Bumps to invalidate in-flight /me when login/logout wins the race. */
  const bootstrapGeneration = useRef(0)

  useEffect(() => {
    const generation = ++bootstrapGeneration.current
    fetchCurrentUser()
      .then((me) => {
        if (generation !== bootstrapGeneration.current) {
          return
        }
        const stored = toStoredUser(me)
        setStoredUser(stored)
        setUser(stored)
      })
      .catch(() => {
        if (generation !== bootstrapGeneration.current) {
          return
        }
        clearSession()
        setUser(null)
      })
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    bootstrapGeneration.current += 1
    const result = await loginApi({ username, password })
    const stored = toStoredUser(result)
    setStoredUser(stored)
    setUser(stored)
  }, [])

  const logout = useCallback(async () => {
    bootstrapGeneration.current += 1
    try {
      await logoutApi()
    } catch {
      // Still clear local UI state even if network logout fails.
    }
    clearSession()
    setUser(null)
  }, [])

  const value = useMemo(
    () => ({
      user,
      isAuthenticated: Boolean(user),
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
