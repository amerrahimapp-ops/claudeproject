// TODO: move to env var for prod.
const API_BASE_URL = 'http://localhost:5000'

/**
 * sessionStorage key the auth session is persisted under. Shared between
 * AuthProvider (writes it on login/logout) and apiFetch (reads the token
 * from it) so this module doesn't need to depend on React context — it can
 * be called from anywhere, including outside components (e.g. React Query
 * queryFns).
 */
export const AUTH_STORAGE_KEY = 'projectAlpha.auth'

export interface StoredAuthSession {
  accessToken: string
  displayName: string
  role: string
  username: string
}

function getStoredToken(): string | null {
  const raw = sessionStorage.getItem(AUTH_STORAGE_KEY)
  if (!raw) return null
  try {
    const parsed = JSON.parse(raw) as StoredAuthSession
    return parsed.accessToken ?? null
  } catch {
    return null
  }
}

function buildAuthHeaders(existing?: HeadersInit): Headers {
  const headers = new Headers(existing)
  const token = getStoredToken()
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }
  return headers
}

/** Thrown for any non-2xx response from the API. */
export class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

/**
 * Thin fetch wrapper for the Project Alpha API: prefixes the base URL,
 * injects `Authorization: Bearer <token>` from the current session, sets
 * JSON headers, and parses the JSON response — or throws ApiError on a
 * non-2xx status.
 */
export async function apiFetch<T = unknown>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const headers = buildAuthHeaders(options.headers)
  if (!headers.has('Content-Type') && options.body) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...options,
    headers,
  })

  if (!response.ok) {
    const message = await response.text().catch(() => '')
    throw new ApiError(response.status, message || response.statusText)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

/**
 * Same auth-header injection as apiFetch, but returns a raw Blob — for
 * binary downloads such as the Excel report, which apiFetch's JSON parsing
 * can't handle.
 */
export async function apiFetchBlob(
  path: string,
  options: RequestInit = {},
): Promise<Blob> {
  const headers = buildAuthHeaders(options.headers)

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...options,
    headers,
  })

  if (!response.ok) {
    const message = await response.text().catch(() => '')
    throw new ApiError(response.status, message || response.statusText)
  }

  return response.blob()
}

/**
 * Same auth-header injection as apiFetch, but for a multipart/form-data
 * body (file uploads) — deliberately does NOT set Content-Type, since the
 * browser must set it itself with the multipart boundary parameter; setting
 * it manually would break the parse on the server.
 */
export async function apiFetchFormData<T = unknown>(
  path: string,
  formData: FormData,
): Promise<T> {
  const headers = buildAuthHeaders()

  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: 'POST',
    headers,
    body: formData,
  })

  if (!response.ok) {
    const message = await response.text().catch(() => '')
    throw new ApiError(response.status, message || response.statusText)
  }

  return (await response.json()) as T
}
