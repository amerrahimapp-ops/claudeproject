import { useQuery } from '@tanstack/react-query'
import { Button, Table, Tag, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { PlusOutlined } from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'
import { apiFetch } from '../api/client'

const { Title } = Typography

// GET /api/v1/requests returns the raw Request entity (Program.cs's
// placeholder handler), not the RequestResponse DTO that GET
// /api/v1/requests/{id} uses — so enum fields serialize as their numeric
// value here instead of a string. Handle both shapes defensively.
type EnumField = string | number

interface RequestSummary {
  id: number
  requestNumber: string
  status: EnumField
  environment: EnumField
  priority: EnumField
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
const PRIORITY_LABELS = ['Low', 'Medium', 'High']

function labelFor(value: EnumField, labels: string[]): string {
  return typeof value === 'number' ? (labels[value] ?? String(value)) : value
}

export function DashboardPage() {
  const navigate = useNavigate()
  const { data, isLoading, isError } = useQuery({
    queryKey: ['requests'],
    queryFn: () => apiFetch<RequestSummary[]>('/api/v1/requests'),
  })

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
      render: (value: EnumField) => (
        <Tag>{labelFor(value, STATUS_LABELS)}</Tag>
      ),
    },
    {
      title: 'Environment',
      dataIndex: 'environment',
      key: 'environment',
      render: (value: EnumField) => labelFor(value, ENVIRONMENT_LABELS),
    },
    {
      title: 'Priority',
      dataIndex: 'priority',
      key: 'priority',
      render: (value: EnumField) => labelFor(value, PRIORITY_LABELS),
    },
    {
      title: 'Created At',
      dataIndex: 'createdAt',
      key: 'createdAt',
      render: (value: string) =>
        value ? new Date(value).toLocaleString() : '—',
    },
  ]

  return (
    <>
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          marginBottom: 16,
        }}
      >
        <Title level={3} style={{ margin: 0 }}>
          Dashboard
        </Title>
        <Button
          type="primary"
          icon={<PlusOutlined />}
          onClick={() => navigate('/requests/new')}
        >
          New Request
        </Button>
      </div>
      <Table<RequestSummary>
        rowKey="id"
        loading={isLoading}
        dataSource={data ?? []}
        columns={columns}
        locale={
          isError
            ? { emptyText: 'Failed to load requests. Please try again.' }
            : undefined
        }
        onRow={(record) => ({
          onClick: () => navigate(`/requests/${record.id}`),
          style: { cursor: 'pointer' },
        })}
      />
    </>
  )
}
