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
  isAuthenticated: boolean
  login: (user: AuthUser, token: string) => void
  logout: () => void
}

export const AuthContext = createContext<AuthContextValue | undefined>(
  undefined,
)
