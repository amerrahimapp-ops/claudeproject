import { useState } from 'react'
import { Alert, Button, Card, Form, Input, Typography, message } from 'antd'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/useAuth'
import { ApiError } from '../api/client'
import { fetchMyPreferences, resolveDefaultViewRoute } from '../api/preferences'

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
      const loggedInUser = await login(values.username, values.password)

      // Respect the user's "default landing page" preference if it can be
      // fetched — a failure here (e.g. transient network blip right after
      // login) shouldn't block a successful sign-in, so fall back to the
      // dashboard rather than surfacing an error.
      let destination = '/dashboard'
      try {
        const preferences = await fetchMyPreferences()
        destination = resolveDefaultViewRoute(
          preferences.defaultView,
          loggedInUser.role,
        )
      } catch {
        // Ignore — destination already defaults to '/dashboard'.
      }

      navigate(destination)
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
