import { apiFetch } from './client'
import type { UserRole } from '../context/authContext'

/** Mirrors DefaultViewOptions in api/src/Api/Modules/Auth/MeEndpoints.cs. */
export type DefaultView = 'Dashboard' | 'NewRequest' | 'ApprovalQueue'

/** Mirrors ThemeOptions in api/src/Api/Modules/Auth/MeEndpoints.cs. */
export type ThemePreference = 'Light' | 'Dark'

/**
 * Mirrors NotificationPreferences in
 * api/src/Api/Modules/Auth/MeEndpoints.cs — a fixed, small set of
 * per-event-type toggles (not a dynamic schema). Phase 8b (real email
 * notifications) reads these back to decide whether to send.
 */
export interface NotificationPreferences {
  requestStatusChanged: boolean
  newAssignedTask: boolean
}

export interface UserPreferences {
  defaultView: DefaultView
  theme: ThemePreference
  notificationPrefs: NotificationPreferences
}

/**
 * Update body — `defaultView` stays required (mirrors the backend's
 * UpdateUserPreferencesRequest, where it's the one non-optional field);
 * `theme`/`notificationPrefs` are optional and, when omitted, leave the
 * previously stored value untouched server-side. Callers that only want to
 * change one of the optional fields still need to pass the current
 * `defaultView` along (see call sites — they read it off the cached
 * preferences query rather than hardcoding a value).
 */
export type UpdateUserPreferences = Pick<UserPreferences, 'defaultView'> &
  Partial<Omit<UserPreferences, 'defaultView'>>

/** GET /api/v1/me/preferences — any authenticated user, own record only. */
export async function fetchMyPreferences(): Promise<UserPreferences> {
  return apiFetch<UserPreferences>('/api/v1/me/preferences')
}

/** PUT /api/v1/me/preferences — any authenticated user, own record only. */
export async function updateMyPreferences(
  update: UpdateUserPreferences,
): Promise<UserPreferences> {
  return apiFetch<UserPreferences>('/api/v1/me/preferences', {
    method: 'PUT',
    body: JSON.stringify(update),
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
