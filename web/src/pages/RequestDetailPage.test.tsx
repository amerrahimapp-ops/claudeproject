import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { RequestDetailPage } from './RequestDetailPage'
import { useAuth } from '../context/useAuth'

vi.mock('../context/useAuth', () => ({
  useAuth: vi.fn(),
}))

const mockUseAuth = vi.mocked(useAuth)

const baseRequest = {
  id: 1,
  requestNumber: 'CAP-2026-0001',
  status: 'AiReviewed',
  title: 'Storage Uplift',
  department: 'Engineering',
  projectName: 'Alpha Rollout',
  projectCode: 'PC-100',
  sponsor: 'Jane Sponsor',
  environment: 'Prod',
  projectType: 'Enhancement',
  priority: 'High',
  startDate: '2026-08-01T00:00:00Z',
  endDate: '2026-09-30T00:00:00Z',
  description: null,
  requestorUserId: 1,
  requestorUsername: 'requestor.dev',
  requestorDisplayName: 'Dev Requestor',
  createdAt: '2026-07-01T00:00:00Z',
  updatedAt: '2026-07-01T00:00:00Z',
  workflowStages: [],
}

function renderPage(aiInsightsResponse: unknown) {
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
    logout: vi.fn(),
  })

  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString()
    if (url.includes('/ai-insights')) {
      return Promise.resolve({
        ok: true,
        status: 200,
        json: () => Promise.resolve(aiInsightsResponse),
      } as Response)
    }
    return Promise.resolve({
      ok: true,
      status: 200,
      json: () => Promise.resolve(baseRequest),
    } as Response)
  })
  vi.stubGlobal('fetch', fetchMock)

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/requests/1']}>
        <Routes>
          <Route path="/requests/:id" element={<RequestDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('RequestDetailPage — AI Insights panel', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    sessionStorage.clear()
  })

  it('shows an empty/pending state when the request has not been evaluated yet', async () => {
    renderPage({ latestEvaluation: null, serverUtilization: [] })

    expect(await screen.findByText('AI Insights')).toBeInTheDocument()
    expect(
      await screen.findByText(/hasn't been submitted, or evaluation is still pending/i),
    ).toBeInTheDocument()
  })

  it('renders score, recommendation, flags, and server utilization stats', async () => {
    renderPage({
      latestEvaluation: {
        id: 45,
        evaluatedAt: '2026-07-11T16:31:28.818040',
        score: 70,
        recommendation: 'challenge',
        flags: ['Insufficient historical utilization data'],
      },
      serverUtilization: [
        {
          hostname: 'app01',
          success: true,
          errorMessage: null,
          cpu: { avg: 42.5, max: 88.1, p95: 75.0 },
          memory: { avg: 30.0, max: 60.0, p95: 50.0 },
          disk: { avg: null, max: null, p95: null },
        },
      ],
    })

    expect(await screen.findByText('70')).toBeInTheDocument()
    expect(await screen.findByText('challenge')).toBeInTheDocument()
    expect(
      await screen.findByText('Insufficient historical utilization data'),
    ).toBeInTheDocument()
    expect(await screen.findByText('app01')).toBeInTheDocument()
    expect(screen.getByText(/avg 42.5% · max 88.1% · p95 75.0%/)).toBeInTheDocument()
  })

  it('shows the error message instead of blank metrics for a failed server query', async () => {
    renderPage({
      latestEvaluation: null,
      serverUtilization: [
        {
          hostname: 'down-host',
          success: false,
          errorMessage: 'Grafana query timed out',
          cpu: null,
          memory: null,
          disk: null,
        },
      ],
    })

    expect(await screen.findByText('down-host')).toBeInTheDocument()
    expect(
      screen.getAllByText('Grafana query timed out').length,
    ).toBeGreaterThan(0)
  })
})
