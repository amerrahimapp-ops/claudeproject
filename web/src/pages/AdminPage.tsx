import { useState } from 'react'
import {
  Alert,
  Button,
  Card,
  Input,
  Space,
  Table,
  Tabs,
  Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useQuery } from '@tanstack/react-query'
import { apiFetch, ApiError } from '../api/client'
import { fetchAuditLog, type AuditLogEntry } from '../api/admin'

const { Title, Paragraph, Text } = Typography

type CheckKey = 'email' | 'grafana' | 'ai'

interface CheckResult {
  status: 'success' | 'error'
  message: string
}

interface CheckDefinition {
  key: CheckKey
  label: string
  run: () => Promise<unknown>
}

// The three real Admin-gated diagnostic endpoints. There is still no user
// management or workflow configuration API — only the audit log viewer
// (Phase 6) has landed on top of these.
const CHECKS: CheckDefinition[] = [
  {
    key: 'email',
    label: 'Test Email',
    run: () =>
      apiFetch('/api/v1/admin/test-email', {
        method: 'POST',
        body: JSON.stringify({ toAddress: 'test@example.com' }),
      }),
  },
  {
    key: 'grafana',
    label: 'Test Grafana',
    run: () => apiFetch('/api/v1/admin/test-grafana'),
  },
  {
    key: 'ai',
    label: 'Test AI Evaluation',
    run: () =>
      apiFetch('/api/v1/admin/test-ai-evaluation', {
        method: 'POST',
        body: JSON.stringify({ requestId: 1 }),
      }),
  },
]

function IntegrationHealthCheck() {
  const [loadingKey, setLoadingKey] = useState<CheckKey | null>(null)
  const [results, setResults] = useState<
    Partial<Record<CheckKey, CheckResult>>
  >({})

  const runCheck = async (check: CheckDefinition) => {
    setLoadingKey(check.key)
    try {
      const body = await check.run()
      setResults((prev) => ({
        ...prev,
        [check.key]: { status: 'success', message: JSON.stringify(body) },
      }))
    } catch (err) {
      const description =
        err instanceof ApiError
          ? `${err.message || err.status}`
          : 'Request failed.'
      setResults((prev) => ({
        ...prev,
        [check.key]: { status: 'error', message: description },
      }))
    } finally {
      setLoadingKey(null)
    }
  }

  return (
    <Card title="Integration Health Check" style={{ maxWidth: 640 }}>
      <Space direction="vertical" style={{ width: '100%' }} size="middle">
        {CHECKS.map((check) => (
          <div key={check.key}>
            <Button
              onClick={() => runCheck(check)}
              loading={loadingKey === check.key}
            >
              {check.label}
            </Button>
            {results[check.key] && (
              <Alert
                style={{ marginTop: 8 }}
                type={results[check.key]?.status}
                message={results[check.key]?.message}
                showIcon
              />
            )}
          </div>
        ))}
      </Space>
    </Card>
  )
}

interface AuditLogFilterState {
  entityType: string
  action: string
}

const EMPTY_FILTERS: AuditLogFilterState = { entityType: '', action: '' }

function AuditLogTab() {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [filters, setFilters] = useState<AuditLogFilterState>(EMPTY_FILTERS)
  const [pendingFilters, setPendingFilters] =
    useState<AuditLogFilterState>(EMPTY_FILTERS)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['auditLog', page, pageSize, filters],
    queryFn: () =>
      fetchAuditLog({
        page,
        pageSize,
        entityType: filters.entityType || undefined,
        action: filters.action || undefined,
      }),
  })

  const applyFilters = () => {
    setPage(1)
    setFilters(pendingFilters)
  }

  const clearFilters = () => {
    setPendingFilters(EMPTY_FILTERS)
    setPage(1)
    setFilters(EMPTY_FILTERS)
  }

  const columns: ColumnsType<AuditLogEntry> = [
    { title: 'ID', dataIndex: 'id', key: 'id', width: 70 },
    { title: 'Entity Type', dataIndex: 'entityType', key: 'entityType' },
    { title: 'Entity ID', dataIndex: 'entityId', key: 'entityId', width: 90 },
    { title: 'Action', dataIndex: 'action', key: 'action' },
    {
      title: 'Performed By',
      dataIndex: 'performedByUserName',
      key: 'performedByUserName',
    },
    {
      title: 'Performed At',
      dataIndex: 'performedAt',
      key: 'performedAt',
      render: (value: string) => new Date(value).toLocaleString(),
    },
  ]

  return (
    <>
      <Paragraph type="secondary">
        Every row currently written here comes from workflow stage
        transitions (see WorkflowEngine.TransitionAsync) — no other action in
        the system logs to the audit trail yet.
      </Paragraph>
      <Space style={{ marginBottom: 16 }} wrap>
        <Input
          placeholder="Filter by entity type (e.g. Request)"
          style={{ width: 240 }}
          value={pendingFilters.entityType}
          onChange={(e) =>
            setPendingFilters((prev) => ({
              ...prev,
              entityType: e.target.value,
            }))
          }
          onPressEnter={applyFilters}
        />
        <Input
          placeholder="Filter by action (e.g. WorkflowTransition)"
          style={{ width: 240 }}
          value={pendingFilters.action}
          onChange={(e) =>
            setPendingFilters((prev) => ({ ...prev, action: e.target.value }))
          }
          onPressEnter={applyFilters}
        />
        <Button type="primary" onClick={applyFilters}>
          Filter
        </Button>
        <Button onClick={clearFilters}>Clear</Button>
      </Space>
      <Table<AuditLogEntry>
        rowKey="id"
        columns={columns}
        dataSource={data?.items ?? []}
        loading={isLoading}
        size="small"
        locale={
          isError
            ? { emptyText: 'Failed to load the audit log. Please try again.' }
            : { emptyText: 'No audit log entries match these filters.' }
        }
        pagination={{
          current: page,
          pageSize,
          total: data?.totalCount ?? 0,
          showSizeChanger: true,
          pageSizeOptions: [25, 50, 100],
          onChange: (nextPage, nextPageSize) => {
            setPage(nextPage)
            setPageSize(nextPageSize)
          },
        }}
        expandable={{
          rowExpandable: (record) =>
            Boolean(record.oldValues || record.newValues),
          expandedRowRender: (record) => (
            <Space direction="vertical" size="small">
              {record.oldValues && (
                <Text code style={{ whiteSpace: 'pre-wrap' }}>
                  Old: {record.oldValues}
                </Text>
              )}
              {record.newValues && (
                <Text code style={{ whiteSpace: 'pre-wrap' }}>
                  New: {record.newValues}
                </Text>
              )}
            </Space>
          ),
        }}
      />
    </>
  )
}

export function AdminPage() {
  return (
    <>
      <Title level={3}>Admin</Title>
      <Paragraph type="secondary">
        User management and workflow configuration are not built yet — the
        backend Admin module doesn&apos;t have endpoints for them. Below:
        the audit log viewer and the integration health check.
      </Paragraph>

      <Tabs
        items={[
          {
            key: 'audit-log',
            label: 'Audit Log',
            children: <AuditLogTab />,
          },
          {
            key: 'health-check',
            label: 'Integration Health Check',
            children: <IntegrationHealthCheck />,
          },
        ]}
      />
    </>
  )
}
