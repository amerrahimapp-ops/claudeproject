import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Skeleton,
  Space,
  Tag,
  Timeline,
  Typography,
  message,
} from 'antd'
import { DownloadOutlined } from '@ant-design/icons'
import { useParams } from 'react-router-dom'
import { apiFetch, apiFetchBlob, ApiError } from '../api/client'
import { transitionRequest } from '../api/requests'
import { useAuth } from '../context/useAuth'

const { Title, Text } = Typography

interface WorkflowStage {
  id: number
  stageName: string
  status: string
  assignedRole: string | null
  startedAt: string | null
  completedAt: string | null
  comments: string | null
}

interface RequestDetail {
  id: number
  requestNumber: string
  status: string
  title: string
  department: string
  projectName: string
  projectCode: string
  sponsor: string
  environment: string
  projectType: string
  priority: string
  startDate: string
  endDate: string
  description: string | null
  requestorUserId: number
  requestorUsername: string
  requestorDisplayName: string
  createdAt: string
  updatedAt: string
  workflowStages: WorkflowStage[]
}

const STAGE_COLORS: Record<string, string> = {
  Pending: 'gray',
  InProgress: 'blue',
  Approved: 'green',
  Rejected: 'red',
  Deferred: 'orange',
}

function formatDate(value: string | null): string {
  return value ? new Date(value).toLocaleString() : '—'
}

export function RequestDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [downloading, setDownloading] = useState(false)
  const { user, role } = useAuth()
  const queryClient = useQueryClient()

  const { data, isLoading, isError } = useQuery({
    queryKey: ['request', id],
    queryFn: () => apiFetch<RequestDetail>(`/api/v1/requests/${id}`),
    enabled: Boolean(id),
  })

  // Mirrors WorkflowEngine.cs's own ownership check ("the request's owner,
  // or an Admin") — the backend is the real gate (see WorkflowEngine.cs);
  // this only controls whether the buttons are shown at all. `user.id` is
  // the AD username (see AuthProvider.tsx), which is what the API returns
  // as `requestorUsername` — comparing on username avoids needing to decode
  // the JWT just to get the numeric user id on the frontend.
  const isOwnerOrAdmin =
    role === 'Admin' ||
    (data !== undefined && user?.id === data.requestorUsername)

  const transition = useMutation({
    mutationFn: ({
      targetStage,
    }: {
      targetStage: 'submitted' | 'capacity_review'
    }) => transitionRequest(Number(id), targetStage),
    onSuccess: async () => {
      message.success('Request updated.')
      await queryClient.invalidateQueries({ queryKey: ['request', id] })
      await queryClient.invalidateQueries({ queryKey: ['requests'] })
    },
    onError: (err: unknown) => {
      if (err instanceof ApiError && err.status === 403) {
        message.error("You don't have permission to perform this action.")
      } else if (err instanceof ApiError) {
        message.error(err.message || 'Failed to update request.')
      } else {
        message.error('Failed to update request.')
      }
    },
  })

  const handleDownloadReport = async () => {
    if (!id) return
    setDownloading(true)
    try {
      const blob = await apiFetchBlob(`/api/v1/requests/${id}/report`)
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = `${data?.requestNumber ?? `request-${id}`}.xlsx`
      document.body.appendChild(link)
      link.click()
      link.remove()
      URL.revokeObjectURL(url)
    } catch (err) {
      message.error(
        err instanceof ApiError
          ? `Failed to download report: ${err.message || err.status}`
          : 'Failed to download report.',
      )
    } finally {
      setDownloading(false)
    }
  }

  if (isLoading) {
    return <Skeleton active />
  }

  if (isError || !data) {
    return (
      <Alert
        type="error"
        showIcon
        message="Failed to load request"
        description="The request may not exist, or the server is unreachable."
      />
    )
  }

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
          {data.requestNumber}
          {data.title ? ` — ${data.title}` : ''}
        </Title>
        <Space>
          {isOwnerOrAdmin && data.status === 'Draft' && (
            <Button
              type="primary"
              loading={transition.isPending}
              onClick={() => transition.mutate({ targetStage: 'submitted' })}
            >
              Submit Request
            </Button>
          )}
          {isOwnerOrAdmin && data.status === 'AiReviewed' && (
            <>
              <Button
                loading={transition.isPending}
                onClick={() => transition.mutate({ targetStage: 'submitted' })}
              >
                Revise
              </Button>
              <Button
                type="primary"
                loading={transition.isPending}
                onClick={() =>
                  transition.mutate({ targetStage: 'capacity_review' })
                }
              >
                Confirm &amp; Send to Capacity Review
              </Button>
            </>
          )}
          <Button
            icon={<DownloadOutlined />}
            onClick={handleDownloadReport}
            loading={downloading}
          >
            Download Excel Report
          </Button>
        </Space>
      </div>

      <Card style={{ marginBottom: 16 }}>
        <Descriptions column={2} bordered size="small">
          <Descriptions.Item label="Status">
            <Tag>{data.status}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="Environment">
            {data.environment}
          </Descriptions.Item>
          <Descriptions.Item label="Project Type">
            {data.projectType}
          </Descriptions.Item>
          <Descriptions.Item label="Priority">
            {data.priority}
          </Descriptions.Item>
          <Descriptions.Item label="Department">
            {data.department}
          </Descriptions.Item>
          <Descriptions.Item label="Sponsor">
            {data.sponsor}
          </Descriptions.Item>
          <Descriptions.Item label="Project Name">
            {data.projectName}
          </Descriptions.Item>
          <Descriptions.Item label="Project Code">
            {data.projectCode}
          </Descriptions.Item>
          <Descriptions.Item label="Requestor">
            {data.requestorDisplayName}
          </Descriptions.Item>
          <Descriptions.Item label="Planned Dates">
            {formatDate(data.startDate)} – {formatDate(data.endDate)}
          </Descriptions.Item>
          <Descriptions.Item label="Created At">
            {formatDate(data.createdAt)}
          </Descriptions.Item>
          <Descriptions.Item label="Updated At">
            {formatDate(data.updatedAt)}
          </Descriptions.Item>
          {data.description && (
            <Descriptions.Item label="Description" span={2}>
              {data.description}
            </Descriptions.Item>
          )}
        </Descriptions>
      </Card>

      <Card title="Workflow History">
        {data.workflowStages.length === 0 ? (
          <Text type="secondary">No workflow stages recorded yet.</Text>
        ) : (
          <Timeline
            items={data.workflowStages.map((stage) => ({
              color: STAGE_COLORS[stage.status] ?? 'gray',
              children: (
                <div key={stage.id}>
                  <Text strong>{stage.stageName}</Text>{' '}
                  <Tag color={STAGE_COLORS[stage.status] ?? 'default'}>
                    {stage.status}
                  </Tag>
                  {stage.assignedRole && (
                    <div>
                      <Text type="secondary">
                        Assigned role: {stage.assignedRole}
                      </Text>
                    </div>
                  )}
                  <div>
                    <Text type="secondary">
                      Started: {formatDate(stage.startedAt)} · Completed:{' '}
                      {formatDate(stage.completedAt)}
                    </Text>
                  </div>
                  {stage.comments && (
                    <div>
                      <Text type="secondary">Comments: {stage.comments}</Text>
                    </div>
                  )}
                </div>
              ),
            }))}
          />
        )}
      </Card>
    </>
  )
}
