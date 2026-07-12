import { Card, Descriptions, message, Space, Switch, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../context/useAuth'
import {
  fetchMyPreferences,
  updateMyPreferences,
  type NotificationPreferences,
  type ThemePreference,
} from '../api/preferences'

const { Title, Paragraph } = Typography

const NOTIFICATION_PREF_LABELS: { key: keyof NotificationPreferences; label: string }[] = [
  { key: 'requestStatusChanged', label: 'My request status changed' },
  { key: 'newAssignedTask', label: 'A new task is assigned to me' },
]

/**
 * User Profile page (spec 8.3): read-only Name/Email/Role, a theme toggle,
 * and per-event notification switches. No editable contact fields — the
 * `User` entity has nothing beyond what's already shown here (Phase 7a
 * deliberately did not add PF/Contact/Department to `User`; see
 * docs/progress/phase-7a-status.md, "Requestor Info decision").
 *
 * Theme/notificationPrefs both go through the same GET/PUT
 * /api/v1/me/preferences endpoint and the same `['myPreferences']`
 * query-cache key that AuthenticatedLayout.tsx and App.tsx already use —
 * updating here is what makes the header's theme (App.tsx) and this page's
 * switches agree immediately, in every tab/component subscribed to that key.
 */
export function ProfilePage() {
  const { user } = useAuth()
  const queryClient = useQueryClient()

  const { data: preferences, isLoading } = useQuery({
    queryKey: ['myPreferences'],
    queryFn: fetchMyPreferences,
  })

  const updatePreference = useMutation({
    mutationFn: updateMyPreferences,
    onSuccess: (updated) => {
      queryClient.setQueryData(['myPreferences'], updated)
      message.success('Preference updated.')
    },
    onError: () => {
      message.error('Failed to update your preference. Please try again.')
    },
  })

  const handleThemeChange = (checked: boolean) => {
    if (!preferences) return
    const theme: ThemePreference = checked ? 'Dark' : 'Light'
    updatePreference.mutate({
      defaultView: preferences.defaultView,
      theme,
      notificationPrefs: preferences.notificationPrefs,
    })
  }

  const handleNotificationPrefChange = (
    key: keyof NotificationPreferences,
    checked: boolean,
  ) => {
    if (!preferences) return
    updatePreference.mutate({
      defaultView: preferences.defaultView,
      theme: preferences.theme,
      notificationPrefs: { ...preferences.notificationPrefs, [key]: checked },
    })
  }

  return (
    <>
      <Title level={3}>Profile</Title>
      <Paragraph type="secondary">
        Your account details and personal preferences.
      </Paragraph>

      <Card title="Account" size="small" style={{ marginBottom: 16 }}>
        <Descriptions column={1} size="small">
          <Descriptions.Item label="Name">{user?.name ?? '—'}</Descriptions.Item>
          <Descriptions.Item label="Email">{user?.email ?? '—'}</Descriptions.Item>
          <Descriptions.Item label="Role">
            {user?.role ? <Tag>{user.role}</Tag> : '—'}
          </Descriptions.Item>
        </Descriptions>
      </Card>

      <Card title="Appearance" size="small" style={{ marginBottom: 16 }}>
        <Space>
          <Switch
            checked={preferences?.theme === 'Dark'}
            checkedChildren="Dark"
            unCheckedChildren="Light"
            loading={isLoading || updatePreference.isPending}
            onChange={handleThemeChange}
          />
          <Typography.Text type="secondary">
            Switch between light and dark theme.
          </Typography.Text>
        </Space>
      </Card>

      <Card title="Notifications" size="small">
        <Space direction="vertical">
          {NOTIFICATION_PREF_LABELS.map(({ key, label }) => (
            <Space key={key}>
              <Switch
                checked={preferences?.notificationPrefs[key] ?? true}
                loading={isLoading || updatePreference.isPending}
                onChange={(checked) => handleNotificationPrefChange(key, checked)}
              />
              <Typography.Text>{label}</Typography.Text>
            </Space>
          ))}
        </Space>
      </Card>
    </>
  )
}
