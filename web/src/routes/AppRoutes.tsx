import { Navigate, Route, Routes } from 'react-router-dom'
import { AuthenticatedLayout } from '../layouts/AuthenticatedLayout'
import { BareLayout } from '../layouts/BareLayout'
import { AdminPage } from '../pages/AdminPage'
import { CapacityQueuePage } from '../pages/CapacityQueuePage'
import { DashboardPage } from '../pages/DashboardPage'
import { InfraApprovalQueuePage } from '../pages/InfraApprovalQueuePage'
import { LoginPage } from '../pages/LoginPage'
import { NewRequestPage } from '../pages/NewRequestPage'
import { NotFoundPage } from '../pages/NotFoundPage'
import { ProfilePage } from '../pages/ProfilePage'
import { ReportsPage } from '../pages/ReportsPage'
import { RequestDetailPage } from '../pages/RequestDetailPage'
import { RequireRole } from './RequireRole'

/**
 * Route table. `RequireRole` wraps the two approval-queue routes so a user
 * with the wrong role sees an "Access Denied" page instead of the queue UI
 * — this is a UX nicety only; see RequireRole.tsx for why it's not a real
 * security boundary.
 */
export function AppRoutes() {
  return (
    <Routes>
      <Route element={<BareLayout />}>
        <Route path="/login" element={<LoginPage />} />
      </Route>

      <Route element={<AuthenticatedLayout />}>
        <Route path="/dashboard" element={<DashboardPage />} />
        <Route path="/requests/new" element={<NewRequestPage />} />
        <Route path="/requests/:id" element={<RequestDetailPage />} />
        <Route
          path="/queues/capacity-review"
          element={
            <RequireRole allow={['CapacityManager', 'Admin']}>
              <CapacityQueuePage />
            </RequireRole>
          }
        />
        <Route
          path="/queues/infra-approval"
          element={
            <RequireRole allow={['InfraHead', 'Admin']}>
              <InfraApprovalQueuePage />
            </RequireRole>
          }
        />
        <Route path="/reports" element={<ReportsPage />} />
        <Route path="/profile" element={<ProfilePage />} />
        <Route
          path="/admin"
          element={
            <RequireRole allow={['Admin']}>
              <AdminPage />
            </RequireRole>
          }
        />
      </Route>

      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  )
}
