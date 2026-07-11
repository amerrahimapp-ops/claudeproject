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
  fixed two real issues before merge: an authorization bypass (any
  authenticated user, not just the request owner, could advance a
  no-required-role stage) and missing concurrency control (two simultaneous
  transitions could silently overwrite each other). Both fixed, both
  covered by new deterministic tests. 11/11 tests passing.
- Manually verified live end-to-end via the real API: created a request,
  walked it through every stage (draft -> submitted -> ai_evaluation ->
  ai_reviewed -> capacity_review -> infra_approval -> done) with the correct
  role at each gated transition, confirmed full audit/stage history.

## CI: root-caused and fixed

CI had been broken since Phase 1 with a misleading "workflow file issue" /
0-jobs failure on every run, which looked exactly like a GitHub
account-level restriction (queued runners, pull_request events never
registering, not even retryable) and cost significant time chasing that
theory (default_branch pointer, a suspected interfering Codex connector,
a push-trigger workaround). **The actual cause**: `hashFiles()` was used in
job-level `if:` conditions (added in Phase 1 to skip the dotnet/web jobs
before those folders existed) — that context is evaluated before any
checkout step runs, so GitHub's parser rejects it outright as an
"Unrecognized function," failing the whole workflow file at parse time.
Generic YAML parsers (PyYAML) don't catch this since it's syntactically
valid YAML, just semantically invalid per GitHub's own schema — confirmed
clean now via `actionlint` (the real GitHub Actions linter, run via Docker,
no install needed).

Fixed: removed the job-level `hashFiles()` guards (moot anyway since
api/web have existed since Phase 2), reverted the push-trigger workaround
back to `pull_request` (that was never the real problem), fixed a CI
connection-string env var name mismatch (`ConnectionStrings__CapacityDb`,
not `__Default`), added a CI-only JWT test signing key, and made the
`playwright` job skip cleanly via a **step-level** `if: hashFiles(...)`
(valid, unlike job-level) until Phase 5/6 adds real Playwright config.

**All 4 CI jobs now pass**: secrets-scan, dotnet (11/11 tests), web,
playwright (skipped as designed). First fully green run in this project's
history. `actionlint` is now a documented CLAUDE.md convention for every
future workflow-file change.

Next: Phase 4 — Integrations. See CLAUDE.md and the plan for scope. Recall
Phase 4 needs Grafana Cloud Free / Jira Cloud Free / Mailtrap credentials
from the user (or proceeds in pure-mock mode if not ready).
