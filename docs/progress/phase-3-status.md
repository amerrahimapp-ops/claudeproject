# Phase 3 — Workflow Engine: status

Done:
- Config-driven state machine (`IWorkflowEngine`/`WorkflowEngine`) validating
  every transition against the seeded `workflow_config` graph: allowed
  transitions, required role, terminal-state blocking.
- Minimal Requests substrate: `POST /api/v1/requests` (create draft),
  `GET /api/v1/requests/{id}` (fetch with workflow history) — full wizard
  business logic deferred to a later phase.
- `POST /api/v1/requests/{id}/transition` — maps outcomes to 200/403/404/409.
- Built TDD: 5 tests written first (red), then implementation.
- /code-review pass on the state machine (highest-risk module) found and
  fixed two real issues before merge:
  - **Authorization bypass**: stages with no `RequiredRole` (draft) were
    reachable by *any* authenticated user, not just the request's owner.
    Fixed — now requires owner or Admin whenever no specific role is set.
  - **No concurrency control**: two simultaneous transitions on the same
    request could both succeed, silently overwriting each other. Fixed with
    an EF Core optimistic-concurrency token (`Request.ConcurrencyVersion`),
    manually incremented per transition; a conflicting concurrent write now
    throws `DbUpdateConcurrencyException`, caught and surfaced as 409.
  - Added 2 more tests proving both fixes (11 total, all passing, re-run
    twice for flakiness). The concurrency test deterministically simulates
    the race via two separate DbContexts rather than real thread timing.
- Also fixed along the way (by the implementing subagent): DbInitializer
  only seeded the admin user, causing FK violations for the other 3 roles;
  cross-test-class parallelism was deadlocking against the shared dev MySQL
  (both integration test classes now run serially via an xUnit collection).

Next: Phase 4 — Integrations. See CLAUDE.md and the plan for scope. Recall
Phase 4 needs Grafana Cloud Free / Jira Cloud Free / Mailtrap credentials
from the user (or proceeds in pure-mock mode if not ready).
