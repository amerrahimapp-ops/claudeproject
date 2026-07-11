import { useEffect, useState } from 'react'
import { Button, Input, Popconfirm, Space, Table, Tag, message } from 'antd'
import type { TableColumnsType } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/client'
import {
  fetchRequests,
  transitionRequest,
  type RequestStatusName,
  type RequestSummary,
  type TargetStage,
} from '../api/requests'

const { TextArea } = Input

const PRIORITY_COLOR: Record<string, string> = {
  High: 'red',
  Medium: 'orange',
  Low: 'default',
}

interface ApprovalQueueTableProps {
  /** Which status this queue shows (client-side filter of GET /api/v1/requests). */
  status: RequestStatusName
  /** Stage the "Approve" action transitions to. */
  approveStage: TargetStage
  /** Label for the approve button, e.g. "Approve" or "Send to Infra". */
  approveLabel: string
}

/**
 * Shared table for the Capacity Manager and Infra Head approval queues.
 * Both queues share the same fetch/filter/transition/refresh shape and only
 * differ in which status they filter to and which stage "Approve" targets,
 * so the behavior lives here once and the two page components just supply
 * that config.
 */
export function ApprovalQueueTable({
  status,
  approveStage,
  approveLabel,
}: ApprovalQueueTableProps) {
  const queryClient = useQueryClient()
  const [commentsById, setCommentsById] = useState<Record<number, string>>({})

  const {
    data,
    isLoading,
    error: fetchError,
  } = useQuery({
    queryKey: ['requests'],
    queryFn: fetchRequests,
  })

  const transition = useMutation({
    mutationFn: ({
      id,
      targetStage,
      comments,
    }: {
      id: number
      targetStage: TargetStage
      comments?: string
    }) => transitionRequest(id, targetStage, comments),
    onSuccess: async () => {
      message.success('Request updated.')
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

  useEffect(() => {
    if (fetchError) {
      message.error('Failed to load requests.')
    }
  }, [fetchError])

  const rows = (data ?? []).filter((r) => r.status === status)

  const columns: TableColumnsType<RequestSummary> = [
    {
      title: 'Request Number',
      dataIndex: 'requestNumber',
      key: 'requestNumber',
    },
    {
      title: 'Environment',
      dataIndex: 'environment',
      key: 'environment',
    },
    {
      title: 'Priority',
      dataIndex: 'priority',
      key: 'priority',
      render: (priority: string) => (
        <Tag color={PRIORITY_COLOR[priority] ?? 'default'}>{priority}</Tag>
      ),
    },
    {
      title: 'Entered Stage',
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      // Approximation: `updatedAt` is bumped alongside `status` by the
      // workflow engine on every transition — see the comment on
      // RequestSummary.updatedAt in web/src/api/requests.ts.
      render: (updatedAt: string) => new Date(updatedAt).toLocaleDateString(),
    },
    {
      title: 'Actions',
      key: 'actions',
      render: (_, record) => {
        const isBusy =
          transition.isPending && transition.variables?.id === record.id
        return (
          <Space direction="vertical" style={{ width: '100%' }}>
            <Space>
              <Popconfirm
                title={`${approveLabel}?`}
                okText={approveLabel}
                onConfirm={() =>
                  transition.mutate({
                    id: record.id,
                    targetStage: approveStage,
                    comments: commentsById[record.id],
                  })
                }
              >
                <Button type="primary" size="small" loading={isBusy}>
                  {approveLabel}
                </Button>
              </Popconfirm>
              <Popconfirm
                title="Defer this request?"
                onConfirm={() =>
                  transition.mutate({
                    id: record.id,
                    targetStage: 'deferred',
                    comments: commentsById[record.id],
                  })
                }
              >
                <Button size="small" loading={isBusy}>
                  Defer
                </Button>
              </Popconfirm>
              <Popconfirm
                title="Reject this request?"
                onConfirm={() =>
                  transition.mutate({
                    id: record.id,
                    targetStage: 'rejected',
                    comments: commentsById[record.id],
                  })
                }
              >
                <Button danger size="small" loading={isBusy}>
                  Reject
                </Button>
              </Popconfirm>
            </Space>
            <TextArea
              placeholder="Comments (optional)"
              size="small"
              autoSize
              value={commentsById[record.id] ?? ''}
              onChange={(e) =>
                setCommentsById((prev) => ({
                  ...prev,
                  [record.id]: e.target.value,
                }))
              }
            />
          </Space>
        )
      },
    },
  ]

  return (
    <Table
      rowKey="id"
      columns={columns}
      dataSource={rows}
      loading={isLoading}
      pagination={{ pageSize: 10 }}
      locale={{ emptyText: 'No requests currently in this stage.' }}
    />
  )
}
