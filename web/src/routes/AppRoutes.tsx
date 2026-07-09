import { Navigate, Route, Routes } from 'react-router-dom'
import { AuthenticatedLayout } from '../layouts/AuthenticatedLayout'
import { BareLayout } from '../layouts/BareLayout'
import { AdminPage } from '../pages/AdminPage'
import { DashboardPage } from '../pages/DashboardPage'
import { LoginPage } from '../pages/LoginPage'
import { NewRequestPage } from '../pages/NewRequestPage'
import { NotFoundPage } from '../pages/NotFoundPage'
import { RequestDetailPage } from '../pages/RequestDetailPage'

/**
 * Route skeleton for Phase 2. Pages are placeholders (heading + "Phase 5"
 * note) — real page content/guarding (e.g. redirecting unauthenticated
 * users away from the authenticated layout) is built out in Phase 5.
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
        <Route path="/admin" element={<AdminPage />} />
      </Route>

      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  )
}
