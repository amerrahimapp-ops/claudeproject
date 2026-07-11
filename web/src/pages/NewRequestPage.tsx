import { useState } from 'react'
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Col,
  DatePicker,
  Descriptions,
  Divider,
  Form,
  Input,
  InputNumber,
  Row,
  Select,
  Space,
  Steps,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import type { Dayjs } from 'dayjs'
import { useNavigate } from 'react-router-dom'
import { apiFetch, ApiError } from '../api/client'
import { useAuth } from '../context/useAuth'

const { Title, Text, Paragraph } = Typography
const { TextArea } = Input
const { RangePicker } = DatePicker

// ---------------------------------------------------------------------
// 5-step request wizard (spec 8.4), replacing the old single-step stub.
// Everything gathered across all 5 steps is submitted as one
// POST /api/v1/requests call at the very end (Phase 7a's single-POST-at-
// the-end contract — see api/src/Api/Modules/Requests/RequestsDtos.cs
// `CreateRequestRequest` and docs/progress/phase-7a-status.md). Field names
// below are matched exactly against that DTO.
// ---------------------------------------------------------------------

type Environment = 'Prod' | 'DR' | 'UAT' | 'SIT' | 'Dev'
type ProjectTypeValue = 'New' | 'Enhancement' | 'Maintenance' | 'BAU'
type Priority = 'Low' | 'Medium' | 'High'
type ResourceTypeKey = 'Storage' | 'Cpu' | 'Ram'
type PlatformValue = 'Unix' | 'Wintel'

const ENVIRONMENT_OPTIONS: Environment[] = ['Prod', 'DR', 'UAT', 'SIT', 'Dev']
const PROJECT_TYPE_OPTIONS: ProjectTypeValue[] = [
  'New',
  'Enhancement',
  'Maintenance',
  'BAU',
]
const PRIORITY_OPTIONS: Priority[] = ['Low', 'Medium', 'High']
const PLATFORM_OPTIONS: PlatformValue[] = ['Unix', 'Wintel']
const RESOURCE_TYPES: ResourceTypeKey[] = ['Storage', 'Cpu', 'Ram']
const RESOURCE_TYPE_LABELS: Record<ResourceTypeKey, string> = {
  Storage: 'Storage',
  Cpu: 'CPU',
  Ram: 'RAM',
}

interface ProjectInfoValues {
  title: string
  department: string
  projectName: string
  projectCode: string
  sponsor: string
  environment: Environment
  projectType: ProjectTypeValue
  priority: Priority
  dateRange: [Dayjs, Dayjs]
  description?: string
}

interface ResourceState {
  currentValue: number | null
  requestedValue: number | null
}

type ResourceValuesState = Record<ResourceTypeKey, ResourceState>

const EMPTY_RESOURCE_VALUES: ResourceValuesState = {
  Storage: { currentValue: null, requestedValue: null },
  Cpu: { currentValue: null, requestedValue: null },
  Ram: { currentValue: null, requestedValue: null },
}

interface ServerRow {
  key: string
  resourceType: ResourceTypeKey
  hostname: string
  ipAddress: string
  os: string
  platform: PlatformValue
  isPhysical: boolean
  currentValue: number | null
  requestedValue: number | null
  mountPoint: string
  drApplicable: boolean
  appTier: string
}

let serverRowSeq = 0
function makeServerRow(resourceType: ResourceTypeKey): ServerRow {
  serverRowSeq += 1
  return {
    key: `server-${serverRowSeq}`,
    resourceType,
    hostname: '',
    ipAddress: '',
    os: '',
    platform: 'Unix',
    isPhysical: false,
    currentValue: null,
    requestedValue: null,
    mountPoint: '',
    drApplicable: false,
    appTier: '',
  }
}

interface JustificationQuestion {
  key: string
  label: string
}

/**
 * Fixed, small Q&A set per resource type (spec 8.4 step 5: "generic Q&A
 * pairs", not an elaborate dynamic form builder). Each selected resource
 * type from Step 3 gets these two questions in Step 5.
 */
const JUSTIFICATION_QUESTIONS: Record<ResourceTypeKey, JustificationQuestion[]> = {
  Storage: [
    {
      key: 'current_utilization',
      label: 'What is the current storage utilization and growth trend?',
    },
    {
      key: 'business_justification',
      label: 'Why is the additional storage capacity needed?',
    },
  ],
  Cpu: [
    {
      key: 'current_utilization',
      label: 'What is the current CPU utilization during peak load?',
    },
    {
      key: 'business_justification',
      label: 'Why is the additional CPU capacity needed?',
    },
  ],
  Ram: [
    {
      key: 'current_utilization',
      label: 'What is the current memory utilization during peak load?',
    },
    {
      key: 'business_justification',
      label: 'Why is the additional memory capacity needed?',
    },
  ],
}

interface CreatedRequest {
  id: number
  requestNumber: string
}

/**
 * Display-only uplift %, purely for the user's benefit while filling the
 * form — the server always recomputes this from currentValue/requestedValue
 * and is the only value that is ever trusted or persisted (see
 * RequestsDtos.cs's ResourceSummaryResponse doc comment).
 */
function computeUpliftLabel(
  current: number | null,
  requested: number | null,
): string {
  if (current === null || requested === null) return '—'
  if (current === 0) return '—'
  const pct = ((requested - current) / current) * 100
  return `${pct >= 0 ? '+' : ''}${pct.toFixed(1)}%`
}

const STEP_TITLES = [
  'Requestor Info',
  'Project Info',
  'Resources',
  'Server Details',
  'Justifications',
]

export function NewRequestPage() {
  const navigate = useNavigate()
  const { user } = useAuth()
  const [current, setCurrent] = useState(0)
  const [submitting, setSubmitting] = useState(false)

  const [projectForm] = Form.useForm<ProjectInfoValues>()
  const [projectInfo, setProjectInfo] = useState<ProjectInfoValues | null>(
    null,
  )

  const [selectedResourceTypes, setSelectedResourceTypes] = useState<
    ResourceTypeKey[]
  >([])
  const [resourceValues, setResourceValues] = useState<ResourceValuesState>(
    EMPTY_RESOURCE_VALUES,
  )

  const [servers, setServers] = useState<ServerRow[]>([])
  const [justifications, setJustifications] = useState<
    Record<string, string>
  >({})

  const updateResourceValue = (
    resourceType: ResourceTypeKey,
    field: keyof ResourceState,
    value: number | null,
  ) => {
    setResourceValues((prev) => ({
      ...prev,
      [resourceType]: { ...prev[resourceType], [field]: value },
    }))
  }

  const addServerRow = (resourceType: ResourceTypeKey) => {
    setServers((prev) => [...prev, makeServerRow(resourceType)])
  }

  const removeServerRow = (key: string) => {
    setServers((prev) => prev.filter((row) => row.key !== key))
  }

  const updateServerField = <K extends keyof ServerRow>(
    key: string,
    field: K,
    value: ServerRow[K],
  ) => {
    setServers((prev) =>
      prev.map((row) => (row.key === key ? { ...row, [field]: value } : row)),
    )
  }

  const handleBack = () => setCurrent((c) => Math.max(0, c - 1))

  const handleNext = async () => {
    if (current === 1) {
      try {
        const values = await projectForm.validateFields()
        setProjectInfo(values)
        setCurrent(2)
      } catch {
        // AntD renders the field-level errors inline; nothing else to do.
      }
      return
    }

    if (current === 2) {
      if (selectedResourceTypes.length === 0) {
        message.error('Select at least one resource type.')
        return
      }
      for (const rt of selectedResourceTypes) {
        const v = resourceValues[rt]
        if (v.currentValue === null || v.requestedValue === null) {
          message.error(
            `Enter current and requested values for ${RESOURCE_TYPE_LABELS[rt]}.`,
          )
          return
        }
      }
      setCurrent(3)
      return
    }

    if (current === 3) {
      for (const server of servers) {
        if (!server.hostname.trim() || !server.ipAddress.trim()) {
          message.error('Each server row needs a hostname and IP address.')
          return
        }
        if (server.currentValue === null || server.requestedValue === null) {
          message.error(
            'Each server row needs current and requested values.',
          )
          return
        }
      }
      setCurrent(4)
      return
    }

    setCurrent((c) => c + 1)
  }

  const handleSubmit = async () => {
    if (!projectInfo) {
      message.error('Project info is missing — go back to Step 2.')
      return
    }

    for (const rt of selectedResourceTypes) {
      for (const q of JUSTIFICATION_QUESTIONS[rt]) {
        const val = justifications[`${rt}::${q.key}`]
        if (!val || !val.trim()) {
          message.error(
            `Answer required: "${q.label}" (${RESOURCE_TYPE_LABELS[rt]}).`,
          )
          return
        }
      }
    }

    const [start, end] = projectInfo.dateRange
    const payload = {
      title: projectInfo.title.trim(),
      department: projectInfo.department.trim(),
      projectName: projectInfo.projectName.trim(),
      projectCode: projectInfo.projectCode.trim(),
      sponsor: projectInfo.sponsor.trim(),
      environment: projectInfo.environment,
      projectType: projectInfo.projectType,
      priority: projectInfo.priority,
      // Plain date strings, not toISOString() — that converts to UTC and
      // shifts the calendar date backward for any timezone ahead of UTC
      // (e.g. local midnight Aug 1 in UTC+8 becomes Jul 31 16:00 UTC).
      startDate: start.format('YYYY-MM-DD'),
      endDate: end.format('YYYY-MM-DD'),
      description: projectInfo.description?.trim()
        ? projectInfo.description.trim()
        : null,
      resources: selectedResourceTypes.map((rt) => ({
        resourceType: rt,
        currentValue: resourceValues[rt].currentValue,
        requestedValue: resourceValues[rt].requestedValue,
      })),
      servers: servers.map((s) => ({
        hostname: s.hostname.trim(),
        ipAddress: s.ipAddress.trim(),
        os: s.os.trim() ? s.os.trim() : null,
        isPhysical: s.isPhysical,
        resourceType: s.resourceType,
        currentValue: s.currentValue,
        requestedValue: s.requestedValue,
        mountPoint: s.mountPoint.trim() ? s.mountPoint.trim() : null,
        platform: s.platform,
        drApplicable: s.drApplicable,
        appTier: s.appTier.trim() ? s.appTier.trim() : null,
      })),
      justifications: selectedResourceTypes.flatMap((rt) =>
        JUSTIFICATION_QUESTIONS[rt].map((q) => ({
          resourceType: rt,
          questionKey: q.key,
          answerText: justifications[`${rt}::${q.key}`].trim(),
        })),
      ),
    }

    setSubmitting(true)
    try {
      const created = await apiFetch<CreatedRequest>('/api/v1/requests', {
        method: 'POST',
        body: JSON.stringify(payload),
      })
      message.success(`Created ${created.requestNumber}`)
      navigate(`/requests/${created.id}`)
    } catch (err) {
      message.error(
        err instanceof ApiError
          ? `Failed to create request: ${err.message || err.status}`
          : 'Failed to create request.',
      )
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <>
      <Title level={3}>New Capacity Request</Title>
      <Steps
        current={current}
        items={STEP_TITLES.map((title) => ({ title }))}
        style={{ marginBottom: 24, maxWidth: 900 }}
        size="small"
      />

      {current === 0 && (
        <Card title="Requestor Info" style={{ maxWidth: 640 }}>
          <Descriptions column={1} bordered size="small">
            <Descriptions.Item label="Name">
              {user?.name ?? '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Email">
              {user?.email ?? '—'}
            </Descriptions.Item>
          </Descriptions>
          <Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
            Department is captured per-request in the next step (a
            requestor's department can change between requests, so it isn't
            stored on your profile).
          </Paragraph>
        </Card>
      )}

      {current === 1 && (
        <Card title="Project Info" style={{ maxWidth: 720 }}>
          <Form form={projectForm} layout="vertical">
            <Form.Item
              name="title"
              label="Title"
              rules={[{ required: true, message: 'Title is required' }]}
            >
              <Input placeholder="Short title for this request" />
            </Form.Item>
            <Row gutter={16}>
              <Col span={12}>
                <Form.Item
                  name="department"
                  label="Department"
                  rules={[
                    { required: true, message: 'Department is required' },
                  ]}
                >
                  <Input />
                </Form.Item>
              </Col>
              <Col span={12}>
                <Form.Item
                  name="sponsor"
                  label="Sponsor"
                  rules={[
                    { required: true, message: 'Sponsor is required' },
                  ]}
                >
                  <Input />
                </Form.Item>
              </Col>
            </Row>
            <Row gutter={16}>
              <Col span={12}>
                <Form.Item
                  name="projectName"
                  label="Project Name"
                  rules={[
                    { required: true, message: 'Project name is required' },
                  ]}
                >
                  <Input />
                </Form.Item>
              </Col>
              <Col span={12}>
                <Form.Item
                  name="projectCode"
                  label="Project Code"
                  rules={[
                    { required: true, message: 'Project code is required' },
                  ]}
                >
                  <Input />
                </Form.Item>
              </Col>
            </Row>
            <Row gutter={16}>
              <Col span={8}>
                <Form.Item
                  name="environment"
                  label="Environment"
                  rules={[
                    { required: true, message: 'Environment is required' },
                  ]}
                >
                  <Select
                    placeholder="Select environment"
                    options={ENVIRONMENT_OPTIONS.map((value) => ({
                      value,
                      label: value,
                    }))}
                  />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item
                  name="projectType"
                  label="Project Type"
                  rules={[
                    { required: true, message: 'Project type is required' },
                  ]}
                >
                  <Select
                    placeholder="Select project type"
                    options={PROJECT_TYPE_OPTIONS.map((value) => ({
                      value,
                      label: value,
                    }))}
                  />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item
                  name="priority"
                  label="Priority"
                  rules={[
                    { required: true, message: 'Priority is required' },
                  ]}
                >
                  <Select
                    placeholder="Select priority"
                    options={PRIORITY_OPTIONS.map((value) => ({
                      value,
                      label: value,
                    }))}
                  />
                </Form.Item>
              </Col>
            </Row>
            <Form.Item
              name="dateRange"
              label="Planned Dates (Start – End)"
              rules={[
                { required: true, message: 'Start and end dates are required' },
              ]}
            >
              <RangePicker style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item name="description" label="Description">
              <TextArea rows={3} placeholder="Optional additional detail" />
            </Form.Item>
          </Form>
        </Card>
      )}

      {current === 2 && (
        <Card title="Resources" style={{ maxWidth: 720 }}>
          <Paragraph type="secondary">
            Select the resource types this request covers. The uplift %
            shown is for your reference only — the server always recomputes
            and enforces this value regardless of what's displayed here.
          </Paragraph>
          <Checkbox.Group
            options={RESOURCE_TYPES.map((rt) => ({
              label: RESOURCE_TYPE_LABELS[rt],
              value: rt,
            }))}
            value={selectedResourceTypes}
            onChange={(values) =>
              setSelectedResourceTypes(values as ResourceTypeKey[])
            }
          />
          <Divider />
          <Space direction="vertical" style={{ width: '100%' }} size="middle">
            {selectedResourceTypes.map((rt) => (
              <Card key={rt} size="small" title={RESOURCE_TYPE_LABELS[rt]}>
                <Row gutter={16} align="middle">
                  <Col span={8}>
                    <Text type="secondary">Current value</Text>
                    <InputNumber
                      style={{ width: '100%' }}
                      min={0}
                      value={resourceValues[rt].currentValue}
                      onChange={(v) =>
                        updateResourceValue(rt, 'currentValue', v)
                      }
                    />
                  </Col>
                  <Col span={8}>
                    <Text type="secondary">Requested value</Text>
                    <InputNumber
                      style={{ width: '100%' }}
                      min={0}
                      value={resourceValues[rt].requestedValue}
                      onChange={(v) =>
                        updateResourceValue(rt, 'requestedValue', v)
                      }
                    />
                  </Col>
                  <Col span={8}>
                    <Text type="secondary">Uplift</Text>
                    <div>
                      <Tag>
                        {computeUpliftLabel(
                          resourceValues[rt].currentValue,
                          resourceValues[rt].requestedValue,
                        )}
                      </Tag>
                    </div>
                  </Col>
                </Row>
              </Card>
            ))}
          </Space>
        </Card>
      )}

      {current === 3 && (
        <Card title="Server Details" style={{ maxWidth: 1100 }}>
          <Paragraph type="secondary">
            Optional — add one row per server for each selected resource
            type.
          </Paragraph>
          {selectedResourceTypes.length === 0 && (
            <Alert
              type="warning"
              showIcon
              message="No resource types were selected in Step 3 — go back to add some, or continue without servers."
            />
          )}
          {selectedResourceTypes.map((rt) => {
            const rows = servers.filter((s) => s.resourceType === rt)
            const columns: ColumnsType<ServerRow> = [
              {
                title: 'Hostname',
                key: 'hostname',
                width: 130,
                render: (_, record) => (
                  <Input
                    size="small"
                    value={record.hostname}
                    onChange={(e) =>
                      updateServerField(
                        record.key,
                        'hostname',
                        e.target.value,
                      )
                    }
                  />
                ),
              },
              {
                title: 'IP Address',
                key: 'ipAddress',
                width: 130,
                render: (_, record) => (
                  <Input
                    size="small"
                    value={record.ipAddress}
                    onChange={(e) =>
                      updateServerField(
                        record.key,
                        'ipAddress',
                        e.target.value,
                      )
                    }
                  />
                ),
              },
              {
                title: 'OS',
                key: 'os',
                width: 120,
                render: (_, record) => (
                  <Input
                    size="small"
                    value={record.os}
                    onChange={(e) =>
                      updateServerField(record.key, 'os', e.target.value)
                    }
                  />
                ),
              },
              {
                title: 'Platform',
                key: 'platform',
                width: 110,
                render: (_, record) => (
                  <Select<PlatformValue>
                    size="small"
                    style={{ width: '100%' }}
                    value={record.platform}
                    options={PLATFORM_OPTIONS.map((value) => ({
                      value,
                      label: value,
                    }))}
                    onChange={(value) =>
                      updateServerField(record.key, 'platform', value)
                    }
                  />
                ),
              },
              {
                title: 'Physical',
                key: 'isPhysical',
                width: 80,
                render: (_, record) => (
                  <Checkbox
                    checked={record.isPhysical}
                    onChange={(e) =>
                      updateServerField(
                        record.key,
                        'isPhysical',
                        e.target.checked,
                      )
                    }
                  />
                ),
              },
              {
                title: 'Current',
                key: 'currentValue',
                width: 100,
                render: (_, record) => (
                  <InputNumber
                    size="small"
                    style={{ width: '100%' }}
                    min={0}
                    value={record.currentValue}
                    onChange={(v) =>
                      updateServerField(record.key, 'currentValue', v)
                    }
                  />
                ),
              },
              {
                title: 'Requested',
                key: 'requestedValue',
                width: 100,
                render: (_, record) => (
                  <InputNumber
                    size="small"
                    style={{ width: '100%' }}
                    min={0}
                    value={record.requestedValue}
                    onChange={(v) =>
                      updateServerField(record.key, 'requestedValue', v)
                    }
                  />
                ),
              },
              ...(rt === 'Storage'
                ? [
                    {
                      title: 'Mount Point',
                      key: 'mountPoint',
                      width: 110,
                      render: (_: unknown, record: ServerRow) => (
                        <Input
                          size="small"
                          value={record.mountPoint}
                          onChange={(e) =>
                            updateServerField(
                              record.key,
                              'mountPoint',
                              e.target.value,
                            )
                          }
                        />
                      ),
                    } as ColumnsType<ServerRow>[number],
                  ]
                : []),
              {
                title: 'DR Applicable',
                key: 'drApplicable',
                width: 90,
                render: (_, record) => (
                  <Checkbox
                    checked={record.drApplicable}
                    onChange={(e) =>
                      updateServerField(
                        record.key,
                        'drApplicable',
                        e.target.checked,
                      )
                    }
                  />
                ),
              },
              {
                title: 'App Tier',
                key: 'appTier',
                width: 110,
                render: (_, record) => (
                  <Input
                    size="small"
                    value={record.appTier}
                    onChange={(e) =>
                      updateServerField(
                        record.key,
                        'appTier',
                        e.target.value,
                      )
                    }
                  />
                ),
              },
              {
                title: '',
                key: 'actions',
                width: 50,
                render: (_, record) => (
                  <Button
                    size="small"
                    danger
                    icon={<DeleteOutlined />}
                    onClick={() => removeServerRow(record.key)}
                  />
                ),
              },
            ]

            return (
              <Card
                key={rt}
                size="small"
                title={RESOURCE_TYPE_LABELS[rt]}
                style={{ marginBottom: 16 }}
                extra={
                  <Button
                    size="small"
                    icon={<PlusOutlined />}
                    onClick={() => addServerRow(rt)}
                  >
                    Add Server
                  </Button>
                }
              >
                <Table<ServerRow>
                  rowKey="key"
                  size="small"
                  pagination={false}
                  columns={columns}
                  dataSource={rows}
                  locale={{ emptyText: 'No servers added.' }}
                  scroll={{ x: true }}
                />
              </Card>
            )
          })}
        </Card>
      )}

      {current === 4 && (
        <Card title="Justifications" style={{ maxWidth: 720 }}>
          {selectedResourceTypes.length === 0 && (
            <Alert
              type="warning"
              showIcon
              message="No resource types were selected in Step 3 — go back to add some."
            />
          )}
          {selectedResourceTypes.map((rt) => (
            <Card
              key={rt}
              size="small"
              title={RESOURCE_TYPE_LABELS[rt]}
              style={{ marginBottom: 16 }}
            >
              <Space direction="vertical" style={{ width: '100%' }} size="middle">
                {JUSTIFICATION_QUESTIONS[rt].map((q) => (
                  <div key={q.key}>
                    <Text>{q.label}</Text>
                    <TextArea
                      rows={2}
                      value={justifications[`${rt}::${q.key}`] ?? ''}
                      onChange={(e) =>
                        setJustifications((prev) => ({
                          ...prev,
                          [`${rt}::${q.key}`]: e.target.value,
                        }))
                      }
                    />
                  </div>
                ))}
              </Space>
            </Card>
          ))}
        </Card>
      )}

      <Space style={{ marginTop: 24 }}>
        {current > 0 && <Button onClick={handleBack}>Back</Button>}
        {current < 4 && (
          <Button type="primary" onClick={handleNext}>
            Next
          </Button>
        )}
        {current === 4 && (
          <Button type="primary" loading={submitting} onClick={handleSubmit}>
            Submit Request
          </Button>
        )}
      </Space>
    </>
  )
}
