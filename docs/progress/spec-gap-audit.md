# Spec vs. Build Gap Audit

Produced 2026-07-11, at the user's request, after several real gaps
surfaced through live testing that every prior phase's own "verification"
had missed. This is a direct, evidence-based diff against
`docs/superpowers/specs/2026-07-08-capacity-request-system-design.md`
(the "spec" below) — every line cites the actual file checked, not a
subagent's self-report. Baseline: `phase/6-polish` @ `8de175c`.

Legend: **Built** = matches spec and is reachable by a real user through
the UI. **Partial** = exists but incomplete, backend-only, or diagnostic-only
(not wired into the real user journey). **Missing** = not implemented at all.

---

## Section 4 — Modules

| Item (spec 4.x) | Status | Evidence |
|---|---|---|
| Requests CRUD | **Partial** | Only Create + Get-by-id exist (`api/src/Api/Modules/Requests/RequestsEndpoints.cs`). No update/delete — a Draft can't be edited before submit, and drafts can't be "resumed" in any meaningful sense since there's nothing to resume into. |
| 5-step wizard (4.1) | **Missing** | `web/src/pages/NewRequestPage.tsx` is a single-step form (Environment/ProjectType/Priority only). |
| Server entries per resource type | **Partial** | `RequestServer` entity exists and is fully modeled (api/src/Api.Data/Entities/RequestServer.cs), but `CreateRequestRequest` never accepts server data — nothing is ever persisted through the API. |
| Attachment upload | **Missing** | `Attachment` entity exists (api/src/Api.Data/Entities/Attachment.cs) but is referenced by zero endpoints anywhere in `api/src/Api/Modules/`. No upload UI either. |
| Workflow Engine (4.2) | **Built** | Config-driven state machine, `workflow_config` table, role-gated transitions, audit log per transition. `api/src/Api/Modules/Workflow/WorkflowEngine.cs`. Solid. |
| Grafana client (4.3) | **Partial** | Real + Mock implementations exist and were live-verified against Grafana Cloud (Phase 4), but only reachable via the Admin diagnostic button (`POST /api/v1/admin/test-grafana`) — never called automatically as part of a request's AI evaluation. |
| AI client (4.3) | **Partial** | Same pattern — real Ollama + Mock implementations exist and work (`api/src/Api/Modules/Ai/`), but `WorkflowEngine.cs` never calls `IAiEvaluationClient`. It only fires from the Admin diagnostic button (`POST /api/v1/admin/test-ai-evaluation`). The `ai_evaluation → ai_reviewed` transition (spec 7.3, "Auto | System") does not happen — anywhere. |
| Email client (4.3) | **Partial** | MailKit SMTP + Mock exist, live-verified against Mailtrap (Phase 4). But "triggered by every workflow state transition" (spec 4.3, 4.4, 10.5) never happens — `WorkflowEngine.cs` has zero `IOutboxWriter` calls. Email only sends via the Admin diagnostic button. |
| Jira/HPSM client | **Missing (by design)** | Correctly deferred to Phase 2+ per spec 4.3/10.2 — not a gap. |
| Notifications Module (4.4) | **Missing** | `api/src/Api/Modules/Notifications/NotificationsServiceCollectionExtensions.cs` is empty scaffolding, unchanged since Foundation phase. No transition ever notifies anyone. No per-event-type on/off preference exists (`UserPreference.NotificationPrefs` column exists in the DB but no endpoint reads/writes it — see Section 5). |
| AI Module adapter pattern (4.5) | **Built** (as a component) / **Partial** (as a feature) | The adapter pattern itself is correctly implemented (interface + Mock + Ollama, ADR 0002). But per spec 4.5 ("Phase 1: mock AI returns structured response... real AI integration added when model is ready") the intent was for this to run as part of the request flow — it doesn't. |
| Reports Module (4.6) | **Partial** | 3-sheet workbook generated (`api/src/Api/Modules/Reports/ClosedXmlReportGenerator.cs`), Sheet 1 (Request Summary) and Sheet 3 (Approval Chain) are real. Sheet 2 (AI Evaluation Report) is a hardcoded placeholder — literally `sheet.Cell(2,1).Value = "No AI evaluation data available"` with a `// TODO` comment, because nothing populates `ai_evaluations` during real usage (see AI client above). |
| Admin Module (4.7) | **Partial** | Audit log viewer: **Built** (Phase 6a, `api/src/Api/Modules/Admin/AuditLogEndpoints.cs`, live-verified). User management (activate/deactivate/role assignment): **Missing**. Workflow config management: **Missing**. Reference data management: **Missing**. `web/src/pages/AdminPage.tsx` says as much directly to the user: *"User management and workflow configuration are not built yet."* |

## Section 5 — Data Model

| Table/field (spec 5.1) | Status | Evidence |
|---|---|---|
| `requests.title` | **Missing** | Not a column on `Request` entity. |
| `requests.department` | **Missing** | Not on `Request` or `User`. |
| `requests.project_name/code` | **Missing** | Not on `Request` entity as of `8de175c` (a stopped, unmerged attempt to add these exists on branch `wip/phase-7a-partial-stopped`, unverified). |
| `requests.planned_start/end_date` | **Missing** | Same as above. |
| `requests.description` | **Missing** | Same as above. |
| `requests.current/requested_capacity`, `uplift_percentages` | **Partial** | Columns exist (`Request.CurrentCapacity/RequestedCapacity/UpliftPercentages`, JSON strings) but nothing ever writes to them — `CreateRequestRequest` doesn't accept this data. |
| `request_servers.ip_address` | **Missing** | Not on `RequestServer` entity. |
| `request_servers.os` | **Missing** | Only a coarse `Platform` enum (Unix/Wintel) exists — no free-text OS field. |
| `request_servers.is_physical` | **Missing** | Not on the entity. |
| `request_servers` (rest) | **Built** | Hostname/ResourceType/CurrentValue/RequestedValue/MountPoint/DrApplicable/AppTier all present — just entirely unpopulated (see above, nothing writes to this table). |
| `justifications.attachment_paths` | **Missing** | Not on `Justification` entity. Otherwise matches spec. |
| `workflow_stages.assigned_user_id` | **Missing** | Only `AssignedRole` (string) exists — no nullable FK to a specific assigned user. |
| `users.pf_number` | **Built** | Present (`User.PfNumber`, nullable), unused by any UI yet. |
| `users.contact`, `users.department` | **Missing** | Not on `User` entity. |
| `users.is_active` | **Missing** | No deactivation concept anywhere — matches the missing Admin user-management feature above. |
| `attachments` table | **Built** (schema only) | Entity exists, zero endpoints use it (see Section 4). |
| `attachments.stage_id` | **Missing** | Entity only has `RequestId`, no nullable `StageId`. |
| `audit_log` | **Built** | Used correctly for every workflow transition. Not used for other entity writes (user/workflow_config changes) — but those features don't exist yet either, so nothing to log. |
| `user_preferences.notifications_email` | **Partial** | Column exists (`NotificationPrefs`, JSON), defaulted to `"{}"` on first write, never read or exposed via any endpoint. |
| `user_preferences.theme` | **Partial** | Column exists (`Theme`, enum), always hardcoded to `Dark` on creation (`MeEndpoints.cs:77`), never exposed as a user-facing toggle — see Section 8 (no theme toggle in the UI at all, `web/src/theme/theme.ts` is a single fixed dark theme). |
| `user_preferences.default_view` | **Built** | Phase 6a, live-verified. |
| `workflow_config` | **Built** | Matches spec exactly, seeded in `DbInitializer.cs`, drives the real state machine. |

## Section 6 — RBAC

| Rule (spec 6.x) | Status | Evidence |
|---|---|---|
| Server-side enforcement only | **Built** | Every endpoint has `.RequireAuthorization()`, workflow transitions independently re-check role/ownership in `WorkflowEngine.cs` regardless of what the frontend hides. |
| Row-level security (Requestor sees own only) | **Fixed today** | Was **Missing** until this session — `GET /api/v1/requests` returned every request to every role with zero filtering (`Program.cs`, prior to commit `8de175c`). Now filtered by `user.IsInRole("Requestor")`. |
| Action-level security | **Built** | Confirmed via `RequireRole`/ownership checks across Workflow/Admin/Ai/Grafana/Email endpoints. |
| Least privilege | **Fixed today** | The Admin nav link and `/admin` route had zero role restriction (any logged-in user could open it) until this session's fix. |
| Queue position (6.3, "You are #3 waiting...") | **Missing** | No such feature anywhere in `web/src/pages/DashboardPage.tsx` or elsewhere. |

## Section 7 — Workflow State Machine

| Item | Status | Evidence |
|---|---|---|
| Config-driven graph, Phase-1 stage set | **Built** | `workflow_config` seeded correctly for all Phase-1 stages (`DbInitializer.cs`). |
| `draft → submitted` (Requestor submits) | **Missing (until now unreachable), fix in progress** | The transition endpoint works correctly when called directly, but **no UI button exists anywhere** to call it — confirmed by the Phase 6b subagent having to fake this via direct API calls in its own Playwright test. A Requestor creating a request today has literally no way to move it forward. (Phase 7a was mid-flight building this when paused for this audit — see wip branch.) |
| `submitted → ai_evaluation → ai_reviewed` (Auto, System) | **Missing** | Never happens automatically — see Section 4, AI client. |
| `ai_reviewed → submitted` / `ai_reviewed → capacity_review` (Requestor) | **Missing** | No UI for this either, and moot today since a request can never actually reach `ai_reviewed`. |
| `capacity_review → {capacity_approved, submitted, rejected, deferred}` | **Built** | Reachable and working via `web/src/pages/CapacityQueuePage.tsx` / `ApprovalQueueTable.tsx` — but only reachable today by manually PATCHing a request's status outside the UI first, since nothing upstream of it works yet. |
| `infra_approval → {approved, rejected, deferred}` | **Built** | Same caveat as above. |
| `approved → done` (Auto, Excel generated) | **Missing** | Confirmed no automatic trigger — Excel is only ever generated on-demand via `GET /api/v1/requests/{id}/report`, callable at any time regardless of status, not automatically at `done`. |

## Section 8 — UI/UX

| Item (spec 8.x) | Status | Evidence |
|---|---|---|
| Dark mode | **Built (partially — no toggle)** | `web/src/theme/theme.ts` hardcodes `theme.darkAlgorithm`. Spec 8.2 wants a toggle with a separate light token set — doesn't exist. |
| Login | **Built** | Real JWT login, `web/src/pages/LoginPage.tsx`. (Spec says "AD SSO redirect" — Phase 1 correctly mocks this per the interface+mock pattern; not a gap.) |
| Dashboard (User) — "queue position, summary cards" | **Partial** | Table of requests exists; no queue position, no summary cards. |
| Dashboard (Team) — "AI flags, utilization alerts, workload" | **Missing** | The two approval-queue pages are plain filtered tables (`ApprovalQueueTable.tsx`) — no AI flags column (nothing to show, AI never runs), no utilization alerts, no workload view. |
| New Request Wizard | **Missing** | Single-step form, see Section 4. |
| Request Detail — "AI panel, review activity" | **Partial** | Workflow timeline and basic info are real (`web/src/pages/RequestDetailPage.tsx`); there is no AI Insights panel at all (confirmed — zero matches for AI/Grafana content in that file). |
| Review Queue / Approval Queue | **Built** | Real, role-gated, working. |
| AI Insights Panel (8.3, dedicated row) | **Missing** | Entirely absent. |
| Reports page | **Built** | Real, working, live-verified. |
| User Profile ("edit name/contact, notification prefs, theme toggle") | **Missing** | No such page exists. The only preference control is the "default landing page" dropdown living in the header. |
| Admin Panel | **Partial** | See Section 4 — only the audit log viewer is real. |
| Request Wizard Flow (8.4, 5 steps) | **Missing** | See Section 4. |

## Section 9 — Security Guardrails

| Control | Status | Evidence |
|---|---|---|
| JWT short expiry | **Built** | 15-min access token, `JwtTokenService.cs`. |
| JWT refresh token (8-hr) | **Missing** | No refresh token issuance or endpoint anywhere — only the single 15-min access token. A session simply expires and forces re-login. |
| Session invalidation / server-side blacklist on logout | **Missing** | Logout is client-side only (clears `sessionStorage`); no server-side JWT blacklist. Low-risk for a 15-min-lived token, but spec explicitly calls for it. |
| Rate limiting | **Partial** | Only applied to `/api/v1/auth/login` (Phase 6b, 20 req/min). Spec 9 wants general per-user throttling (e.g. max 10 submissions/min) — not implemented elsewhere. |
| CSRF protection | **N/A / not applicable as specified** | This is a bearer-JWT API with no cookies, so classic CSRF doesn't apply the way the spec assumes (which reads as written for a cookie-session model) — worth a note back to spec authors rather than a real gap. |
| Input validation, SQL injection protection, encryption in transit (dev), audit logging | **Built** | EF Core parameterized queries throughout, server-side validation on create/transition endpoints, audit log on every transition. |
| PII minimization, attachment access control | **N/A** | No attachments feature exists yet to secure (see Section 4). |

## Section 10 — Integrations

Matches Section 4's findings: all four real integrations (Grafana/AI/Email + Excel) are genuinely built and were live-verified against real external accounts in Phase 4 — but three of the four (Grafana/AI/Email) are **diagnostic-only**, reachable solely through Admin test buttons, not through the actual request workflow they're supposed to serve. This is the single biggest thread running through this whole audit: real, working integration code that the workflow engine never calls.

## Section 11 — Infrastructure / Section 12 — Dev Environment

Out of scope for a code audit (these describe production RHEL/Windows deployment topology). Dev environment setup (MySQL/Mailtrap/Grafana Cloud/Ollama) matches spec 12 and is documented in `docs/runbook.md` (Phase 6b). One real, practical gap: there is no persistent local deployment — the API/web/DB must be manually started every session (`docker compose up -d`, `dotnet run`, `npm run dev`), which is very likely the direct cause of the "failed to load/create request" reports earlier in this conversation.

## Section 13 — Configuration Strategy

**Built.** Provider-switch pattern (`Mock`/real per integration) matches spec 13.1 exactly across Email/Grafana/AI/Auth.

## Section 14 — Testing Strategy

| Layer | Status | Evidence |
|---|---|---|
| Workflow Engine unit tests | **Built** | `api/tests/Api.Tests/WorkflowEngineTests.cs`. |
| API integration tests (real DB) | **Built** | Consistent pattern across all endpoint test files, real MySQL via docker-compose/CI service container. |
| Excel generator snapshot test | **Built** | `api/tests/Api.Tests/ReportEndpointTests.cs`. |
| AI adapter unit test (mocked client) | **Built** | `api/tests/Api.Tests/AiEndpointTests.cs`. |
| Frontend manual QA | **Built** | Ad hoc this session; a real Playwright suite now also exists (Phase 6b) covering RBAC nav matrix + one happy path — exceeds spec's stated minimum ("Manual QA" only). |
| Security manual RBAC testing | **Partial** | Some done this session (found the row-level and Admin-nav gaps); no systematic pass previously. |

This section is the one area that's in genuinely good shape.

## Section 15 — Error Handling, Section 18 — Idle Timeout

| Item | Status | Evidence |
|---|---|---|
| Grafana/AI unreachable → queued retry / "Skip AI" button | **Missing** | Moot — nothing calls these automatically yet (Section 4), so there's no failure path to handle. |
| Validation error → frontend scrolls to field | **Partial** | AntD's default form validation shows inline errors; no explicit "scroll to field" behavior verified. |
| Generic 500 → no stack traces exposed | **Built** | Standard ASP.NET Core behavior in non-Development environments; Development shows full errors as expected for local dev. |
| Session expired → 401 → redirect to login | **Built** | Confirmed via `api/src/Api/client.ts`'s `apiFetch` + `AuthenticatedLayout`'s auth check (fixed this session). |
| 15-min idle timeout + 13-min warning + auto-save draft | **Missing** | Nothing implements client-side idle detection at all. |

## Section 19 — Definition of Done (Phase 1)

Direct read against 19.1's checklist:

| Criterion | Status |
|---|---|
| Feature complete (submit → AI eval → review → approve → Excel) | **Not met** — the full chain has never run end-to-end through the UI; only fragments work in isolation, several manually via direct API calls. |
| Auth works | **Met** — login/JWT/RBAC all function correctly for what exists. |
| Integrations work | **Partial** — all three work when manually triggered via Admin; none work as part of the real flow. |
| Excel output | **Partial** — structure matches (3 sheets) but Sheet 2 is a placeholder and generation isn't automatic. |
| Data persisted | **Met** — everything that is captured is correctly persisted and queryable. |
| User accepted (1 real E2E request by non-developer) | **Not met** — can't happen yet; the flow isn't complete enough for a non-developer to submit and see it through. |
| No critical bugs | **Not met, until this session** — row-level security bypass and unrestricted Admin route were both real, now fixed. |
| Docs exist | **Met** — spec, CLAUDE.md, ADRs, `docs/runbook.md` all exist. |
| Pilot ready | **Not met** — no persistent deployment exists yet (dev-only, manually started). |
| Rollback known | **Met** (trivially — nothing has replaced the old Excel process yet). |

---

## Summary: the one real thread

Nearly every "Partial" and "Missing" item above traces back to a single root cause: **the workflow engine only knows how to move a request through stages that a human explicitly requests via the API — it never calls any of the three System-triggered automations the spec requires** (AI evaluation, email notifications on transition, Excel generation on completion), and **the UI never gave a Requestor any way to advance a request out of Draft in the first place.** Every integration that "doesn't work" actually does work in isolation (Grafana/Email/AI were all live-verified against real external services in Phase 4) — they're just never invoked by the one piece of code that was supposed to call them automatically.

This is exactly what Phase 7 (a/b/c, currently paused) was scoped to fix. This audit confirms that scope is correct and, if anything, slightly larger than originally understood — the data model needs more fields than Phase 7a's stopped attempt had identified (title, department, is_physical, os, assigned_user_id, attachment_paths), and Section 8's User Profile page / theme toggle / notification preferences are additional real gaps not previously scoped into Phase 7 at all.
