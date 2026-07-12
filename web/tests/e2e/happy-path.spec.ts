import { test, expect } from '@playwright/test'
import { apiLogin, fastForwardToCapacityReview, findQueueRow, loginAs } from './helpers'

/**
 * Full happy-path flow (Phase 6 Polish, updated for Phase 7's real
 * data-capturing wizard and AI-evaluation cascade): Requestor creates a
 * request through the real 5-step wizard, Capacity Manager approves it from
 * the real Capacity Review queue via the real transition endpoint, and the
 * status change is reflected back in the UI. Also covers the Excel report
 * download endpoint (network-level only, per the plan — no xlsx parsing).
 *
 * Since Phase 7b, `submitted -> ai_evaluation -> ai_reviewed` happens
 * automatically inside the `submitted` transition itself
 * (WorkflowAutomationService) — it's a real, synchronous Ollama+Grafana
 * call, not simulated here. Only the Requestor's own `capacity_review`
 * confirmation still needs a separate call (no dedicated UI button for it
 * yet — see phase-7a-status.md). The capacity_review -> infra_approval
 * step, the actually interesting/security-relevant transition (requires the
 * CapacityManager role), is driven for real through the browser UI.
 */
test('requestor creates a request; capacity manager approves it from the queue', async ({
  page,
  request,
}) => {
  // The 5-step wizard is more interaction-heavy than the old single-step
  // form, and fastForwardToCapacityReview's `submitted` transition now
  // triggers a real, synchronous Ollama + Grafana call (WorkflowAutomationService,
  // Phase 7b) rather than being instant — give this one real headroom.
  test.setTimeout(90_000)

  // --- Requestor: create the request through the real 5-step wizard ---
  await loginAs(page, 'Requestor')
  await page.goto('/requests/new')

  // Step 1: Requestor Info (read-only, pre-filled from the session) — just advance.
  await page.getByRole('button', { name: 'Next' }).click()

  // Step 2: Project Info.
  await page.getByLabel('Title').fill('E2E happy path request')
  await page.getByLabel('Department').fill('QA')
  await page.getByLabel('Sponsor').fill('E2E Sponsor')
  await page.getByLabel('Project Name').fill('E2E Project')
  await page.getByLabel('Project Code').fill('PC-E2E')
  await page.getByLabel('Environment').click()
  await page.getByTitle('Prod').click()
  await page.getByLabel('Project Type').click()
  await page.getByTitle('New').click()
  await page.getByLabel('Priority').click()
  await page.getByTitle('High').click()
  await page.getByPlaceholder('Start date').fill('2026-08-01')
  await page.getByPlaceholder('Start date').press('Enter')
  await page.getByPlaceholder('End date').fill('2026-12-31')
  await page.getByPlaceholder('End date').press('Enter')
  await page.keyboard.press('Escape') // close the date picker's popup calendar
  await page.getByRole('button', { name: 'Next' }).click()

  // Step 3: Resources — select CPU, fill current/requested values.
  await page.getByRole('checkbox', { name: 'CPU' }).click()
  const spinbuttons = page.getByRole('spinbutton')
  await spinbuttons.nth(0).fill('50')
  await spinbuttons.nth(1).fill('80')
  await page.getByRole('button', { name: 'Next' }).click()

  // Step 4: Server Details — optional, skip.
  await page.getByRole('button', { name: 'Next' }).click()

  // Step 5: Justifications — fill both fixed CPU questions, then submit.
  const justificationBoxes = page.getByRole('textbox')
  await justificationBoxes.nth(0).fill('65% average utilization over the last 30 days.')
  await justificationBoxes.nth(1).fill('Anticipated growth from an upcoming product launch.')
  await page.getByRole('button', { name: 'Submit Request' }).click()

  await expect(page).toHaveURL(/\/requests\/\d+$/)
  const requestId = Number(page.url().split('/').pop())
  expect(Number.isInteger(requestId)).toBeTruthy()

  // The detail page shows a loading Skeleton until its GET request resolves
  // (see RequestDetailPage.tsx), so wait for the heading text itself via a
  // retrying web-first assertion rather than a one-shot innerText() read
  // (which can race the query and observe the pre-render/empty DOM). Since
  // Phase 7a/7c, the heading is "CAP-YYYY-NNNN — <title>", not just the
  // request number, so extract it rather than assuming the whole heading is it.
  const heading = page.getByRole('heading', { level: 3 }).first()
  await expect(heading).toContainText(/CAP-\d{4}-\d{4}/)
  const headingText = (await heading.innerText()).trim()
  const requestNumberMatch = headingText.match(/CAP-\d{4}-\d{4}/)
  expect(requestNumberMatch).not.toBeNull()
  const requestNumberText = requestNumberMatch![0]

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
