# Phase 7b Status — Auto AI-Evaluation Chain, AI Insights Endpoint, Excel/Attachment Fixes

Built directly on `phase/6-polish` (no isolated worktree — see the task's
note on the worktree-isolation tooling bug; confirmed the working tree was
clean at `3ff69ad` before starting). Builds directly on Phase 7a's expanded
data model and Submit/Confirm actions.

Source of truth for what was missing: `docs/progress/spec-gap-audit.md`
("the one real thread" — Grafana/AI/Email integrations work in isolation
but `WorkflowEngine.cs` never calls them). This task closes the AI/Grafana
half of that gap plus the Excel/Attachment side effects; email-on-transition
remains out of scope (not mentioned in this task's brief).

## 1. Orchestration design — where the auto-chain lives

**New `IWorkflowAutomationService` / `WorkflowAutomationService`**
(`api/src/Api/Modules/Workflow/WorkflowAutomationService.cs`), called once
from the tail of `WorkflowEngine.TransitionAsync` right after that
transition's own `SaveChangesAsync` commits:

```csharp
var result = WorkflowTransitionResult.Success(request);
return await _automation.RunPostTransitionHooksAsync(
    this, request, targetStage, actingUserId, actingUserRole, result);
```

Rationale: `WorkflowEngine.cs` was already a long, carefully-commented,
load-bearing state machine (validation, stage bookkeeping, concurrency
handling, audit log). Bolting three different integrations' worth of
branching logic directly into `TransitionAsync` would have buried the state
machine under integration-specific noise. A single delegated call keeps
`TransitionAsync` exactly as readable as it was in Phase 3, with one net-new
line at the very end.

**The circular-DI problem and how it's avoided**: the automation service
needs to trigger *further* transitions (submitted → ai_evaluation →
ai_reviewed is two hops), which means it needs an `IWorkflowEngine`. But
`WorkflowEngine` also needs the automation service. Constructor-injecting
`IWorkflowEngine` into `WorkflowAutomationService` would create a
container-unresolvable cycle. Fix: `WorkflowEngine` passes **itself**
(`this`, typed as `IWorkflowEngine`) as a plain method argument to
`RunPostTransitionHooksAsync` instead of the automation service resolving
it via DI. No cycle, no `IServiceProvider`-based lazy-resolution workaround
needed.

**Dispatch, per spec 7.3**:
- `targetStage == "submitted"` → immediately recurse into
  `TransitionAsync(..., "ai_evaluation", ...)` (reusing the same
  `actingUserId`/`actingUserRole` from the original submit call, which is
  always either the request's own owner or an Admin — both satisfy the
  `ai_evaluation`/`ai_reviewed` stages' "no RequiredRole ⇒ owner-or-Admin"
  check with no special-casing needed).
- `targetStage == "ai_evaluation"` → calls the new
  `IRequestAiEvaluationService.EvaluateAndPersistAsync(request)`
  (`api/src/Api/Modules/Ai/RequestAiEvaluationService.cs`), which builds the
  AI prompt input from the **real** request (title/department/project/
  resources/servers/justifications — not placeholder text) plus real
  Grafana utilization data (see §2) for the request's servers, calls
  `IAiEvaluationClient.EvaluateAsync`, and persists the result to
  `AiEvaluations` regardless of success/failure (ADR 0002's audit trail).
  On success, recurses into `ai_reviewed`. **On failure**, logs an error and
  returns the request sitting at `ai_evaluation` rather than leaving it at
  `submitted` with no trace — loosely matches spec 15's "AI model fails →
  manual skip" intent; no "Skip AI" UI button was built (explicitly out of
  scope per the task brief).
- `targetStage == "done"` → generates the Excel report and persists it as
  an `Attachment` (see §4). Wrapped in try/catch so a report-generation
  failure doesn't fail the `done` transition itself (the request has
  legitimately finished its workflow; the on-demand report endpoint remains
  available as a fallback).
- Anything else → no-op, returns the original result unchanged.

Both `IRequestAiEvaluationService` and `WorkflowAutomationService` are
registered `Scoped` (`AiServiceCollectionExtensions.cs`,
`WorkflowServiceCollectionExtensions.cs`), so within one HTTP request they
share the same `CapacityDbContext` instance as `WorkflowEngine` itself —
consistent identity map across the whole recursive chain.

## 2. Grafana utilization → shared service, feeds both AI eval and ai-insights

New `IGrafanaUtilizationService` / `GrafanaUtilizationService`
(`api/src/Api/Modules/Integrations/Grafana/GrafanaUtilizationService.cs`),
sitting on top of `IGrafanaClient.QueryRangeAsync` (the same low-level
wrapper the existing diagnostic endpoint uses). Per ADR 0003's already-
resolved decision ("CPU/memory/disk utilization %, per hostname, trailing
30 days, avg/max/p95"): for each hostname it runs three PromQL queries
(`cpu_utilization_percent{instance="..."}`,
`memory_utilization_percent{instance="..."}`,
`disk_utilization_percent{instance="..."}`) over the trailing 30 days and
reduces each series to `{Avg, Max, P95}`. **The exact metric names are
placeholders** (same caveat `GrafanaClient.cs` already documents for its own
query construction) — real names depend on what's actually scraped in a
given deployment; this was confirmed live against the real Grafana Cloud
account (see §6) — queries return `success: true` with all-null stats
because no series named `cpu_utilization_percent` etc. exists there yet.
This is expected, not a bug: the query *mechanism* is proven end-to-end
(real HTTP call, real auth, real response parsing), and the metric names
are a one-line change once ADR 0003's naming is finalized against a real
scrape config.

This single service is used by **both**:
1. `RequestAiEvaluationService` (feeds utilization JSON into the AI prompt
   input — spec 4.5/10.4's "Input: Request data + Grafana utilization
   metrics").
2. The new `GET /api/v1/requests/{id}/ai-insights` endpoint (§3).

## 3. `GET /api/v1/requests/{id}/ai-insights` — response shape

New file `api/src/Api/Modules/Ai/AiInsightsEndpoints.cs`, mapped in
`Program.cs`. Any authenticated user, standard `.RequireAuthorization()`
(matches Requests/Workflow endpoints' baseline, no extra role gate).

```jsonc
GET /api/v1/requests/{id}/ai-insights
200 OK
{
  "latestEvaluation": {              // null if the request has never been evaluated
    "id": 45,
    "evaluatedAt": "2026-07-11T16:31:28.818040",
    "score": 70,                     // nullable — null if the AI response failed to parse
    "recommendation": "challenge",   // nullable, same reason
    "flags": ["Insufficient historical utilization data for accurate assessment"]
  },
  "serverUtilization": [             // one entry per distinct hostname on the request's servers
    {
      "hostname": "live-verify-host-01",
      "success": true,               // false if any of the 3 underlying Grafana queries failed
      "errorMessage": null,
      "cpu": { "avg": null, "max": null, "p95": null },     // null stats = no data points, not an error
      "memory": { "avg": null, "max": null, "p95": null },
      "disk": { "avg": null, "max": null, "p95": null }
    }
  ]
}
404 Not Found  // request doesn't exist
```

`latestEvaluation` picks the single most-recent `AiEvaluations` row for the
request (`OrderByDescending(EvaluatedAt).FirstOrDefault()`).
`serverUtilization` is empty (`[]`) if the request has no servers. **Phase
7c builds its "AI Insights" panel directly against this shape** — this is
the contract to build against, live-verified in §6.

## 4. Excel AI Evaluation sheet — real data, all evaluations, newest first

`IReportGenerator.GenerateRequestReport` gained a second parameter:
`GenerateRequestReport(Request request, IReadOnlyList<AiEvaluation>
aiEvaluations)`. Chose this over adding an `AiEvaluations` navigation
collection to the `Request` entity itself — no schema/model change needed,
and it keeps `IReportGenerator`'s existing "caller must eager-load
everything, no lazy-loading proxies" contract consistent (the caller now
explicitly queries evaluations the same way it already explicitly includes
`WorkflowStages`).

`ClosedXmlReportGenerator.AddAiEvaluationSheet` (the `// TODO` placeholder
is gone) now renders every evaluation, **newest first**
(`OrderByDescending(EvaluatedAt)`) — a request can be evaluated more than
once over its lifetime (e.g. a future revise-and-resubmit re-enters
`ai_evaluation`), so all rows are kept, not just the latest. Falls back to
the original "No AI evaluation data available" text only when the list is
genuinely empty (e.g. report downloaded before the request was ever
submitted) — not a placeholder for unbuilt functionality anymore.

Both call sites updated: the on-demand `GET /requests/{id}/report` endpoint
(`ReportsEndpoints.cs`) now queries `AiEvaluations` before calling the
generator; `WorkflowAutomationService`'s `done`-triggered auto-generation
does the same.

## 5. Auto-generate Excel on `done` → `Attachment` row

`WorkflowAutomationService.GenerateAndStoreReportAsync`: on a successful
`done` transition, generates the report (same generator, same data as the
on-demand endpoint), writes it to
**`<API content root>/generated-reports/{RequestNumber}-{timestamp}.xlsx`**
(`IWebHostEnvironment.ContentRootPath` + `"generated-reports"`, directory
created if missing — plain filesystem per the task brief, no blob storage
at Phase 1 scale), then persists an `Attachment` row (`FileName`,
`StoragePath` = full absolute path, `ContentType` =
`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`,
`UploadedByUserId` = the user who triggered the `done` transition,
`UploadedAt` = now). Wrapped in try/catch with error logging — a failure
here does not fail the `done` transition itself.

The existing on-demand `GET /requests/{id}/report` endpoint is **unchanged
in behavior** — still regenerates fresh on every call, still doesn't touch
`Attachments`. The `done` auto-generation is a pure addition, live-verified
in §6 to produce a real file **and** a matching DB row for the same
request.

## 6. Verification performed

**Build/test** (MySQL already running via `docker compose up -d mysql`):
- `dotnet build` — 0 warnings, 0 errors.
- `dotnet test` — **44/44 passed** (40 pre-existing + 4 new). Breakdown of
  what changed and why:
  - `WorkflowEngineTests.cs`: added `ConfigureTestServices` overrides
    forcing Mock `IAiEvaluationClient`/`IGrafanaClient` (every `submitted`
    transition now triggers the real chain, so without this override these
    tests would flakily call the real Ollama/Grafana Cloud configured in
    `appsettings.Development.json`). Removed now-redundant explicit
    `ai_evaluation`/`ai_reviewed` transition calls in several tests (they'd
    now 409 — the request has already auto-cascaded past those stages by
    the time the explicit call runs). Renamed and rewrote
    `Transition_DraftToSubmitted_...` to assert the *final* auto-cascaded
    state (`AiReviewed`) and that an `AiEvaluation` row was persisted.
    Extended `FullHappyPath_DraftToDone_Succeeds` to assert an `Attachment`
    row exists and its file is present on disk after reaching `done`.
    Updated `ConcurrentTransitions_OnSameRequest_...` to resolve
    `IWorkflowAutomationService` from each manual DI scope (the
    `WorkflowEngine` constructor now requires it).
  - `ReportEndpointTests.cs`: same Mock-provider override added. Extended
    the structural test to assert the AI sheet contains real Mock content
    (`"approve"`, `"80"`) and does **not** contain the old placeholder
    text. Added a new test seeding two evaluations directly and asserting
    the Excel sheet renders both, newest first.
  - New `WorkflowAutomationTests.cs`: (a) a capturing `IAiEvaluationClient`
    test double proving the orchestrator's request summary contains the
    request's actual title and server hostname (not placeholder text); (b)
    `ai-insights` endpoint shape assertions (latestEvaluation fields,
    serverUtilization per-hostname cpu/memory/disk); (c) 404 for a
    non-existent request.

**Live verification** (real Ollama + real Grafana Cloud, per
`appsettings.Development.json` — same accounts live-verified in Phase 4;
`dotnet run` on port 5000, MySQL via existing `docker compose`):
1. Logged in as `requestor.dev`, created a full-payload request (title,
   department, one Storage resource, one server `live-verify-host-01`, one
   justification) → `201 Created`, `CAP-2026-0318`.
2. `POST .../transition {targetStage: "submitted"}` → `200 OK`, response
   status **`AiReviewed`** (not `Submitted`) — confirmed the auto-chain ran
   synchronously within the single request. `workflowStages`:
   `draft:Approved, submitted:Approved, ai_evaluation:Approved,
   ai_reviewed:InProgress`.
3. Confirmed against the real local Ollama (`qwen2.5-coder:7b`) — ran this
   three times across separate requests and got **genuinely varying**
   results each time (score 60/"challenge", 80/"approve", 70/"challenge",
   with different real flag text each time), proving this is a live model
   call, not a canned response.
4. `GET .../ai-insights` → `200 OK`, shape matches §3 exactly;
   `serverUtilization[0].hostname == "live-verify-host-01"`,
   `success: true`, all three metric groups present with `null` stats (see
   §2 — expected, metric names aren't scraped in the real account yet).
5. `GET .../report` → downloaded and inspected the raw XLSX (unzipped,
   read `sharedStrings.xml` directly): the AI Evaluation Report sheet
   contains the **real** live-evaluation content — `"challenge"` and
   `"Insufficient historical utilization data for accurate assessment"` —
   not the old placeholder string. Approval Chain sheet also visibly shows
   the `"Automatic system transition (see WorkflowAutomationService)."`
   comment on the auto-driven stages.
6. Manually drove the same request through `capacity_review` →
   `infra_approval` → `done` (as `requestor.dev` → `capacitymanager.dev` →
   `infrahead.dev`) via direct transition calls → all `200 OK`, final
   status `Done`.
7. Queried MySQL directly (`docker exec ... mysql`): confirmed one
   `Attachments` row for the request
   (`CAP-2026-0318-20260711163129091.xlsx`, correct `ContentType`,
   `UploadedByUserId` = the InfraHead who triggered `done`) and confirmed
   the file exists on disk at the stored `StoragePath`
   (`api/src/Api/generated-reports/`).
8. Killed the `dotnet run` API process (confirmed via
   `Get-NetTCPConnection -LocalPort 5000` returning empty afterward). No
   web dev server was started this task (backend-only phase). Left the
   standard MSBuild/VBCSCompiler node-reuse background processes running —
   these are normal dotnet-tooling infrastructure, not stray app/dev-server
   processes.

## 7. OpenCode + Ollama experiment — did not help this time

Per the standing instruction, tried routing context-gathering (not code
writing) through `opencode run -m ollama/qwen2.5-coder:7b` for three
targets: `WorkflowEngine.cs`'s transition logic, the `Api.Modules.Ai`
interface/DTO shapes, and `Api.Modules.Integrations.Grafana`'s client
interface — the idea being to read a plain-English summary instead of
spending my own context reading the files cold.

**Result: unusable, discarded outright, same failure mode Phase 7a already
documented.** Both attempts (different phrasings, absolute and relative
file paths) returned in ~7 seconds with a single malformed raw tool-call
JSON blob printed as plain text —
`{"name": "read", "arguments": {"filePath": "..."}}` — instead of actually
invoking a file-read tool and producing a summary. No file was read, no
summary was produced, nothing to fix or patch. This reproduced identically
across both attempts, confirming it's not prompt-specific.

**Honest assessment**: this is now confirmed twice, once for a
mechanical test-writing task (Phase 7a) and once for pure read-only
summarization (this task) — a strictly easier bar than writing new code.
The local `qwen2.5-coder:7b` model via this `opencode` CLI setup does not
reliably drive the tool-calling loop `opencode run` expects at all; it
appears to emit what looks like a tool-call as text rather than triggering
opencode's own tool dispatch. This isn't a "prompt wasn't precise enough"
problem — it never got far enough to attempt the actual read. Recommend
against reflexively re-trying this pattern in future phases without first
independently confirming (outside of a real task's critical path) that
`opencode run`'s tool-calling loop works at all with this model/config
combination — the underlying issue looks like it's in the opencode↔Ollama
tool-call wiring, not in prompt quality. All context-gathering for this
task was done via direct file reads instead, same as if the experiment had
never been attempted — no time was saved, and the two failed attempts cost
a small amount of extra turnaround before falling back.

## Files touched

- `api/src/Api/Modules/Integrations/Grafana/GrafanaUtilizationService.cs` (new)
- `api/src/Api/Modules/Integrations/IntegrationsServiceCollectionExtensions.cs`
- `api/src/Api/Modules/Ai/RequestAiEvaluationService.cs` (new)
- `api/src/Api/Modules/Ai/AiInsightsEndpoints.cs` (new)
- `api/src/Api/Modules/Ai/AiServiceCollectionExtensions.cs`
- `api/src/Api/Modules/Workflow/WorkflowAutomationService.cs` (new)
- `api/src/Api/Modules/Workflow/WorkflowEngine.cs`
- `api/src/Api/Modules/Workflow/WorkflowServiceCollectionExtensions.cs`
- `api/src/Api/Modules/Reports/IReportGenerator.cs`
- `api/src/Api/Modules/Reports/ClosedXmlReportGenerator.cs`
- `api/src/Api/Modules/Reports/ReportsEndpoints.cs`
- `api/src/Api/Modules/Requests/RequestsDtos.cs` (two mapper methods made `internal`)
- `api/src/Api/Program.cs`
- `api/tests/Api.Tests/WorkflowEngineTests.cs`
- `api/tests/Api.Tests/ReportEndpointTests.cs`
- `api/tests/Api.Tests/WorkflowAutomationTests.cs` (new)

## Known follow-ups (not this task's scope)

- Real PromQL metric names for Grafana utilization (§2) — placeholders
  until ADR 0003's metric set is matched against an actual scrape config.
- No "Skip AI" manual-override UI for the `ai_evaluation` failure case
  (spec 15) — explicitly deferred per the task brief.
- Email-on-transition (spec 4.3/4.4/10.5) is still not wired — untouched by
  this task, remains a gap per the spec-gap audit.
- Phase 7c: build the "AI Insights" panel against the shape in §3.
