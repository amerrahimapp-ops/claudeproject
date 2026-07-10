import { Layout } from 'antd'
import { Outlet } from 'react-router-dom'

const { Content } = Layout

/**
 * Minimal layout for unauthenticated routes (currently just /login) —
 * no sidebar/header chrome.
 */
export function BareLayout() {
  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Content
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Outlet />
      </Content>
    </Layout>
  )
}
