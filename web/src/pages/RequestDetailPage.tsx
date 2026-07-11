import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Skeleton,
  Tag,
  Timeline,
  Typography,
  message,
} from 'antd'
import { DownloadOutlined } from '@ant-design/icons'
import { useParams } from 'react-router-dom'
import { apiFetch, apiFetchBlob, ApiError } from '../api/client'

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
  environment: string
  projectType: string
  priority: string
  requestorUserId: number
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

  const { data, isLoading, isError } = useQuery({
    queryKey: ['request', id],
    queryFn: () => apiFetch<RequestDetail>(`/api/v1/requests/${id}`),
    enabled: Boolean(id),
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
        </Title>
        <Button
          icon={<DownloadOutlined />}
          onClick={handleDownloadReport}
          loading={downloading}
        >
          Download Excel Report
        </Button>
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
          <Descriptions.Item label="Created At">
            {formatDate(data.createdAt)}
          </Descriptions.Item>
          <Descriptions.Item label="Updated At">
            {formatDate(data.updatedAt)}
          </Descriptions.Item>
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
