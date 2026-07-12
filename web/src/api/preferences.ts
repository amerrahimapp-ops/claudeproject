import { apiFetch } from './client'
import type { UserRole } from '../context/authContext'

/** Mirrors DefaultViewOptions in api/src/Api/Modules/Auth/MeEndpoints.cs. */
export type DefaultView = 'Dashboard' | 'NewRequest' | 'ApprovalQueue'

export interface UserPreferences {
  defaultView: DefaultView
}

/** GET /api/v1/me/preferences — any authenticated user, own record only. */
export async function fetchMyPreferences(): Promise<UserPreferences> {
  return apiFetch<UserPreferences>('/api/v1/me/preferences')
}

/** PUT /api/v1/me/preferences — any authenticated user, own record only. */
export async function updateMyPreferences(
  defaultView: DefaultView,
): Promise<UserPreferences> {
  return apiFetch<UserPreferences>('/api/v1/me/preferences', {
    method: 'PUT',
    body: JSON.stringify({ defaultView }),
  })
}

/**
 * Maps a stored DefaultView preference to an actual route, given the
 * logged-in user's role — "ApprovalQueue" means different things per role
 * (or nothing, for a Requestor), so this can't be resolved on the backend
 * without duplicating the frontend's own route table there.
 */
export function resolveDefaultViewRoute(
  defaultView: DefaultView | undefined,
  role: UserRole | null,
): string {
  switch (defaultView) {
    case 'NewRequest':
      return '/requests/new'
    case 'ApprovalQueue':
      if (role === 'CapacityManager') return '/queues/capacity-review'
      if (role === 'InfraHead') return '/queues/infra-approval'
      return '/dashboard'
    case 'Dashboard':
    default:
      return '/dashboard'
  }
}
