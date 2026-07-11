import { test, expect } from '@playwright/test'
import { apiLogin, fastForwardToCapacityReview, findQueueRow, loginAs } from './helpers'

/**
 * Full happy-path flow (Phase 6 Polish): Requestor creates a request,
 * Capacity Manager approves it from the real Capacity Review queue via the
 * real transition endpoint, and the status change is reflected back in the
 * UI. Also covers the Excel report download endpoint (network-level only,
 * per the plan — no xlsx parsing).
 *
 * The frontend has no "Submit" button yet to move a request out of Draft
 * (see docs/progress/phase-6b-status.md - a genuine product gap found while
 * writing this test, out of scope to build here). The intermediate
 * system-owned stages (submitted -> ai_evaluation -> ai_reviewed ->
 * capacity_review) are fast-forwarded via direct API calls as the
 * requesting user — exactly what any real caller would have to do too,
 * since WorkflowEngine allows the owner to drive all of them. The
 * capacity_review -> infra_approval step, which is the actually
 * interesting/security-relevant transition (requires the CapacityManager
 * role), is driven for real through the browser UI.
 */
test('requestor creates a request; capacity manager approves it from the queue', async ({
  page,
  request,
}) => {
  // --- Requestor: create the request through the real UI ---
  await loginAs(page, 'Requestor')
  await page.goto('/requests/new')
  await page.getByLabel('Environment').click()
  await page.getByTitle('Prod').click()
  await page.getByLabel('Project Type').click()
  await page.getByTitle('New').click()
  await page.getByLabel('Priority').click()
  await page.getByTitle('High').click()
  await page.getByRole('button', { name: 'Submit Request' }).click()

  await expect(page).toHaveURL(/\/requests\/\d+$/)
  const requestId = Number(page.url().split('/').pop())
  expect(Number.isInteger(requestId)).toBeTruthy()

  // The detail page shows a loading Skeleton until its GET request resolves
  // (see RequestDetailPage.tsx), so wait for the heading text itself via a
  // retrying web-first assertion rather than a one-shot innerText() read
  // (which can race the query and observe the pre-render/empty DOM).
  const heading = page.getByRole('heading', { level: 3 }).first()
  await expect(heading).toContainText(/CAP-\d{4}-\d{4}/)
  const requestNumberText = (await heading.innerText()).trim()

  // Report download works even in Draft (any authenticated user, per
  // ReportsEndpoints.cs) - confirm the network call succeeds, not the
  // xlsx contents.
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.getByRole('button', { name: 'Download Excel Report' }).click(),
  ])
  expect(download.suggestedFilename()).toBe(`${requestNumberText}.xlsx`)

  // --- Fast-forward the system-owned stages as the requestor (see file doc comment) ---
  const requestorToken = await apiLogin(request, 'Requestor')
  await fastForwardToCapacityReview(request, requestorToken, requestId)

  // --- Capacity Manager: approve it for real, through the queue UI ---
  await loginAs(page, 'CapacityManager')
  await page.goto('/queues/capacity-review')

  const row = await findQueueRow(page, requestNumberText)
  await expect(row).toBeVisible()

  await row.getByRole('button', { name: 'Approve' }).click()
  await page.getByRole('button', { name: 'Approve' }).last().click() // Popconfirm's own confirm button

  await expect(page.getByText('Request updated.')).toBeVisible()
  // The approved request leaves the CapacityReview queue once its status
  // moves to InfraApproval.
  await expect(row).toHaveCount(0)

  // --- Confirm the transition genuinely landed server-side ---
  await page.goto(`/requests/${requestId}`)
  await expect(page.getByText('InfraApproval')).toBeVisible()
})
