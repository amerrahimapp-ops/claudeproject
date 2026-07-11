import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../context/AuthProvider'
import { LoginPage } from './LoginPage'

describe('LoginPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    sessionStorage.clear()
  })

  it('submits credentials and calls the login API', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        accessToken: 'test-token',
        expiresInMinutes: 60,
        displayName: 'Dev Requestor',
        role: 'Requestor',
      }),
    })
    vi.stubGlobal('fetch', fetchMock)

    render(
      <MemoryRouter>
        <AuthProvider>
          <LoginPage />
        </AuthProvider>
      </MemoryRouter>,
    )

    fireEvent.change(screen.getByLabelText('Username'), {
      target: { value: 'requestor.dev' },
    })
    fireEvent.change(screen.getByLabelText('Password'), {
      target: { value: 'anything' },
    })
    fireEvent.click(screen.getByRole('button', { name: /sign in/i }))

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/auth/login'),
        expect.objectContaining({ method: 'POST' }),
      )
    })
  })

  it('shows an error message on invalid credentials', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      statusText: 'Unauthorized',
      text: async () => '',
    })
    vi.stubGlobal('fetch', fetchMock)

    render(
      <MemoryRouter>
        <AuthProvider>
          <LoginPage />
        </AuthProvider>
      </MemoryRouter>,
    )

    fireEvent.change(screen.getByLabelText('Username'), {
      target: { value: 'unknown.user' },
    })
    fireEvent.change(screen.getByLabelText('Password'), {
      target: { value: 'anything' },
    })
    fireEvent.click(screen.getByRole('button', { name: /sign in/i }))

    expect(
      await screen.findByText('Invalid username or password.'),
    ).toBeInTheDocument()
  })
})
