import { Typography } from 'antd'

const { Title, Paragraph } = Typography

export function DashboardPage() {
  return (
    <>
      <Title level={3}>Dashboard</Title>
      <Paragraph type="secondary">
        Placeholder route — the real request queue/dashboard is built in
        Phase 5.
      </Paragraph>
    </>
  )
}
