import { Layout, Menu, Typography, Space, Tag } from 'antd'
import {
  DashboardOutlined,
  FileAddOutlined,
  SettingOutlined,
  UnorderedListOutlined,
} from '@ant-design/icons'
import { Link, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../context/useAuth'

const { Header, Sider, Content } = Layout
const { Text } = Typography

const NAV_ITEMS = [
  { key: '/dashboard', label: 'Dashboard', icon: <DashboardOutlined /> },
  { key: '/requests/new', label: 'New Request', icon: <FileAddOutlined /> },
  { key: '/admin', label: 'Admin', icon: <SettingOutlined /> },
]

/**
 * Shell for authenticated routes: sidebar + header placeholders.
 * Real navigation/content is built out in Phase 5.
 */
export function AuthenticatedLayout() {
  const location = useLocation()
  const { user } = useAuth()

  // Highlight the closest matching top-level nav item (e.g. /requests/:id
  // has no direct nav entry, so nothing is selected — that's fine for now).
  const selectedKey =
    NAV_ITEMS.find((item) => location.pathname.startsWith(item.key))?.key ?? ''

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider width={220}>
        <div
          style={{
            height: 48,
            display: 'flex',
            alignItems: 'center',
            paddingInline: 16,
          }}
        >
          <Text strong style={{ color: 'rgba(255, 255, 255, 0.85)' }}>
            Project Alpha
          </Text>
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={selectedKey ? [selectedKey] : []}
          items={NAV_ITEMS.map((item) => ({
            key: item.key,
            icon: item.icon,
            label: <Link to={item.key}>{item.label}</Link>,
          }))}
        />
      </Sider>
      <Layout>
        <Header
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
          }}
        >
          <Space>
            <UnorderedListOutlined />
            <Text>Capacity Request Management</Text>
          </Space>
          <Space>
            <Text type="secondary">{user?.name ?? 'Not signed in'}</Text>
            {user && <Tag>{user.role}</Tag>}
          </Space>
        </Header>
        <Content style={{ padding: 16 }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  )
}
