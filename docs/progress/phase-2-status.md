# Phase 2 — Foundation: status

Done:
- EF Core entities + migration for all 9 tables, workflow_config seed for
  the Phase-1 7-stage flow, local dev admin user.
- .NET 9 modular-monolith scaffold (8 module folders wired via one
  registration call each), Swagger, health endpoint, Serilog.
- IIdentityProvider/MockIdentityProvider auth, JWT issuance, guarded to
  never run outside Development (throws at startup otherwise).
- React 18 + Vite + TS + Ant Design 5 shell: dark theme, routing skeleton,
  React Query, auth context stub.
- xUnit integration tests (4, against real local MySQL), Vitest unit test.
- /code-review pass found and fixed: missing auth guard on GET
  /api/v1/requests, unguarded Mock provider reachable outside dev, a
  hardcoded dev password duplicated in tracked source (now env-overridable),
  a flaky test assuming an empty shared DB, and a dark-theme text-color bug
  (inline `color: inherit` override making the sidebar logo invisible).
- Live-verified in browser preview: dark theme renders correctly, routing
  works, no console/network errors.

Environment notes (not repo state, but relevant for future sessions on this
machine): .NET 9 SDK now installed user-locally
(%LOCALAPPDATA%\dotnet — add to PATH if a fresh shell can't find `dotnet`).
Docker Desktop's "AI/Inference" feature was disabled after it caused a
crash loop from corrupted stale sockets; re-check if Docker acts up again
after a reboot.

Next: Phase 3 — Workflow Engine. See CLAUDE.md and the plan for scope.
