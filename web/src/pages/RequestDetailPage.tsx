import { Typography } from 'antd'
import { useParams } from 'react-router-dom'

const { Title, Paragraph } = Typography

export function RequestDetailPage() {
  const { id } = useParams<{ id: string }>()

  return (
    <>
      <Title level={3}>Request {id}</Title>
      <Paragraph type="secondary">
        Placeholder route — the request detail/workflow view is built in
        Phase 5.
      </Paragraph>
    </>
  )
}
