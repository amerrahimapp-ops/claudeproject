import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { RequireRole } from './RequireRole'
import { useAuth } from '../context/useAuth'

vi.mock('../context/useAuth', () => ({
  useAuth: vi.fn(),
}))

const mockUseAuth = vi.mocked(useAuth)

describe('RequireRole', () => {
  it('shows Access Denied when the role does not match', () => {
    mockUseAuth.mockReturnValue({
      user: { id: '1', name: 'A', email: 'a@example.com', role: 'Requestor' },
      token: 'x',
      role: 'Requestor',
      isAuthenticated: true,
      login: vi.fn(),
      logout: vi.fn(),
    })

    render(
      <MemoryRouter>
        <RequireRole allow="CapacityManager">
          <div>Protected content</div>
        </RequireRole>
      </MemoryRouter>,
    )

    expect(screen.getByText('Access Denied')).toBeInTheDocument()
    expect(screen.queryByText('Protected content')).not.toBeInTheDocument()
  })

  it('renders children when the role matches', () => {
    mockUseAuth.mockReturnValue({
      user: {
        id: '1',
        name: 'A',
        email: 'a@example.com',
        role: 'CapacityManager',
      },
      token: 'x',
      role: 'CapacityManager',
      isAuthenticated: true,
      login: vi.fn(),
      logout: vi.fn(),
    })

    render(
      <MemoryRouter>
        <RequireRole allow="CapacityManager">
          <div>Protected content</div>
        </RequireRole>
      </MemoryRouter>,
    )

    expect(screen.getByText('Protected content')).toBeInTheDocument()
    expect(screen.queryByText('Access Denied')).not.toBeInTheDocument()
  })

  it('redirects to /login when not authenticated', () => {
    mockUseAuth.mockReturnValue({
      user: null,
      token: null,
      role: null,
      isAuthenticated: false,
      login: vi.fn(),
      logout: vi.fn(),
    })

    render(
      <MemoryRouter initialEntries={['/queues/capacity-review']}>
        <RequireRole allow="CapacityManager">
          <div>Protected content</div>
        </RequireRole>
      </MemoryRouter>,
    )

    expect(screen.queryByText('Protected content')).not.toBeInTheDocument()
    expect(screen.queryByText('Access Denied')).not.toBeInTheDocument()
  })
})
