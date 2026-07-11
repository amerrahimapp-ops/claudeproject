# Phase 6a — Admin audit-log viewer + minimal user preferences: status (complete)

One of two parallel Phase 6 (Polish) workstreams. Scope: an Admin-only
audit-log viewer and a single "default landing page after login" user
preference, backend + frontend.

## What's done

### Audit log viewer (backend)
- `GET /api/v1/admin/audit-log` (`api/src/Api/Modules/Admin/AuditLogEndpoints.cs`),
  wired into the Admin module as `app.MapAdminEndpoints()` in `Program.cs`
  (same slot as the other module `MapXxxEndpoints()` calls).
- Paginated (`page`/`pageSize`, default page size 25, capped at 200),
  ordered by `PerformedAt` descending, optional filters: `entityType`,
  `entityId`, `action`, `performedByUserId`.
- Joins to `User` for `PerformedByUserName` in the projection (no raw
  FK-only `PerformedByUserId` leaked when a display name is available for
  free in the same query).
- Admin-only via `.RequireAuthorization(policy => policy.RequireRole("Admin"))`,
  same idiom as `EmailEndpoints`/`GrafanaEndpoints`/`AiEndpoints`.
- **Existing-gap check, per the task brief**: grepped the workflow engine
  and requests module before building this. Turns out something *does*
  already write to `AuditLogs` — `WorkflowEngine.TransitionAsync` logs one
  row (`EntityType: "Request"`, `Action: "WorkflowTransition"`) per stage
  transition. That's the *only* writer today; no other action (user
  creation, workflow_config changes, request field edits outside the
  workflow transition) is audited, because none of those have mutable
  endpoints yet either. Confirmed live against the real dev DB: 470+ real
  rows already present from workflow activity by the time this was tested.
- Integration tests: `api/tests/Api.Tests/AdminAuditLogEndpointTests.cs`
  (admin/non-admin/unauthenticated cases, pagination, entityType+entityId+action
  filtering, newest-first ordering, display-name join) — seeds its own rows
  under a per-run-unique `EntityType` (`TestEntity-{guid}`) to stay
  independent of whatever the shared dev MySQL already has from workflow
  activity, following `EmailEndpointTests.cs`'s pattern.

### User preferences (backend + frontend)
- `UserPreference` entity/table (`DefaultView`, `NotificationPrefs`,
  `Theme`) and its EF configuration in `CapacityDbContext` **already
  existed** from an earlier phase, already covered by the `InitialCreate`
  migration — no new migration was needed. Only `DefaultView` is exposed
  here; `NotificationPrefs`/`Theme` are out of scope for this task.
- `GET`/`PUT /api/v1/me/preferences` (`api/src/Api/Modules/Auth/MeEndpoints.cs`),
  any authenticated user, own record only (`.RequireAuthorization()`, no
  role gate), resolved via the `user_id` JWT claim — same pattern as
  `RequestsEndpoints`/`WorkflowEndpoints`. Allowed values: `Dashboard`,
  `NewRequest`, `ApprovalQueue` (400 on anything else). `GET` before any
  `PUT` defaults to `Dashboard` without creating a row; `PUT` creates the
  row on first write.
- Integration tests: `api/tests/Api.Tests/MePreferencesEndpointTests.cs`
  (unauthenticated, default value, PUT-then-GET round trip, invalid value
  → 400).
- **Frontend**: `web/src/api/preferences.ts` (typed fetch wrapper +
  `resolveDefaultViewRoute`, which maps `ApprovalQueue` to the caller's
  actual queue route by role — Capacity Manager vs. Infra Head vs.
  fallback to Dashboard — since the backend doesn't know the frontend's own
  route table). A small `Select` was added to `AuthenticatedLayout`'s
  header, next to the user name/role `Tag`, that reads/writes the
  preference immediately on change (React Query + a mutation). `LoginPage`
  now fetches the preference right after a successful login and navigates
  to the resolved route instead of always going to `/dashboard` (falls back
  to `/dashboard` silently if the preference fetch fails, so a transient
  hiccup never blocks a successful sign-in). `AuthProvider.login` now
  returns the logged-in `AuthUser` so `LoginPage` doesn't have to wait on a
  re-render to read the fresh role.

### Audit log viewer (frontend)
- `web/src/api/admin.ts` (typed fetch wrapper, `fetchAuditLog`).
- `AdminPage.tsx` restructured into an AntD `Tabs`: "Audit Log" (new) and
  "Integration Health Check" (the existing panel, unchanged behavior).
  Audit Log tab: `Table` with server-side pagination (`showSizeChanger`,
  25/50/100), `entityType`/`action` text filters with Filter/Clear buttons,
  and an expandable row showing the raw `oldValues`/`newValues` JSON —
  matches the `ReportsPage.tsx`/`ApprovalQueueTable.tsx` AntD `Table`
  conventions already in the repo (size="small", `locale.emptyText` for
  empty/error states, React Query for data fetching).

## Real bugs / gaps found

- **No other writer to `AuditLogs` besides `WorkflowEngine.TransitionAsync`** —
  confirmed via grep before building (see above). Not a bug introduced by
  this task, but a real, pre-existing gap: user-management and
  workflow_config admin actions have no audit trail because those mutation
  endpoints don't exist yet (Admin module scope is still limited to the
  audit-log viewer and the three integration diagnostic endpoints). Worth
  keeping in mind for whichever phase adds those admin mutation endpoints —
  they should write to `AuditLogs` too.
- **`OutboxTests.EnqueueEmail_IsPersistedAsPending_ThenDeliveredByProcessor`
  is flaky** (`Assert.True(emailClient.SentCount > 0)` — occasionally the
  outbox message reaches `Sent` status but the singleton `MockEmailClient`
  instance the test asserts against afterward shows `SentCount == 0`; it
  passed on some runs and failed on others in this session, both isolated
  and as part of the full suite). Confirmed via `git stash` that the same
  intermittent failure reproduces against the unmodified `phase/6-polish`
  base — **pre-existing, unrelated to this task's changes** (Outbox/Email
  module, not touched here). Not fixed as part of this task since it's out
  of scope for 6a; flagging here per the "note real gaps" convention rather
  than silently working around it.
- **Process note, not a code bug**: initial edits were accidentally made
  in the main repo checkout instead of this isolated worktree
  (`.claude/worktrees/agent-af84457ff8b2c800f`). Caught before committing —
  all changes were copied into the correct worktree, the main checkout was
  restored to clean via `git checkout --`/`rm`, and `git status` was
  re-verified in both locations before proceeding.

## Verification performed

- **Backend**: `dotnet build` clean (0 warnings/errors). `dotnet test`
  (real local MySQL via `docker compose up -d mysql`): 33/33 passing on
  the final run; across the session the only intermittent failure was the
  pre-existing `OutboxTests` flake above (unrelated to this task, confirmed
  via `git stash`). All 10 new tests (5 audit-log + 5 preferences) pass
  individually and as part of the full suite, every time.
- **Frontend**: `npm run build`, `npm run lint`, `npm test` all clean (4
  test files / 7 tests passing, no new failures).
- **Full live end-to-end verified in a real browser** against the real dev
  API + MySQL (a second API instance on a scratch port, since the parallel
  6b workstream's own dev API already owned :5000 in this session; the
  frontend's `API_BASE_URL` was pointed at it only transiently for this
  verification pass and reverted to `:5000` before committing):
  - Logged in as `admin` (confirmed via `DbInitializer.cs` that the seeded
    Admin username is `admin`, not `admin.dev` as originally assumed —
    the other three dev roles do use the `.dev` suffix).
  - Audit Log tab rendered real data (470+ rows from live workflow
    activity plus the two rows seeded by the test), newest first.
  - Filtered by `entityType=Request`, confirmed non-`Request` rows dropped
    from the result set.
  - Paginated to page 2, confirmed the next 25 rows loaded and the filter
    stayed applied.
  - Expanded a row, confirmed the old/new JSON values render correctly.
  - Set the header preference dropdown to "New Request", confirmed the
    `PUT` succeeded (200) over the network tab.
  - Logged out and back in as `admin`: landed directly on `/requests/new`,
    confirming the preference persisted server-side and the login redirect
    resolves it correctly.
  - Reset the preference back to "Dashboard" and confirmed the `GET`
    reflected it, leaving the dev DB in its default state.

## Next

Phase 6b (the parallel workstream) covers the rest of Polish. Remaining
Phase 6 items per `CLAUDE.md`: security hardening pass, `docs/runbook.md`,
Playwright suite in CI, and re-surfacing the still-open business decisions
(pilot team, go-live date, real AD group mapping) at the end of Phase 6.
