import { Button, Card, Typography } from 'antd'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/useAuth'

const { Title, Paragraph } = Typography

/**
 * Placeholder login page. Real credential flow (AD via IIdentityProvider)
 * lands in a later phase — this just exercises the AuthContext shape with
 * a mock user so downstream routing/layout can be built against it.
 */
export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()

  const handleMockLogin = () => {
    login(
      {
        id: 'mock-user-1',
        name: 'Mock Requestor',
        email: 'mock.requestor@example.com',
        role: 'Requestor',
      },
      'mock-jwt-token',
    )
    navigate('/dashboard')
  }

  return (
    <Card style={{ width: 360 }}>
      <Title level={3}>Project Alpha</Title>
      <Paragraph type="secondary">
        Capacity Request Management System — Phase 5 will replace this with
        the real sign-in flow.
      </Paragraph>
      <Button type="primary" block onClick={handleMockLogin}>
        Continue with mock session
      </Button>
    </Card>
  )
}
