import { apiFetch } from './client'

/** Mirrors `RequestStatus` in api/src/Api.Data/Entities/Enums.cs. */
export type RequestStatusName =
  | 'Draft'
  | 'Submitted'
  | 'AiEvaluation'
  | 'AiReviewed'
  | 'CapacityReview'
  | 'InfraApproval'
  | 'Done'
  | 'Rejected'
  | 'Deferred'

const REQUEST_STATUS_BY_INDEX: readonly RequestStatusName[] = [
  'Draft',
  'Submitted',
  'AiEvaluation',
  'AiReviewed',
  'CapacityReview',
  'InfraApproval',
  'Done',
  'Rejected',
  'Deferred',
]

/** Mirrors `RequestEnvironment` in api/src/Api.Data/Entities/Enums.cs. */
const ENVIRONMENT_BY_INDEX = ['Prod', 'DR', 'UAT', 'SIT', 'Dev'] as const

/** Mirrors `RequestPriority` in api/src/Api.Data/Entities/Enums.cs. */
const PRIORITY_BY_INDEX = ['Low', 'Medium', 'High'] as const

function normalizeEnum<T extends string>(
  value: T | number,
  table: readonly T[],
): T {
  return typeof value === 'number' ? table[value] : value
}

/**
 * GET /api/v1/requests currently serializes the raw EF `Request` entity
 * directly (see api/src/Api/Program.cs:92-101, explicitly commented there
 * as a Phase-3 placeholder ahead of the real DTO/filtering), NOT the
 * `RequestResponse` shape used by the detail (`GET .../{id}`) and
 * `.../transition` endpoints. Concretely: `status`/`environment`/`priority`
 * come back as raw numeric enum values because no JsonStringEnumConverter
 * is registered anywhere in Program.cs — unlike the detail/transition
 * endpoints, which go through `RequestMapper` and emit PascalCase strings.
 * We normalize both shapes defensively (numeric today, string if/when the
 * placeholder is replaced) so this queue code doesn't need to change when
 * that endpoint is fixed.
 */
interface RawRequestListItem {
  id: number
  requestNumber: string
  status: RequestStatusName | number
  environment: (typeof ENVIRONMENT_BY_INDEX)[number] | number
  priority: (typeof PRIORITY_BY_INDEX)[number] | number
  createdAt: string
  updatedAt: string
}

export interface RequestSummary {
  id: number
  requestNumber: string
  status: RequestStatusName
  environment: string
  priority: string
  /**
   * The list endpoint has no per-item "entered current stage" field. As an
   * approximation, `updatedAt` is bumped by the workflow engine every time
   * `status` changes (see WorkflowEngine.TransitionAsync, which sets
   * `request.UpdatedAt = now` in the same save as `request.Status =
   * targetStatus`), so for a request currently sitting in a stage,
   * `updatedAt` is the time it entered that stage — unless something else
   * ever updates the row without a status change, which nothing currently
   * does.
   */
  updatedAt: string
  createdAt: string
}

function toSummary(raw: RawRequestListItem): RequestSummary {
  return {
    id: raw.id,
    requestNumber: raw.requestNumber,
    status: normalizeEnum(raw.status, REQUEST_STATUS_BY_INDEX),
    environment: normalizeEnum(raw.environment, ENVIRONMENT_BY_INDEX),
    priority: normalizeEnum(raw.priority, PRIORITY_BY_INDEX),
    createdAt: raw.createdAt,
    updatedAt: raw.updatedAt,
  }
}

/** Fetches all requests and filters client-side (no server-side status filter yet). */
export async function fetchRequests(): Promise<RequestSummary[]> {
  const raw = await apiFetch<RawRequestListItem[]>('/api/v1/requests')
  return raw.map(toSummary)
}

/** Snake_case stage ids accepted by POST /api/v1/requests/{id}/transition. */
export type TargetStage =
  | 'draft'
  | 'submitted'
  | 'ai_evaluation'
  | 'ai_reviewed'
  | 'capacity_review'
  | 'infra_approval'
  | 'done'
  | 'rejected'
  | 'deferred'

export async function transitionRequest(
  id: number,
  targetStage: TargetStage,
  comments?: string,
): Promise<void> {
  await apiFetch(`/api/v1/requests/${id}/transition`, {
    method: 'POST',
    body: JSON.stringify({
      targetStage,
      comments: comments?.trim() ? comments.trim() : undefined,
    }),
  })
}
