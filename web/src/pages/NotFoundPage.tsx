import { Typography } from 'antd'

const { Title, Paragraph } = Typography

export function NotFoundPage() {
  return (
    <>
      <Title level={3}>Not found</Title>
      <Paragraph type="secondary">
        This route doesn&apos;t exist. Check the URL, or use the sidebar
        navigation.
      </Paragraph>
    </>
  )
}
