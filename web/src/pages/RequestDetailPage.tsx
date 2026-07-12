import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Empty,
  List,
  Skeleton,
  Space,
  Table,
  Tag,
  Timeline,
  Typography,
  Upload,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { UploadRequestOption } from 'rc-upload/lib/interface'
import { DownloadOutlined, InboxOutlined } from '@ant-design/icons'
import { useParams } from 'react-router-dom'
import { apiFetch, apiFetchBlob, ApiError } from '../api/client'
import {
  fetchAttachments,
  transitionRequest,
  uploadAttachment,
  type Attachment,
} from '../api/requests'
import { useAuth } from '../context/useAuth'

const { Title, Text } = Typography

// ---------------------------------------------------------------------
// AI Insights panel — response shape mirrors
// api/src/Api/Modules/Ai/AiInsightsEndpoints.cs's AiInsightsResponse
// exactly (see docs/progress/phase-7b-status.md section 3).
// ---------------------------------------------------------------------

interface MetricStats {
  avg: number | null
  max: number | null
  p95: number | null
}

interface ServerUtilization {
  hostname: string
  success: boolean
  errorMessage: string | null
  cpu: MetricStats | null
  memory: MetricStats | null
  disk: MetricStats | null
}

interface LatestAiEvaluation {
  id: number
  evaluatedAt: string
  score: number | null
  recommendation: string | null
  flags: string[]
}

interface AiInsights {
  latestEvaluation: LatestAiEvaluation | null
  serverUtilization: ServerUtilization[]
}

const RECOMMENDATION_COLORS: Record<string, string> = {
  approve: 'green',
  challenge: 'orange',
  reject: 'red',
}

function formatStats(stats: MetricStats | null): string {
  if (!stats) return '—'
  const fmt = (v: number | null) => (v === null ? '—' : `${v.toFixed(1)}%`)
  return `avg ${fmt(stats.avg)} · max ${fmt(stats.max)} · p95 ${fmt(stats.p95)}`
}

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
  /**
   * 1-based position among other requests currently waiting in the same
   * human-reviewed stage (spec 6.3), oldest-first — null whenever the
   * request isn't in CapacityReview/InfraApproval (see RequestMapper.cs /
   * RequestsEndpoints.cs's GET /api/v1/requests/{id}).
   */
  queuePosition: number | null
}

const STAGE_COLORS: Record<string, string> = {
  Pending: 'gray',
  InProgress: 'blue',
  Approved: 'green',
  Rejected: 'red',
  Deferred: 'orange',
}

/** Human-readable label for the two stages a queue position is ever shown for (spec 6.3). */
const QUEUE_STAGE_LABELS: Record<string, string> = {
  CapacityReview: 'Capacity Review',
  InfraApproval: 'Infra Approval',
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

  // Follows the same React Query pattern as the request detail fetch above.
  // `latestEvaluation: null` is a normal, non-error state (request hasn't
  // been evaluated yet — still Draft/Submitted) — rendered as an empty/
  // pending state below, not an Alert.
  const { data: aiInsights, isLoading: aiInsightsLoading } = useQuery({
    queryKey: ['requestAiInsights', id],
    queryFn: () => apiFetch<AiInsights>(`/api/v1/requests/${id}/ai-insights`),
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

  // Uploading is only offered while the request is still "editable-ish" —
  // Draft (not yet submitted) or Submitted (mid-cascade / just submitted).
  // Later stages are under active review, so attaching new evidence there
  // would be surprising rather than useful; this is a UI nicety, not a hard
  // security boundary (the backend's owner-or-Admin check is the real gate).
  const canUploadAttachment =
    isOwnerOrAdmin &&
    data !== undefined &&
    (data.status === 'Draft' || data.status === 'Submitted')

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
      // A submitted/ai_evaluation transition auto-cascades to ai_reviewed
      // (WorkflowAutomationService) and produces a new AiEvaluation row —
      // refresh the AI Insights panel too, or it keeps showing stale
      // "not evaluated yet" data until an unrelated refetch happens.
      await queryClient.invalidateQueries({
        queryKey: ['requestAiInsights', id],
      })
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

  const { data: attachments, isLoading: attachmentsLoading } = useQuery({
    queryKey: ['requestAttachments', id],
    queryFn: () => fetchAttachments(Number(id)),
    enabled: Boolean(id),
  })

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadAttachment(Number(id), file),
    onSuccess: async () => {
      message.success('Attachment uploaded.')
      await queryClient.invalidateQueries({ queryKey: ['requestAttachments', id] })
    },
    onError: (err: unknown) => {
      message.error(
        err instanceof ApiError
          ? err.message || 'Failed to upload attachment.'
          : 'Failed to upload attachment.',
      )
    },
  })

  const handleDownloadAttachment = async (attachment: Attachment) => {
    if (!id) return
    try {
      const blob = await apiFetchBlob(`/api/v1/requests/${id}/attachments/${attachment.id}`)
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = attachment.fileName
      document.body.appendChild(link)
      link.click()
      link.remove()
      URL.revokeObjectURL(url)
    } catch (err) {
      message.error(
        err instanceof ApiError
          ? `Failed to download attachment: ${err.message || err.status}`
          : 'Failed to download attachment.',
      )
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

      {isOwnerOrAdmin &&
        data.queuePosition !== null &&
        QUEUE_STAGE_LABELS[data.status] && (
          <Alert
            style={{ marginBottom: 16 }}
            type="info"
            showIcon
            message={`You are #${data.queuePosition} waiting for ${QUEUE_STAGE_LABELS[data.status]}.`}
          />
        )}

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

      <Card title="AI Insights" style={{ marginTop: 16 }}>
        {aiInsightsLoading ? (
          <Skeleton active paragraph={{ rows: 2 }} />
        ) : (
          <Space direction="vertical" style={{ width: '100%' }} size="middle">
            {aiInsights?.latestEvaluation ? (
              <div>
                <Space wrap>
                  <Text strong>Score:</Text>
                  <Text>{aiInsights.latestEvaluation.score ?? '—'}</Text>
                  <Text strong>Recommendation:</Text>
                  {aiInsights.latestEvaluation.recommendation ? (
                    <Tag
                      color={
                        RECOMMENDATION_COLORS[
                          aiInsights.latestEvaluation.recommendation.toLowerCase()
                        ] ?? 'default'
                      }
                    >
                      {aiInsights.latestEvaluation.recommendation}
                    </Tag>
                  ) : (
                    <Text type="secondary">—</Text>
                  )}
                  <Text type="secondary">
                    Evaluated {formatDate(aiInsights.latestEvaluation.evaluatedAt)}
                  </Text>
                </Space>
                {aiInsights.latestEvaluation.flags.length > 0 && (
                  <div style={{ marginTop: 8 }}>
                    <Text strong>Flags:</Text>
                    <ul style={{ margin: '4px 0 0 20px' }}>
                      {aiInsights.latestEvaluation.flags.map((flag) => (
                        <li key={flag}>
                          <Text type="secondary">{flag}</Text>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
              </div>
            ) : (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="No AI evaluation yet — this request hasn't been submitted, or evaluation is still pending."
              />
            )}

            {aiInsights && aiInsights.serverUtilization.length > 0 && (
              <Table<ServerUtilization>
                rowKey="hostname"
                size="small"
                pagination={false}
                dataSource={aiInsights.serverUtilization}
                columns={
                  [
                    { title: 'Hostname', dataIndex: 'hostname', key: 'hostname' },
                    {
                      title: 'CPU',
                      key: 'cpu',
                      render: (_, record) =>
                        record.success ? (
                          formatStats(record.cpu)
                        ) : (
                          <Text type="danger">{record.errorMessage ?? 'Failed'}</Text>
                        ),
                    },
                    {
                      title: 'Memory',
                      key: 'memory',
                      render: (_, record) =>
                        record.success ? (
                          formatStats(record.memory)
                        ) : (
                          <Text type="danger">{record.errorMessage ?? 'Failed'}</Text>
                        ),
                    },
                    {
                      title: 'Disk',
                      key: 'disk',
                      render: (_, record) =>
                        record.success ? (
                          formatStats(record.disk)
                        ) : (
                          <Text type="danger">{record.errorMessage ?? 'Failed'}</Text>
                        ),
                    },
                  ] as ColumnsType<ServerUtilization>
                }
              />
            )}
          </Space>
        )}
      </Card>

      <Card title="Attachments" style={{ marginTop: 16 }}>
        <Space direction="vertical" style={{ width: '100%' }} size="middle">
          {canUploadAttachment && (
            <Upload.Dragger
              multiple={false}
              showUploadList={false}
              disabled={uploadMutation.isPending}
              customRequest={(options: UploadRequestOption) => {
                uploadMutation.mutate(options.file as File, {
                  onSuccess: (attachment) => options.onSuccess?.(attachment),
                  onError: (err) => options.onError?.(err as Error),
                })
              }}
            >
              <p className="ant-upload-drag-icon">
                <InboxOutlined />
              </p>
              <p className="ant-upload-text">Click or drag a file to upload</p>
              <p className="ant-upload-hint">pdf, xlsx, docx, png, jpg, txt — up to 10MB.</p>
            </Upload.Dragger>
          )}

          {attachmentsLoading ? (
            <Skeleton active paragraph={{ rows: 2 }} />
          ) : attachments && attachments.length > 0 ? (
            <List
              size="small"
              dataSource={attachments}
              renderItem={(attachment) => (
                <List.Item
                  key={attachment.id}
                  actions={[
                    <Button
                      key="download"
                      type="link"
                      icon={<DownloadOutlined />}
                      onClick={() => handleDownloadAttachment(attachment)}
                    >
                      Download
                    </Button>,
                  ]}
                >
                  <List.Item.Meta
                    title={attachment.fileName}
                    description={`Uploaded by ${attachment.uploadedByDisplayName} · ${formatDate(attachment.uploadedAt)}`}
                  />
                </List.Item>
              )}
            />
          ) : (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description="No attachments yet."
            />
          )}
        </Space>
      </Card>
    </>
  )
}
