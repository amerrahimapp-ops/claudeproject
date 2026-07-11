import { useState } from 'react'
import { Button, Card, Form, Select, Typography, message } from 'antd'
import { useNavigate } from 'react-router-dom'
import { apiFetch, ApiError } from '../api/client'

const { Title, Text } = Typography

type Environment = 'Prod' | 'DR' | 'UAT' | 'SIT' | 'Dev'
type ProjectType = 'New' | 'Enhancement' | 'Maintenance' | 'BAU'
type Priority = 'Low' | 'Medium' | 'High'

interface NewRequestFormValues {
  environment: Environment
  projectType: ProjectType
  priority: Priority
}

interface CreatedRequest {
  id: number
  requestNumber: string
}

const ENVIRONMENT_OPTIONS: Environment[] = ['Prod', 'DR', 'UAT', 'SIT', 'Dev']
const PROJECT_TYPE_OPTIONS: ProjectType[] = [
  'New',
  'Enhancement',
  'Maintenance',
  'BAU',
]
const PRIORITY_OPTIONS: Priority[] = ['Low', 'Medium', 'High']

/**
 * Single-step request creation form. Only environment/projectType/priority
 * are persisted by the backend right now (see RequestsDtos.cs
 * CreateRequestRequest) — no justifications/servers/attachments substrate
 * exists yet, so this intentionally isn't a multi-step wizard.
 */
export function NewRequestPage() {
  const navigate = useNavigate()
  const [submitting, setSubmitting] = useState(false)

  const handleFinish = async (values: NewRequestFormValues) => {
    setSubmitting(true)
    try {
      const created = await apiFetch<CreatedRequest>('/api/v1/requests', {
        method: 'POST',
        body: JSON.stringify(values),
      })
      message.success(`Created ${created.requestNumber}`)
      navigate(`/requests/${created.id}`)
    } catch (err) {
      message.error(
        err instanceof ApiError
          ? `Failed to create request: ${err.message || err.status}`
          : 'Failed to create request.',
      )
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <>
      <Title level={3}>New Capacity Request</Title>
      <Text type="secondary">
        Additional details (servers, justifications) will be added in a
        later phase.
      </Text>
      <Card style={{ maxWidth: 480, marginTop: 16 }}>
        <Form layout="vertical" onFinish={handleFinish} disabled={submitting}>
          <Form.Item
            label="Environment"
            name="environment"
            rules={[{ required: true, message: 'Environment is required' }]}
          >
            <Select
              placeholder="Select environment"
              options={ENVIRONMENT_OPTIONS.map((value) => ({
                value,
                label: value,
              }))}
            />
          </Form.Item>
          <Form.Item
            label="Project Type"
            name="projectType"
            rules={[{ required: true, message: 'Project type is required' }]}
          >
            <Select
              placeholder="Select project type"
              options={PROJECT_TYPE_OPTIONS.map((value) => ({
                value,
                label: value,
              }))}
            />
          </Form.Item>
          <Form.Item
            label="Priority"
            name="priority"
            rules={[{ required: true, message: 'Priority is required' }]}
          >
            <Select
              placeholder="Select priority"
              options={PRIORITY_OPTIONS.map((value) => ({
                value,
                label: value,
              }))}
            />
          </Form.Item>
          <Form.Item style={{ marginBottom: 0 }}>
            <Button type="primary" htmlType="submit" loading={submitting}>
              Submit Request
            </Button>
          </Form.Item>
        </Form>
      </Card>
    </>
  )
}
