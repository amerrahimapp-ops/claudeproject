import { createContext } from 'react'

/**
 * Roles per the design spec's Phase 1 scope (Group Capacity, Group Capacity
 * Head, HOD, Group Infra Fulfillment are Phase 2+ and not modeled yet).
 */
export type UserRole = 'Requestor' | 'CapacityManager' | 'InfraHead' | 'Admin'

export interface AuthUser {
  id: string
  name: string
  email: string
  role: UserRole
}

export interface AuthContextValue {
  user: AuthUser | null
  token: string | null
  /** Convenience accessor for user?.role — for parallel-built nav guards/UI. */
  role: UserRole | null
  isAuthenticated: boolean
  /**
   * Calls POST /api/v1/auth/login; throws ApiError on a non-2xx response.
   * Returns the logged-in user so callers (e.g. LoginPage, resolving the
   * default-landing-page preference) don't have to wait on a re-render to
   * read the fresh role off of `useAuth()`.
   */
  login: (username: string, password: string) => Promise<AuthUser>
  logout: () => void
}

export const AuthContext = createContext<AuthContextValue | undefined>(
  undefined,
)
