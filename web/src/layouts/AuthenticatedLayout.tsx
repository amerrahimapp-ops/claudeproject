import type { ReactNode } from 'react'
import { Layout, Menu, Typography, Space, Tag } from 'antd'
import {
  CheckSquareOutlined,
  DashboardOutlined,
  FileAddOutlined,
  FileTextOutlined,
  SafetyCertificateOutlined,
  SettingOutlined,
  UnorderedListOutlined,
} from '@ant-design/icons'
import { Link, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../context/useAuth'
import type { UserRole } from '../context/authContext'

const { Header, Sider, Content } = Layout
const { Text } = Typography

interface NavItem {
  key: string
  label: string
  icon: ReactNode
  /** Roles allowed to see this item; omit to show it to every role. */
  roles?: UserRole[]
}

const NAV_ITEMS: NavItem[] = [
  { key: '/dashboard', label: 'Dashboard', icon: <DashboardOutlined /> },
  { key: '/requests/new', label: 'New Request', icon: <FileAddOutlined /> },
  {
    key: '/queues/capacity-review',
    label: 'Capacity Review',
    icon: <CheckSquareOutlined />,
    // CapacityManager owns this queue. Admin can reasonably see both
    // approval queues (and the Infra one below) for oversight/troubleshooting
    // even though Admin never owns a WorkflowConfig.RequiredRole stage
    // itself — the backend transition endpoint is the real gate, so letting
    // Admin view (not necessarily act on) both queues is low-risk here.
    roles: ['CapacityManager', 'Admin'],
  },
  {
    key: '/queues/infra-approval',
    label: 'Infra Approval',
    icon: <SafetyCertificateOutlined />,
    roles: ['InfraHead', 'Admin'],
  },
  { key: '/reports', label: 'Reports', icon: <FileTextOutlined /> },
  { key: '/admin', label: 'Admin', icon: <SettingOutlined /> },
]

/**
 * Shell for authenticated routes: sidebar + header.
 */
export function AuthenticatedLayout() {
  const location = useLocation()
  const { user, role } = useAuth()

  const visibleNavItems = NAV_ITEMS.filter(
    (item) => !item.roles || (role !== null && item.roles.includes(role)),
  )

  // Highlight the closest matching top-level nav item (e.g. /requests/:id
  // has no direct nav entry, so nothing is selected — that's fine for now).
  const selectedKey =
    visibleNavItems.find((item) => location.pathname.startsWith(item.key))
      ?.key ?? ''

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
          items={visibleNavItems.map((item) => ({
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
