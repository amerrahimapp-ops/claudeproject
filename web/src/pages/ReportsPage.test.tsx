import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ReportsPage } from './ReportsPage'

const mockRequests = [
  {
    id: 1,
    requestNumber: 'CAP-2026-0001',
    status: 0,
    environment: 0,
    createdAt: '2026-07-01T00:00:00Z',
  },
]

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <ReportsPage />
    </QueryClientProvider>,
  )
}

describe('ReportsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    sessionStorage.clear()
  })

  it('renders the request list and downloads a report on click', async () => {
    URL.createObjectURL = vi.fn(() => 'blob:mock-url')
    URL.revokeObjectURL = vi.fn()

    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url.includes('/report')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          blob: () => Promise.resolve(new Blob(['fake xlsx contents'])),
        } as Response)
      }
      return Promise.resolve({
        ok: true,
        status: 200,
        json: () => Promise.resolve(mockRequests),
      } as Response)
    })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()

    expect(await screen.findByText('CAP-2026-0001')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /download report/i }))

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/requests/1/report'),
        expect.anything(),
      )
    })
    expect(URL.createObjectURL).toHaveBeenCalled()
  })
})
