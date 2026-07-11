import { Typography } from 'antd'
import { ApprovalQueueTable } from '../components/ApprovalQueueTable'

const { Title, Paragraph } = Typography

/**
 * Capacity Manager's approval queue: requests currently in the
 * `CapacityReview` stage. Approving sends the request on to
 * `infra_approval`; Reject/Defer end the request (see WorkflowConfig for
 * the allowed-transition graph this mirrors on the backend).
 */
export function CapacityQueuePage() {
  return (
    <>
      <Title level={3}>Capacity Review Queue</Title>
      <Paragraph type="secondary">
        Requests awaiting Capacity Manager approval.
      </Paragraph>
      <ApprovalQueueTable
        status="CapacityReview"
        approveStage="infra_approval"
        approveLabel="Approve"
      />
    </>
  )
}
