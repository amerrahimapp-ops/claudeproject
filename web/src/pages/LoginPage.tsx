import { useState } from 'react'
import { Alert, Button, Card, Form, Input, Typography, message } from 'antd'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/useAuth'
import { ApiError } from '../api/client'

const { Title, Paragraph } = Typography

interface LoginFormValues {
  username: string
  password: string
}

/**
 * Real sign-in form against POST /api/v1/auth/login (mock AD provider —
 * any password is accepted for the 4 dev usernames: requestor.dev,
 * capacitymanager.dev, infrahead.dev, admin).
 */
export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleFinish = async (values: LoginFormValues) => {
    setSubmitting(true)
    setError(null)
    try {
      await login(values.username, values.password)
      navigate('/dashboard')
    } catch (err) {
      const description =
        err instanceof ApiError && err.status === 401
          ? 'Invalid username or password.'
          : 'Login failed. Please try again.'
      setError(description)
      message.error(description)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Card style={{ width: 360 }}>
      <Title level={3}>Project Alpha</Title>
      <Paragraph type="secondary">
        Capacity Request Management System — sign in with a dev username
        (e.g. <code>requestor.dev</code>); any password works.
      </Paragraph>
      {error && (
        <Alert
          type="error"
          message={error}
          showIcon
          style={{ marginBottom: 16 }}
        />
      )}
      <Form layout="vertical" onFinish={handleFinish} disabled={submitting}>
        <Form.Item
          label="Username"
          name="username"
          rules={[{ required: true, message: 'Username is required' }]}
        >
          <Input autoFocus autoComplete="username" />
        </Form.Item>
        <Form.Item
          label="Password"
          name="password"
          rules={[{ required: true, message: 'Password is required' }]}
        >
          <Input.Password autoComplete="current-password" />
        </Form.Item>
        <Form.Item style={{ marginBottom: 0 }}>
          <Button type="primary" htmlType="submit" block loading={submitting}>
            Sign in
          </Button>
        </Form.Item>
      </Form>
    </Card>
  )
}
