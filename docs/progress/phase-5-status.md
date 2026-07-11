# Phase 5 — Frontend: status (complete)

Done, built via 3 parallel Claude Code subagents + direct backend fixes:

- **Real login**: `AuthProvider` calls `POST /api/v1/auth/login` for real,
  session persisted to `sessionStorage` (survives refresh, cleared on
  logout). `LoginPage` is a real AntD form, no more mock-session button.
- **Request creation**: single-step form (Environment/ProjectType/Priority
  — matches what the backend actually persists; not a fictitious 5-step
  wizard around data that doesn't exist yet).
- **Request list/detail**: Dashboard shows a real paginated table from
  `GET /api/v1/requests`; detail page shows full workflow-stage timeline
  and an Excel report download.
- **Approval queues**: Capacity Manager and Infra Head queues, each
  filtered client-side by status, with Approve/Reject/Defer actions
  calling the real transition endpoint.
- **Role-based nav + route guards**: nav items and routes gated by role
  (explicitly documented as UX only — the backend transition endpoint is
  the real security boundary).
- **Reports page**: real request list + working Excel download per row.
- **Admin page**: honest placeholder (user mgmt/workflow config/audit log
  genuinely aren't built yet) plus a working "Integration Health Check"
  panel wired to the three real diagnostic endpoints (test-email,
  test-grafana, test-ai-evaluation).

## Real bugs found and fixed (backend, not frontend)
- **No CORS middleware existed at all** — every browser-based API call
  from the Vite dev server (:5173) to the API (:5000) failed with a 405
  preflight. All three parallel subagents hit this independently; fixed
  once, directly, in `Program.cs` (dev-only CORS policy).
- **`GET /api/v1/requests` returned raw entities**, not the same
  `RequestResponse` DTO shape the detail endpoint uses — `status`/
  `environment`/`priority` serialized as numeric enum values instead of
  strings. Fixed to go through `RequestMapper` like every other endpoint.

## Verification
- Backend: 23/23 tests passing.
- Frontend: `npm run build`/`lint` clean, 7/7 tests passing across 4 files.
- **Full live end-to-end verified in a real browser**: logged in as
  Requestor, viewed the real request list (with correct status/role
  display), opened a request's detail/timeline view, logged in as
  CapacityManager, confirmed only the correct nav items are visible,
  approved a real request from the queue via the actual transition
  endpoint, and confirmed the status genuinely moved
  `CapacityReview` → `InfraApproval` with accurate timestamps.

Next: Phase 6 — Polish (audit log viewer, user preferences, security
hardening pass, remaining unit tests, runbook.md, Playwright suite wired
into CI). Then the spec's own Go-Live Checklist (pilot team, go-live date,
real AD mapping — all still open business decisions).
