import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from './App'

describe('App shell', () => {
  it('renders without crashing and redirects unauthenticated visitors to /login', () => {
    // jsdom's default location is http://localhost/, which the router
    // redirects "/" -> "/dashboard" -> AuthenticatedLayout. With no signed-in
    // user, AuthenticatedLayout redirects straight to /login rather than
    // rendering the dashboard shell (see AuthenticatedLayout.tsx's doc
    // comment - this is the actual session boundary; previously only the
    // two RequireRole-wrapped queue routes had it, and every other route,
    // including this one, rendered its shell to a signed-out visitor).
    render(<App />)

    expect(screen.getByText('Project Alpha')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument()
    expect(screen.queryByText('Not signed in')).not.toBeInTheDocument()
  })
})
