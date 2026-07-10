# web

Project Alpha (Capacity Request Management System) frontend — React 18 +
TypeScript + Vite, Ant Design 5, React Router 6, React Query. See the repo
root `CLAUDE.md` for overall project context.

This is the Phase 2 ("Foundation") app shell: theme, routing skeleton, and
auth context only. The real wizard/dashboard/queue pages land in Phase 5.

## Scripts

- `npm run dev` — start the Vite dev server.
- `npm run build` — type-check (`tsc -b`) and produce a production build.
- `npm test` — run the Vitest suite once.
- `npm run lint` — run ESLint.

## Structure

- `src/theme/` — Ant Design `ConfigProvider` theme (dark mode, dense/flat,
  single accent color).
- `src/context/` — `AuthProvider`/`useAuth`, a mock in-memory auth context
  (no real backend call yet).
- `src/layouts/` — `AuthenticatedLayout` (sidebar/header shell) and
  `BareLayout` (for `/login`).
- `src/pages/` — placeholder route components.
- `src/routes/` — `AppRoutes`, the React Router route table.
