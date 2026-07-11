import { useCallback, useMemo, useState, type ReactNode } from 'react'
import {
  AuthContext,
  type AuthContextValue,
  type AuthUser,
  type UserRole,
} from './authContext'
import { apiFetch, AUTH_STORAGE_KEY, type StoredAuthSession } from '../api/client'

/** Response shape of POST /api/v1/auth/login. */
interface LoginResponse {
  accessToken: string
  expiresInMinutes: number
  displayName: string
  role: UserRole
}

function userFromSession(session: StoredAuthSession): AuthUser {
  return {
    id: session.username,
    name: session.displayName,
    email: session.username,
    role: session.role as UserRole,
  }
}

function readStoredSession(): StoredAuthSession | null {
  const raw = sessionStorage.getItem(AUTH_STORAGE_KEY)
  if (!raw) return null
  try {
    return JSON.parse(raw) as StoredAuthSession
  } catch {
    return null
  }
}

/**
 * Holds the current user/JWT in state, backed by the real
 * POST /api/v1/auth/login endpoint (mock AD provider — any password is
 * accepted). The session is persisted to sessionStorage (not localStorage,
 * so it clears when the browser/tab closes rather than lingering) so a page
 * refresh doesn't immediately log the user out.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const initialSession = readStoredSession()
  const [user, setUser] = useState<AuthUser | null>(
    initialSession ? userFromSession(initialSession) : null,
  )
  const [token, setToken] = useState<string | null>(
    initialSession?.accessToken ?? null,
  )

  const login = useCallback(async (username: string, password: string) => {
    const response = await apiFetch<LoginResponse>('/api/v1/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    })

    const session: StoredAuthSession = {
      accessToken: response.accessToken,
      displayName: response.displayName,
      role: response.role,
      username,
    }
    sessionStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(session))

    const nextUser = userFromSession(session)
    setUser(nextUser)
    setToken(session.accessToken)
    return nextUser
  }, [])

  const logout = useCallback(() => {
    sessionStorage.removeItem(AUTH_STORAGE_KEY)
    setUser(null)
    setToken(null)
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      token,
      role: user?.role ?? null,
      isAuthenticated: user !== null && token !== null,
      login,
      logout,
    }),
    [user, token, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
