import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

describe('App shell', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders without crashing and shows the login route by default', async () => {
    // jsdom's default location is http://localhost/, which the router
    // redirects "/" to "/dashboard" -> AuthenticatedLayout, since there is
    // no signed-in user yet the header falls back to "Not signed in".
    // DashboardPage fetches GET /api/v1/requests on mount, so stub fetch to
    // avoid a real network call in this test.
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => [],
      }),
    )

    render(<App />)

    expect(screen.getByText('Project Alpha')).toBeInTheDocument()
    expect(screen.getByText('Not signed in')).toBeInTheDocument()

    await waitFor(() => {
      expect(
        screen.getByRole('heading', { name: 'Dashboard' }),
      ).toBeInTheDocument()
    })
  })
})
