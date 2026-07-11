import { apiFetch } from './client'

export interface AuditLogEntry {
  id: number
  entityType: string
  entityId: number
  action: string
  performedByUserName: string
  performedAt: string
  oldValues: string | null
  newValues: string | null
}

export interface AuditLogPage {
  items: AuditLogEntry[]
  page: number
  pageSize: number
  totalCount: number
}

export interface AuditLogFilters {
  page?: number
  pageSize?: number
  entityType?: string
  entityId?: number
  action?: string
  performedByUserId?: number
}

/** GET /api/v1/admin/audit-log — Admin-only, paginated, newest first. */
export async function fetchAuditLog(
  filters: AuditLogFilters = {},
): Promise<AuditLogPage> {
  const params = new URLSearchParams()
  if (filters.page) params.set('page', String(filters.page))
  if (filters.pageSize) params.set('pageSize', String(filters.pageSize))
  if (filters.entityType) params.set('entityType', filters.entityType)
  if (filters.entityId !== undefined) {
    params.set('entityId', String(filters.entityId))
  }
  if (filters.action) params.set('action', filters.action)
  if (filters.performedByUserId !== undefined) {
    params.set('performedByUserId', String(filters.performedByUserId))
  }

  const qs = params.toString()
  return apiFetch<AuditLogPage>(
    `/api/v1/admin/audit-log${qs ? `?${qs}` : ''}`,
  )
}
