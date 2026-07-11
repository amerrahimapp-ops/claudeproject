import { useState } from 'react'
import { Alert, Button, Card, Space, Typography } from 'antd'
import { apiFetch, ApiError } from '../api/client'

const { Title, Paragraph } = Typography

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

// Only the three real Admin-gated diagnostic endpoints that exist today —
// there is no user management, workflow configuration, or audit log API
// yet, so this page doesn't pretend those exist.
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

export function AdminPage() {
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
    <>
      <Title level={3}>Admin</Title>
      <Paragraph type="secondary">
        User management, workflow configuration, and audit log viewing are
        not built yet — the backend Admin module doesn&apos;t have endpoints
        for them. What&apos;s below is the integration health check, backed
        by the three diagnostic endpoints that do exist.
      </Paragraph>

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
    </>
  )
}
