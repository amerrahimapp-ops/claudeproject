import type { APIRequestContext, Locator, Page } from '@playwright/test'
import { expect } from '@playwright/test'

/**
 * The API is not proxied by the Vite dev server — web/src/api/client.ts
 * hardcodes this same base URL, so tests that need to call the API directly
 * (rather than through the UI) hit it here too.
 */
export const API_BASE_URL = 'http://localhost:5000'

export type DevRole = 'Admin' | 'Requestor' | 'CapacityManager' | 'InfraHead'

/** Mirrors MockIdentityProvider's hardcoded dev users (any password works). */
export const DEV_USERS: Record<DevRole, { username: string; displayName: string }> = {
  Admin: { username: 'admin', displayName: 'Local Admin' },
  Requestor: { username: 'requestor.dev', displayName: 'Dev Requestor' },
  CapacityManager: { username: 'capacitymanager.dev', displayName: 'Dev Capacity Manager' },
  InfraHead: { username: 'infrahead.dev', displayName: 'Dev Infra Head' },
}

/** Nav item label -> roles allowed to see it, mirroring AuthenticatedLayout's NAV_ITEMS. */
export const NAV_VISIBILITY: Record<string, DevRole[] | 'all'> = {
  Dashboard: 'all',
  'New Request': 'all',
  'Capacity Review': ['CapacityManager', 'Admin'],
  'Infra Approval': ['InfraHead', 'Admin'],
  Reports: 'all',
  Admin: ['Admin'],
}

/** Logs in through the real UI form (not a mocked session) as the given dev role. */
export async function loginAs(page: Page, role: DevRole): Promise<void> {
  const { username } = DEV_USERS[role]
  await page.goto('/login')
  await page.getByLabel('Username').fill(username)
  await page.getByLabel('Password').fill('anything')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/dashboard$/)
}

/**
 * Logs in via the API directly (no browser) to get a bearer token — used to
 * fast-forward a request through the system-owned workflow stages
 * (submitted -> ai_evaluation -> ai_reviewed -> capacity_review) that have
 * no dedicated UI button yet (see phase-6b-status.md: the frontend only
 * exposes "create draft" and the two approval-queue actions). All of these
 * intermediate transitions are legally performable by the request's own
 * owner per WorkflowEngine.TransitionAsync, so this mirrors what any real
 * API caller (e.g. a future "Submit" button) would do.
 */
export async function apiLogin(
  request: APIRequestContext,
  role: DevRole,
): Promise<string> {
  const { username } = DEV_USERS[role]
  const response = await request.post(`${API_BASE_URL}/api/v1/auth/login`, {
    data: { username, password: 'anything' },
  })
  expect(response.ok()).toBeTruthy()
  const body = (await response.json()) as { accessToken: string }
  return body.accessToken
}

export async function apiTransition(
  request: APIRequestContext,
  token: string,
  requestId: number,
  targetStage: string,
): Promise<void> {
  const response = await request.post(
    `${API_BASE_URL}/api/v1/requests/${requestId}/transition`,
    {
      headers: { Authorization: `Bearer ${token}` },
      data: { targetStage },
    },
  )
  expect(response.ok(), `transition to ${targetStage} failed: ${await response.text()}`).toBeTruthy()
}

/**
 * Drives a freshly-created draft request all the way to capacity_review, as
 * its owner. Since Phase 7b, `submitted -> ai_evaluation -> ai_reviewed` is
 * an automatic cascade the API performs synchronously inside the `submitted`
 * transition itself (WorkflowAutomationService) - only `submitted` and the
 * Requestor's own `capacity_review` confirmation are still real, separate
 * calls.
 */
export async function fastForwardToCapacityReview(
  request: APIRequestContext,
  requestorToken: string,
  requestId: number,
): Promise<void> {
  for (const stage of ['submitted', 'capacity_review']) {
    await apiTransition(request, requestorToken, requestId, stage)
  }
}

/**
 * Finds a request's row in one of the approval-queue tables, paging forward
 * as needed. ApprovalQueueTable paginates client-side (it fetches the full
 * list once, then AntD's <Table pagination={{ pageSize: 10 }}> slices it),
 * and the dev database accumulates requests across every run (there's no
 * per-test isolation/cleanup) - so a freshly-created row isn't guaranteed to
 * land on page 1. Walks "Next Page" until the row is found or pagination is
 * exhausted.
 */
export async function findQueueRow(page: Page, requestNumberText: string): Promise<Locator> {
  const row = page.getByRole('row', { name: new RegExp(requestNumberText) })
  const nextPageButton = page.getByRole('listitem', { name: 'Next Page' }).getByRole('button')

  for (let attempt = 0; attempt < 50; attempt++) {
    if ((await row.count()) > 0) {
      return row
    }
    if (!(await nextPageButton.isEnabled())) {
      break
    }
    await nextPageButton.click()
  }

  return row
}
