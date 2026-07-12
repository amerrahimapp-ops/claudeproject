import { test, expect } from '@playwright/test'
import { DEV_USERS, NAV_VISIBILITY, loginAs, type DevRole } from './helpers'

/**
 * Login / session-boundary coverage (Phase 6 Polish):
 * - An unauthenticated visitor is redirected to /login.
 * - Each of the 4 dev roles logs in for real (MockIdentityProvider) and
 *   sees exactly the nav items their role is entitled to — a genuine 4-role
 *   matrix, not a single spot-check. Nav gating is UX only (see
 *   RequireRole.tsx / AuthenticatedLayout.tsx); the real boundary is
 *   server-side, which the happy-path spec exercises separately.
 */

test.describe('unauthenticated session boundary', () => {
  test('visiting a protected route redirects to /login', async ({ page }) => {
    await page.goto('/dashboard')
    await expect(page).toHaveURL(/\/login$/)
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
  })
})

test.describe('role-based navigation matrix', () => {
  const roles = Object.keys(DEV_USERS) as DevRole[]

  for (const role of roles) {
    test(`${role} sees only their allowed nav items`, async ({ page }) => {
      await loginAs(page, role)

      const nav = page.getByRole('menu')

      for (const [label, allowed] of Object.entries(NAV_VISIBILITY)) {
        const isVisible = allowed === 'all' || allowed.includes(role)
        const item = nav.getByRole('menuitem', { name: label })
        if (isVisible) {
          await expect(item).toBeVisible()
        } else {
          await expect(item).toHaveCount(0)
        }
      }

      // Confirm the header shows the signed-in user's role tag, so this
      // test is actually verifying the role it claims to (not just that
      // *some* login succeeded). Scoped to <header> because for the Admin
      // role, "Admin" is also a nav link label — an unscoped text match is
      // ambiguous between the two.
      await expect(page.locator('header').getByText(role, { exact: true })).toBeVisible()
    })
  }

  test('CapacityManager is denied the Infra Approval route directly', async ({ page }) => {
    await loginAs(page, 'CapacityManager')
    await page.goto('/queues/infra-approval')
    await expect(page.getByText('Access Denied')).toBeVisible()
  })

  test('InfraHead is denied the Capacity Review route directly', async ({ page }) => {
    await loginAs(page, 'InfraHead')
    await page.goto('/queues/capacity-review')
    await expect(page.getByText('Access Denied')).toBeVisible()
  })
})
