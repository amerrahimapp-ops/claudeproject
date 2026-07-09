import { useCallback, useMemo, useState, type ReactNode } from 'react'
import {
  AuthContext,
  type AuthContextValue,
  type AuthUser,
} from './authContext'

/**
 * Holds the current user/JWT in memory.
 *
 * This is a placeholder for Phase 2: it does not call the real API yet
 * (the .NET auth module is being scaffolded in parallel). `login`/`logout`
 * just set local state so later phases have a stable shape
 * (`IIdentityProvider` on the backend, this context on the frontend) to
 * build the real flow against.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null)
  const [token, setToken] = useState<string | null>(null)

  const login = useCallback((nextUser: AuthUser, nextToken: string) => {
    setUser(nextUser)
    setToken(nextToken)
  }, [])

  const logout = useCallback(() => {
    setUser(null)
    setToken(null)
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      token,
      isAuthenticated: user !== null && token !== null,
      login,
      logout,
    }),
    [user, token, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
