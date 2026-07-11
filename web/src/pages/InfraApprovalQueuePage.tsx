import { Typography } from 'antd'
import { ApprovalQueueTable } from '../components/ApprovalQueueTable'

const { Title, Paragraph } = Typography

/**
 * Infra Head's approval queue: requests currently in the `InfraApproval`
 * stage. Approving sends the request on to `done` (Excel generation runs
 * server-side on that transition); Reject/Defer end the request.
 */
export function InfraApprovalQueuePage() {
  return (
    <>
      <Title level={3}>Infra Approval Queue</Title>
      <Paragraph type="secondary">
        Requests awaiting Infra Head approval.
      </Paragraph>
      <ApprovalQueueTable
        status="InfraApproval"
        approveStage="done"
        approveLabel="Approve"
      />
    </>
  )
}
