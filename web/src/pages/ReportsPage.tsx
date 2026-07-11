import { useState } from 'react'
import { Button, message, Table, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useQuery } from '@tanstack/react-query'
import { apiFetch, apiFetchBlob, ApiError } from '../api/client'

const { Title, Paragraph } = Typography

// GET /api/v1/requests is still a Phase-3 placeholder on the backend (see
// Program.cs) — it serializes the raw Request entity, not the
// RequestResponse DTO, so status/environment may come through as their
// numeric enum value instead of a string. Handle both shapes defensively
// (same approach as DashboardPage.tsx).
type EnumField = string | number

interface RequestSummary {
  id: number
  requestNumber: string
  status: EnumField
  environment: EnumField
  createdAt: string
}

const STATUS_LABELS = [
  'Draft',
  'Submitted',
  'AiEvaluation',
  'AiReviewed',
  'CapacityReview',
  'InfraApproval',
  'Done',
  'Rejected',
  'Deferred',
]

const ENVIRONMENT_LABELS = ['Prod', 'DR', 'UAT', 'SIT', 'Dev']

function labelFor(value: EnumField, labels: string[]): string {
  return typeof value === 'number' ? (labels[value] ?? String(value)) : value
}

/**
 * All capacity requests with a per-row Excel report download. Distinct from
 * DashboardPage (which lists requests for navigation) in that this page's
 * whole purpose is the report export, matching RequestDetailPage's
 * single-request download action but across the full request list.
 */
export function ReportsPage() {
  const [downloadingId, setDownloadingId] = useState<number | null>(null)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['requests'],
    queryFn: () => apiFetch<RequestSummary[]>('/api/v1/requests'),
  })

  const handleDownload = async (record: RequestSummary) => {
    setDownloadingId(record.id)
    try {
      const blob = await apiFetchBlob(`/api/v1/requests/${record.id}/report`)
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = `${record.requestNumber}.xlsx`
      document.body.appendChild(anchor)
      anchor.click()
      anchor.remove()
      URL.revokeObjectURL(url)
    } catch (err) {
      message.error(
        err instanceof ApiError
          ? `Failed to download report: ${err.message || err.status}`
          : 'Failed to download report.',
      )
    } finally {
      setDownloadingId(null)
    }
  }

  const columns: ColumnsType<RequestSummary> = [
    {
      title: 'Request Number',
      dataIndex: 'requestNumber',
      key: 'requestNumber',
    },
    {
      title: 'Status',
      dataIndex: 'status',
      key: 'status',
      render: (value: EnumField) => labelFor(value, STATUS_LABELS),
    },
    {
      title: 'Environment',
      dataIndex: 'environment',
      key: 'environment',
      render: (value: EnumField) => labelFor(value, ENVIRONMENT_LABELS),
    },
    {
      title: 'Created At',
      dataIndex: 'createdAt',
      key: 'createdAt',
      render: (value: string) =>
        value ? new Date(value).toLocaleString() : '—',
    },
    {
      title: 'Action',
      key: 'action',
      render: (_, record) => (
        <Button
          size="small"
          loading={downloadingId === record.id}
          onClick={() => handleDownload(record)}
        >
          Download Report
        </Button>
      ),
    },
  ]

  return (
    <>
      <Title level={3}>Reports</Title>
      <Paragraph type="secondary">
        All capacity requests. Download the generated Excel report (Request
        Summary, AI Evaluation Report, Approval Chain) for any request.
      </Paragraph>
      <Table<RequestSummary>
        rowKey="id"
        columns={columns}
        dataSource={data ?? []}
        loading={isLoading}
        size="small"
        locale={
          isError
            ? { emptyText: 'Failed to load requests. Please try again.' }
            : undefined
        }
      />
    </>
  )
}
