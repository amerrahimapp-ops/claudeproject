import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { Result } from 'antd'
import { useAuth } from '../context/useAuth'
import type { UserRole } from '../context/authContext'

interface RequireRoleProps {
  /** Role(s) allowed to view the wrapped route. */
  allow: UserRole | UserRole[]
  children: ReactNode
}

/**
 * Route guard that hides a page's content from roles that shouldn't see it
 * in the nav/UI. This is a UX nicety only, NOT a security boundary — the
 * real authorization is enforced server-side (WorkflowEngine.TransitionAsync
 * checks WorkflowConfig.RequiredRole and returns 403 on mismatch). Anyone
 * could still hit the API directly regardless of what this component does,
 * so don't rely on it for anything security-sensitive.
 *
 * If not authenticated, or authenticated with a role not in `allow`, shows
 * an "Access Denied" Result instead of the protected content (rather than
 * silently redirecting, so the user understands why they landed here).
 */
export function RequireRole({ allow, children }: RequireRoleProps) {
  const { role, isAuthenticated } = useAuth()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  const allowedRoles = Array.isArray(allow) ? allow : [allow]
  if (!role || !allowedRoles.includes(role)) {
    return (
      <Result
        status="403"
        title="Access Denied"
        subTitle="You don't have permission to view this page."
      />
    )
  }

  return <>{children}</>
}
