import { defineConfig, devices } from '@playwright/test'

/**
 * Real Playwright suite for Project Alpha (Phase 6 Polish). Exercises the
 * actual running stack — MockIdentityProvider login, the workflow
 * transition endpoint, the Excel report endpoint — against a real API +
 * MySQL, not mocks. See web/tests/e2e/README.md-equivalent comments in each
 * spec for what's covered.
 *
 * Prerequisites (both local dev and CI):
 * - MySQL running (docker-compose) with migrations applied + dev users
 *   seeded (Program.cs does this automatically in Development).
 * - The API running on http://localhost:5000 (see web/src/api/client.ts's
 *   hardcoded API_BASE_URL — not configurable via env yet).
 * This config only starts the web dev server; the API + MySQL are started
 * separately (a plain `dotnet run` locally, or the `playwright` CI job's
 * background-process steps — see .github/workflows/ci.yml).
 */
const WEB_PORT = 5173
const BASE_URL = `http://localhost:${WEB_PORT}`

export default defineConfig({
  testDir: './tests/e2e',
  // The suite shares one API + MySQL instance and asserts on request
  // numbering/state transitions, so tests must not run concurrently against
  // each other (a concurrent create in another test could shift request
  // counts/queue contents mid-assertion).
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  // The html reporter's output feeds the CI job's on-failure artifact
  // upload (see .github/workflows/ci.yml) so a failure can be inspected
  // without re-running locally.
  reporter: process.env.CI
    ? [['github'], ['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]]
    : [['list']],
  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: 'npm run dev -- --port 5173 --strictPort',
    url: BASE_URL,
    // Locally, reuse a dev server the developer already has running;
    // in CI always start a fresh one.
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
})
