import {
  useCallback,
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
import { AuthContext } from './authState'
import {
  clearSession,
  getStoredUser,
  setStoredUser,
  type StoredUser,
} from './tokenStorage'

function toStoredUser(user: {
  userId: string
  fullName: string
  username: string
  role: StoredUser['role']
}): StoredUser {
  return {
    userId: String(user.userId),
    fullName: user.fullName,
    username: user.username,
    role: user.role,
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<StoredUser | null>(() => getStoredUser())
  const [isBootstrapping, setIsBootstrapping] = useState(true)
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
      .finally(() => {
        if (generation === bootstrapGeneration.current) {
          setIsBootstrapping(false)
        }
      })
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    bootstrapGeneration.current += 1
    try {
      const result = await loginApi({ username, password })
      const stored = toStoredUser(result)
      setStoredUser(stored)
      setUser(stored)
    } catch (error) {
      clearSession()
      setUser(null)
      throw error
    } finally {
      setIsBootstrapping(false)
    }
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
    setIsBootstrapping(false)
  }, [])

  const value = useMemo(
    () => ({
      user,
      isAuthenticated: !isBootstrapping && Boolean(user),
      isBootstrapping,
      login,
      logout,
    }),
    [user, isBootstrapping, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
