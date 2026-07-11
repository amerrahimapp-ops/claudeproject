import { fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AuthenticatedLayout } from './AuthenticatedLayout'
import { useAuth } from '../context/useAuth'

vi.mock('../context/useAuth', () => ({
  useAuth: vi.fn(),
}))

const mockUseAuth = vi.mocked(useAuth)

function renderLayout() {
  const logout = vi.fn()
  mockUseAuth.mockReturnValue({
    user: {
      id: 'requestor.dev',
      name: 'Dev Requestor',
      email: 'requestor.dev',
      role: 'Requestor',
    },
    token: 'x',
    role: 'Requestor',
    isAuthenticated: true,
    login: vi.fn(),
    logout,
  })

  const fetchMock = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: () => Promise.resolve({ defaultView: 'Dashboard' }),
  } as Response)
  vi.stubGlobal('fetch', fetchMock)

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route path="/login" element={<div>Login Page</div>} />
          <Route element={<AuthenticatedLayout />}>
            <Route path="/dashboard" element={<div>Dashboard Content</div>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )

  return { logout }
}

describe('AuthenticatedLayout — logout', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    sessionStorage.clear()
  })

  it('calls logout() and navigates to /login when Logout is clicked', async () => {
    const { logout } = renderLayout()

    expect(await screen.findByText('Dashboard Content')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /logout/i }))

    expect(logout).toHaveBeenCalledTimes(1)
    expect(await screen.findByText('Login Page')).toBeInTheDocument()
  })
})
