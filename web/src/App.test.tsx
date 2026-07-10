import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from './App'

describe('App shell', () => {
  it('renders without crashing and shows the login route by default', () => {
    // jsdom's default location is http://localhost/, which the router
    // redirects "/" to "/dashboard" -> AuthenticatedLayout, since there is
    // no signed-in user yet the header falls back to "Not signed in".
    render(<App />)

    expect(screen.getByText('Project Alpha')).toBeInTheDocument()
    expect(
      screen.getByText(/real request queue\/dashboard is built in Phase 5/i),
    ).toBeInTheDocument()
  })
})
