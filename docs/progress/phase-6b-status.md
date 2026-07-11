# Phase 6b — Security hardening + Playwright suite + runbook: status (complete)

One of two parallel Phase 6 (Polish) workstreams (6a: admin audit-log viewer
+ user preferences — separate, not touched here).

## 1. Security hardening pass

Reviewed JWT config, RBAC enforcement, brute-force protection, CORS,
secrets-in-logs, and frontend role-gating. Two real issues found and fixed;
everything else reviewed and confirmed already sound.

**Fixed:**
- **JWT signing key had no minimum-strength check.** `AuthServiceCollectionExtensions.AddAuthModule`
  now fails fast at startup if `Jwt:SigningKey` is under 32 bytes (256 bits,
  the RFC 7518 floor for HS256) — previously a short/weak key would be
  accepted silently. The CI-only test key (60 chars) and the key generated
  for local dev both clear this easily; only a genuinely weak key would
  trip it.
- **No brute-force protection on `POST /api/v1/auth/login`.** Added a
  `Microsoft.AspNetCore.RateLimiting` fixed-window policy (20
  requests/minute, partitioned by client IP, `RequireRateLimiting("login")`
  on the endpoint) — in-memory, single-instance, matching the "simple, not
  distributed" scope this was asked for. Verified it doesn't interfere with
  the existing 23 integration tests or the new Playwright suite in a single
  clean run; it *does* trip if you manually re-run the Playwright suite
  several times in a row against the same long-lived `dotnet run` process
  without restarting it (expected — the limiter is per-process and resets
  on restart, noted as an explicit tradeoff in the code comment and in
  `docs/runbook.md`'s production-configuration checklist for anyone running
  more than one API instance).
- **Frontend had no session boundary on most authenticated routes** — found
  while writing the login/session-boundary Playwright test, not by manual
  review. `RequireRole.tsx` (used only by the two approval-queue routes)
  had its own `isAuthenticated` redirect, but `AuthenticatedLayout.tsx`
  (the shell wrapping *all* authenticated routes — Dashboard, New Request,
  Request Detail, Reports, Admin) had none. An unauthenticated visitor
  hitting `/dashboard` directly saw the full dashboard chrome with failed
  401 API calls instead of being redirected to `/login`. Not a backend
  security hole (every API endpoint already requires a valid JWT via
  `RequireAuthorization()`), but a real UX/session-boundary gap. Fixed by
  adding the same `isAuthenticated` check to `AuthenticatedLayout` itself,
  covering every nested route in one place. Updated `App.test.tsx`, which
  had encoded the old (buggy) behavior as its expected result.

**Reviewed, confirmed already sound, no change made:**
- `WorkflowEngine.TransitionAsync`'s owner-or-Admin fix (from Phase 3/5) is
  still correct — re-read it end to end, no new issue found.
- CORS (`Program.cs`) is genuinely dev-only: the policy is only ever added
  to the request pipeline inside `if (app.Environment.IsDevelopment())`.
  Registering the policy itself is unconditional but inert unless applied.
- No secrets/tokens/request bodies/auth headers are logged anywhere
  (grepped every `ILogger`/Serilog call site across all modules — Email,
  Grafana, AI, Outbox). The one log line that includes a response body
  (`OllamaAiEvaluationClient`'s warning on a malformed Ollama response)
  logs model output, not credentials.
- `RequireRole.tsx` already documents itself clearly as UX-only, backend is
  the real boundary — verified this is still true and accurate.

**Noted, not fixed (explicitly out of scope for this pass):**
- The frontend has no "Submit" button to move a request out of `Draft` —
  found while writing the happy-path E2E test (see below). This is a
  product/feature gap (a missing "Submit" action on `RequestDetailPage` or
  `NewRequestPage`), not a security issue — the backend workflow engine
  already supports it fully. Flagging for a future phase rather than
  building it here, since it's outside "security hardening + E2E + runbook."

## 2. Real Playwright suite, wired into CI

Added `web/playwright.config.ts` and `web/tests/e2e/` with 2 spec files:

- **`auth.spec.ts`** — unauthenticated redirect-to-login, plus a genuine
  4-role nav-visibility matrix (Admin/Requestor/CapacityManager/InfraHead
  each checked against every nav item, not a single spot-check), plus two
  direct-URL-access denial checks for the cross-role queue routes.
- **`happy-path.spec.ts`** — Requestor creates a real request through the
  UI, downloads its Excel report (network-level assertion only, per the
  plan — no xlsx parsing), then CapacityManager approves it for real from
  the Capacity Review queue UI, with the transition endpoint result
  confirmed via the request detail page afterward.

The intermediate system-owned workflow stages (`submitted` -> `ai_evaluation`
-> `ai_reviewed` -> `capacity_review`) are fast-forwarded via direct API
calls as the requesting user in the happy-path spec, since there's no UI
button for them yet (see the product gap noted above) — every one of those
transitions is legitimately performable by the request's own owner per
`WorkflowEngine`, so this is exactly what any real caller would have to do
too, and the actually security-relevant step (CapacityManager-gated
approval) is driven for real through the browser.

**CI (`\.github/workflows/ci.yml`, `playwright` job):** previously checked
out `web/` only and ran `npx playwright test` against nothing — no backend,
no database, gated by a step-level `hashFiles()` check that cleanly skipped
until this config existed. Now: added its own MySQL service container
(mirroring the `dotnet` job's), `actions/setup-dotnet@v4`, and a background
`dotnet run` of the real API (same CI-only test signing key /
connection-string pattern as the `dotnet` job) with a polling health-check
step before the suite runs. Playwright's own `webServer` block in
`playwright.config.ts` starts the web dev server (`npm run dev`) — simpler
than a separate build+preview step and already proven to work in local
verification. On failure, the job dumps the API log and uploads the
Playwright HTML report as an artifact.

**actionlint: clean (exit 0)** — `docker run --rm -v "<repo>:/repo" -w /repo rhysd/actionlint:latest -color`.

## 3. `docs/runbook.md`

Written, covering: docker-compose MySQL + `dotnet ef database update` +
`dotnet run`/publish for the API + `npm run build` + static hosting for the
web app (deploy); `/health` response shapes and what to do about an
unhealthy one; `mysqldump`-based backup/restore; and rollback (both the
deploy artifact/git commit and `dotnet ef database update <migration-name>`
for the schema, including the current migration list and a worked example).
Notes explicitly that there's no production hosting target decided yet and
what's still missing before a real deploy (real `AdIdentityProvider`, a
non-localhost web API base URL, secrets-manager integration).

## Verification performed

- `dotnet build` (Release): clean, 0 warnings, 0 errors.
- `dotnet test`: **23/23 passing**, run against a real local MySQL
  (docker-compose), not mocked — confirms the rate limiter and signing-key
  check don't break the existing integration test suite.
- `npm run build`, `npm run lint`, `npm test`: all clean (7/7 unit tests
  passing across 4 files).
- **Playwright suite actually run locally against the real dev stack**
  (docker-compose MySQL + `dotnet run` API + Playwright's own `npm run dev`
  webServer) — all 8 tests passing, confirmed on a freshly-restarted API
  process (not just a lucky first run). Found and fixed three real issues
  in the process (the two security-relevant frontend/pagination-locator
  issues above, plus a Vitest/Playwright test-file collision — both
  runners' default globs matched `tests/e2e/*.spec.ts`, fixed by excluding
  `tests/e2e/**` from Vitest's `test.exclude`).
- actionlint: exit 0 on the modified `ci.yml`.
- **Found (not fixed, out of scope):** `dotnet test` run against the
  long-lived local `capacity_dev` database (rather than a fresh one)
  intermittently failed one test —
  `OutboxTests.EnqueueEmail_IsPersistedAsPending_ThenDeliveredByProcessor`
  — after this session's unusually heavy repeated manual testing against
  that same shared DB (many `dotnet run`/Playwright passes in a row without
  restarting). The DB row was correctly marked `Sent`; only the test's own
  `MockEmailClient.SentCount` assertion failed, which looks like a stale
  `OutboxMessage` row (stuck in `Processing`, presumably from an earlier
  interrupted run in this same session) confusing the test's "latest row =
  ours" assumption. Confirmed 3 clean 23/23 runs in a row against a fresh
  throwaway database (mirroring what CI's `dotnet` job actually does — its
  own ephemeral `capacity_test` service container, never a reused one) —
  so this is very unlikely to affect CI, but it's a real local-dev-loop
  papercut worth a closer look separately (Outbox module, untouched by this
  workstream).

## Pending business decisions (re-surfacing per CLAUDE.md)

Unresolved, not blocking the build, but gating real rollout:
- Pilot team identification.
- Go-live date.
- Real AD group -> role mapping (blocks building `AdIdentityProvider`,
  which in turn blocks any non-Development deploy per the fail-fast check
  in `AuthServiceCollectionExtensions`).

Next: Phase 6a (parallel workstream: admin audit-log viewer, user
preferences) merges separately. After both land, the spec's own Go-Live
Checklist is the remaining gate.
