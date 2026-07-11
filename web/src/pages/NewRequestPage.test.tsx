import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { NewRequestPage } from './NewRequestPage'
import { useAuth } from '../context/useAuth'

vi.mock('../context/useAuth', () => ({
  useAuth: vi.fn(),
}))

const mockUseAuth = vi.mocked(useAuth)

function renderPage() {
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

  return render(
    <MemoryRouter>
      <NewRequestPage />
    </MemoryRouter>,
  )
}

/** Drives an AntD Select: opens the dropdown and clicks the matching option. */
function selectAntdOption(labelText: string, optionText: string) {
  fireEvent.mouseDown(screen.getByLabelText(labelText))
  const options = document.querySelectorAll('.ant-select-item-option-content')
  const match = Array.from(options).find((el) => el.textContent === optionText)
  if (!match) throw new Error(`Option not found: ${optionText}`)
  fireEvent.click(match)
}

function fillProjectInfoStep() {
  fireEvent.change(screen.getByLabelText('Title'), {
    target: { value: 'Q3 Storage Uplift' },
  })
  fireEvent.change(screen.getByLabelText('Department'), {
    target: { value: 'Engineering' },
  })
  fireEvent.change(screen.getByLabelText('Sponsor'), {
    target: { value: 'Jane Sponsor' },
  })
  fireEvent.change(screen.getByLabelText('Project Name'), {
    target: { value: 'Alpha Rollout' },
  })
  fireEvent.change(screen.getByLabelText('Project Code'), {
    target: { value: 'PC-100' },
  })
  selectAntdOption('Environment', 'Prod')
  selectAntdOption('Project Type', 'Enhancement')
  selectAntdOption('Priority', 'High')

  const startInput = screen.getByPlaceholderText('Start date')
  fireEvent.mouseDown(startInput)
  fireEvent.focus(startInput)
  fireEvent.change(startInput, { target: { value: '2026-08-01' } })
  fireEvent.keyDown(startInput, { key: 'Enter', code: 'Enter' })

  const endInput = screen.getByPlaceholderText('End date')
  fireEvent.mouseDown(endInput)
  fireEvent.focus(endInput)
  fireEvent.change(endInput, { target: { value: '2026-09-30' } })
  fireEvent.keyDown(endInput, { key: 'Enter', code: 'Enter' })
  fireEvent.blur(endInput)
}

describe('NewRequestPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    sessionStorage.clear()
  })

  it('shows read-only requestor info sourced from the logged-in user on step 1', () => {
    renderPage()
    expect(screen.getByText('Dev Requestor')).toBeInTheDocument()
    expect(screen.getByText('requestor.dev')).toBeInTheDocument()
  })

  it('blocks navigation past Project Info until required fields are filled', async () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Next' })) // -> Project Info
    fireEvent.click(screen.getByRole('button', { name: 'Next' })) // attempt -> Resources

    expect(await screen.findByText('Title is required')).toBeInTheDocument()
    // Still on Project Info — the Resources step's checkbox group isn't rendered.
    expect(screen.queryByText('Select the resource types')).not.toBeInTheDocument()
  })

  it('blocks Resources -> Server Details until a resource type is selected', async () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Next' })) // -> Project Info
    fillProjectInfoStep()
    fireEvent.click(screen.getByRole('button', { name: 'Next' })) // -> Resources

    expect(await screen.findByText('Storage')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Next' })) // attempt -> Server Details

    expect(
      await screen.findByText('Select at least one resource type.'),
    ).toBeInTheDocument()
  })

  it('walks the full 5-step wizard and submits the expected payload', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: 42, requestNumber: 'CAP-2026-0042' }),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()

    // Step 1: Requestor Info -> Next
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))

    // Step 2: Project Info
    fillProjectInfoStep()
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))

    // Step 3: Resources
    expect(await screen.findByText('Storage')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Storage' }))

    const numberInputs = await screen.findAllByRole('spinbutton')
    fireEvent.change(numberInputs[0], { target: { value: '200' } })
    fireEvent.change(numberInputs[1], { target: { value: '260' } })
    expect(screen.getByText('+30.0%')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Next' }))

    // Step 4: Server Details — add one server row for Storage
    expect(await screen.findByText('Add Server')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /add server/i }))

    const hostnameInput = document.querySelector(
      'input',
    ) as HTMLInputElement | null
    expect(hostnameInput).not.toBeNull()

    // Locate the row's cells by their column position within the table.
    const table = document.querySelector('.ant-table-tbody') as HTMLElement
    const rowInputs = table.querySelectorAll('input')
    fireEvent.change(rowInputs[0], { target: { value: 'app01' } }) // hostname
    fireEvent.change(rowInputs[1], { target: { value: '10.0.0.5' } }) // ip address
    fireEvent.change(rowInputs[2], { target: { value: 'RHEL 8.6' } }) // os
    const rowNumberInputs = table.querySelectorAll('input[role="spinbutton"]')
    fireEvent.change(rowNumberInputs[0], { target: { value: '200' } }) // current
    fireEvent.change(rowNumberInputs[1], { target: { value: '260' } }) // requested

    fireEvent.click(screen.getByRole('button', { name: 'Next' }))

    // Step 5: Justifications
    expect(
      await screen.findByText(
        'What is the current storage utilization and growth trend?',
      ),
    ).toBeInTheDocument()
    const textAreas = screen.getAllByRole('textbox').filter(
      (el) => el.tagName === 'TEXTAREA',
    )
    fireEvent.change(textAreas[0], {
      target: { value: 'Utilization is at 85% and growing 5%/month.' },
    })
    fireEvent.change(textAreas[1], {
      target: { value: 'Needed to support the Alpha Rollout project.' },
    })

    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/requests'),
        expect.objectContaining({ method: 'POST' }),
      )
    })

    const [, options] = fetchMock.mock.calls[0]
    const body = JSON.parse(options.body as string)

    expect(body).toMatchObject({
      title: 'Q3 Storage Uplift',
      department: 'Engineering',
      projectName: 'Alpha Rollout',
      projectCode: 'PC-100',
      sponsor: 'Jane Sponsor',
      environment: 'Prod',
      projectType: 'Enhancement',
      priority: 'High',
    })
    expect(body.resources).toEqual([
      { resourceType: 'Storage', currentValue: 200, requestedValue: 260 },
    ])
    expect(body.servers).toEqual([
      expect.objectContaining({
        hostname: 'app01',
        ipAddress: '10.0.0.5',
        os: 'RHEL 8.6',
        resourceType: 'Storage',
        currentValue: 200,
        requestedValue: 260,
      }),
    ])
    expect(body.justifications).toEqual([
      {
        resourceType: 'Storage',
        questionKey: 'current_utilization',
        answerText: 'Utilization is at 85% and growing 5%/month.',
      },
      {
        resourceType: 'Storage',
        questionKey: 'business_justification',
        answerText: 'Needed to support the Alpha Rollout project.',
      },
    ])
  })
})
