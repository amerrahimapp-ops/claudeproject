import { configDefaults, defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/test/setup.ts',
    // web/tests/e2e is the real Playwright suite (see playwright.config.ts)
    // - its *.spec.ts files match Vitest's default include glob too, so
    // without this exclude Vitest tries to run them as unit tests and fails
    // (Playwright's test()/test.describe() only work under the Playwright
    // runner).
    exclude: [...configDefaults.exclude, 'tests/e2e/**'],
  },
})
