# Phase 7a Status — Data Model + Create-Request Contract + Submit/Confirm Actions

Built directly on `phase/6-polish` (not an isolated worktree — see the task's
note on the worktree-isolation tooling bug). Supersedes the stopped,
unmerged `wip/phase-7a-partial-stopped` branch entirely; that branch is
preserved for reference only and should not be merged (see "A stray branch
caused a real DB conflict" below).

Source of truth: `docs/superpowers/specs/2026-07-08-capacity-request-system-design.md`
sections 5 (data model), 7.3 (state transitions), 8.4 (wizard flow), read
directly for this task. Gaps confirmed against `docs/progress/spec-gap-audit.md`.

## 1. Data model — one migration

Migration: `AddRequestWizardFields`
(`api/src/Api.Data/Migrations/20260711153509_AddRequestWizardFields.cs`),
generated via:

```
cd api
dotnet ef migrations add AddRequestWizardFields --project src/Api.Data --startup-project src/Api
```

Adds:

| Entity | New columns |
|---|---|
| `Request` | `Title` (varchar 200, required), `Department` (varchar 100, required), `ProjectName` (varchar 200, required), `ProjectCode` (varchar 50, required), `Sponsor` (varchar 200, required — spec 8.4 wizard step 2, not in the 5.1 table but part of the flow), `StartDate`/`EndDate` (datetime, required), `Description` (text, nullable) |
| `RequestServer` | `IpAddress` (varchar 45, required), `Os` (varchar 100, nullable — free-text, distinct from the existing `Platform` enum), `IsPhysical` (bool) |
| `WorkflowStage` | `AssignedUserId` (int, nullable FK → `Users`, `OnDelete: Restrict`) |
| `Justification` | `AttachmentPaths` (json, nullable — column only, no upload plumbing, explicitly out of scope) |

All new NOT NULL string columns get an empty-string default so the
migration is safe against any pre-existing rows (there weren't any in the
seeded dev set, but this keeps it honest for a real deploy).

Applied and verified against the local MySQL (`docker compose`) — see
"Verification" below.

### A stray branch caused a real DB conflict — worth flagging

The shared local dev MySQL container already had a *different*,
never-committed-here migration applied to it: `wip/phase-7a-partial-stopped`
(commit `e3cc7ff`) had generated its own `AddRequestWizardFields` migration
(different timestamp ID) and run it against the same dev DB before being
stopped. Running `dotnet test` against my fresh migration failed with
`Duplicate column name 'AssignedUserId'` because MySQL already had a
partial, differently-shaped version of this schema (nullable
`ProjectCode`/`ProjectName`/`Sponsor`/dates, no `Title`/`Department` at all,
`IpAddress` present but no `Os`/`IsPhysical`, `AssignedUserId` present but no
FK). I manually reverted the stray columns and the stray
`__EFMigrationsHistory` row via direct SQL (safe — disposable local dev
data, no production data involved), then reapplied cleanly. If anyone
resumes work from `wip/phase-7a-partial-stopped` later, delete it instead —
its migration is incompatible with this one and its DTOs/tests are a
different, earlier design.

## 2. Create-request API contract

**Single-POST-at-the-end contract** (not multi-step draft-then-PATCH) — the
whole 5-step wizard is submitted as one `POST /api/v1/requests` body once
the frontend wizard collects all steps client-side. Chosen for simplicity;
Phase 7c's frontend agent should build directly against this.

### Request body (`CreateRequestRequest`)

```jsonc
{
  "title": "string",
  "department": "string",
  "projectName": "string",
  "projectCode": "string",
  "sponsor": "string",
  "environment": "Prod|DR|UAT|SIT|Dev",
  "projectType": "New|Enhancement|Maintenance|BAU",
  "priority": "Low|Medium|High",
  "startDate": "2026-08-01T00:00:00Z",
  "endDate": "2026-09-30T00:00:00Z",
  "description": "string or null",
  "resources": [                     // required, at least 1, no duplicate resourceTypes
    { "resourceType": "Storage|Cpu|Ram", "currentValue": 200, "requestedValue": 260 }
  ],
  "servers": [                       // optional, may be omitted or empty
    {
      "hostname": "app01", "ipAddress": "10.0.0.5", "os": "RHEL 8.6",
      "isPhysical": false, "resourceType": "Storage",
      "currentValue": 200, "requestedValue": 260,
      "mountPoint": "/data", "platform": "Unix|Wintel",
      "drApplicable": true, "appTier": "Tier 1"
    }
  ],
  "justifications": [                // optional, may be omitted or empty
    { "resourceType": "Storage", "questionKey": "data_lifecycle", "answerText": "..." }
  ]
}
```

**Uplift % is always computed server-side** from each resource's
`currentValue`/`requestedValue` — `(requested - current) / current * 100`,
rounded to 2dp, or `null` when `currentValue` is 0. The DTO does not even
have a field for a client-supplied percentage; there is nothing to "trust"
by construction. Verified live (see below): current=200, requested=260 →
`upliftPercent: 30.0`.

**Validation** (mirrors the existing endpoint's `BadRequest` style, not
exhaustive): required non-blank text fields; `endDate >= startDate`; at
least one resource with a recognized `resourceType` and no duplicates;
`current`/`requestedValue >= 0` everywhere (resources and servers); known
`resourceType`/`platform` enum strings on servers and justifications;
required non-blank hostname/ipAddress on servers and questionKey/answerText
on justifications.

All of Request + its RequestServers + Justifications are added to the
`DbContext` and persisted in a **single `SaveChangesAsync()` call** — one
implicit transaction, all-or-nothing.

### Response shape (`RequestResponse`, from `RequestMapper.ToResponse`)

Now includes everything the create body accepted, plus:
- `resources[]` — reconstructed from the stored `CurrentCapacity`/
  `RequestedCapacity`/`UpliftPercentages` JSON columns, each with
  `resourceType`, `currentValue`, `requestedValue`, `upliftPercent`.
- `servers[]`, `justifications[]` — full entity fields including the new
  ones (`ipAddress`/`os`/`isPhysical`, `attachmentPaths`).
- `requestorUsername` and `requestorDisplayName` — **new fields**, added
  specifically so the frontend can determine "is this my request" without
  decoding the JWT (see "Requestor Info decision" below).
- `workflowStages[].assignedUserId` — new field, currently always `null`
  since nothing assigns a specific user yet (role-based assignment only,
  unchanged from before).

`GET /api/v1/requests/{id}`, `GET /api/v1/requests` (list), and
`POST /api/v1/requests/{id}/transition` all return this same shape via the
same `RequestMapper`. All three now `.Include(r => r.RequestorUser)` (needed
for the new username/display-name fields) — the detail endpoint and
`WorkflowEngine.TransitionAsync`'s query also `.Include` `RequestServers`
and `Justifications` so a transition response reflects the full request;
the list endpoint deliberately does not (summary view, lighter query — its
`resources`/`servers`/`justifications` will read back empty, which is fine
since the frontend's `RequestSummary` type doesn't consume them).

## 3. Requestor Info decision

**Did not add any `User` columns.** Spec 8.4 step 1 (Name/PF/Email/Contact/
Department) is read-only, pre-filled display data — `User.DisplayName`,
`User.Email`, and the already-existing `User.PfNumber` cover Name/Email/PF.
`Contact` has no obviously-correct place yet (not asked for here, and adding
it speculatively would be scope creep) and is left for whoever eventually
wires up user profile editing (see the audit's "User Profile page" gap).
`Department` is captured on the **Request** itself (not `User`) per spec
5.1's `requests.department` column — it's a per-request snapshot (a
requestor's department could change between requests), not an edit to the
user's own profile, and the wizard step 1 UI can simply default the field
from the logged-in user's own department if/when `User.Department` exists,
or just let them type it (current behavior — it's a plain required string
in the create body).

## 4. Submit / Confirm actions (`RequestDetailPage.tsx`)

- **Draft → Submitted**: "Submit Request" button, shown when
  `status === 'Draft'` AND (`role === 'Admin'` OR the logged-in user's
  username matches `requestorUsername`). Calls the existing
  `transitionRequest(id, 'submitted')` (already existed in
  `web/src/api/requests.ts`) via the same `useMutation` pattern as
  `ApprovalQueueTable.tsx`.
- **AiReviewed → Submitted / CapacityReview**: "Revise" and "Confirm & Send
  to Capacity Review" buttons, same visibility gate, shown when
  `status === 'AiReviewed'`. Cannot be live-tested yet — nothing currently
  drives a request into `AiReviewed` (Phase 7b wires the automatic
  `ai_evaluation → ai_reviewed` System transition). Built against the exact
  same API contract as the Draft case; will start working the moment 7b
  lands, no frontend changes anticipated.
- Ownership check: compares `useAuth().user.id` (which is the AD username —
  see `AuthProvider.tsx`) against the new `requestorUsername` response
  field, rather than decoding the JWT for the numeric user id. The frontend
  check is UX-only; `WorkflowEngine.cs`'s existing
  owner-or-Admin-when-`RequiredRole is null` check is the real gate and was
  not touched.
- Also added a handful of the new fields (Title, Department, Sponsor,
  Project Name/Code, Requestor, Planned Dates, Description) to the detail
  page's existing Descriptions block, since that data now actually exists
  and the page was otherwise showing almost nothing — kept to the existing
  layout/component style, no redesign.

`web/src/pages/NewRequestPage.tsx` (the single-step stub form) was
deliberately **left untouched** — it still posts only
`{environment, projectType, priority}` and will now get a 400 from the
expanded contract. This is expected and in-scope per the task ("curl/REST
client is fine if the real wizard UI doesn't exist yet — Phase 7c builds
that next"); Phase 7c replaces this page with the real 5-step wizard against
the contract documented above. Flagging clearly here so it isn't mistaken
for a regression: **a Requestor currently has no working "create request"
form in the UI** until 7c ships. Draft requests for testing Submit/Confirm
must be created via curl/REST client in the meantime (as done for this
task's own verification).

## 5. OpenCode + Ollama experiment

Tried routing the new integration test file
(`api/tests/Api.Tests/CreateRequestWizardEndpointTests.cs`) through
OpenCode+Ollama, per the standing instruction to proactively try it on a
mechanical piece rather than never touching it.

```
opencode run -m "ollama/qwen2.5-coder:7b" "<precise prompt: exact DTO shapes,
exact response fields including the computed upliftPercent, the exact test
file naming/pattern from HealthAndRequestsEndpointTests.cs, 6 specific test
cases to write, explicit 'do not touch any other file' instruction>"
```

First attempt with no `-m` flag routed to `gpt-5.3-codex` (some other
configured provider, not Ollama) and immediately failed with "Quota
exceeded" — had to pass `-m "ollama/qwen2.5-coder:7b"` explicitly to force
the local model configured in `opencode.json`.

With the model pinned correctly, the run **did not fail**, but the output
was low quality and not usable as-is: it printed raw, malformed tool-call
JSON to the terminal instead of cleanly invoking a file-write tool, and the
C# it did produce didn't compile (missing braces, referenced an undeclared
`builder` variable outside any method, wrong login request casing,
truncated mid-statement). No file was actually written to disk (confirmed
via `git status` — nothing new appeared).

Per the task's explicit instruction ("fix it if wrong, or discard and write
it yourself if the output is low quality — don't iteratively patch bad
output"), this was discarded outright rather than patched, and the test
file was written by hand. Conclusion: for this task, OpenCode+Ollama with
the 7B model was not yet reliable enough for even a fairly mechanical,
precisely-scoped test-writing task — worth another look with a more
capable local model or a tighter/simpler prompt if this gets tried again,
but not worth burning more time on for this task.

## 6. Verification performed

- `dotnet build` — 0 warnings, 0 errors.
- `dotnet test` (MySQL via `docker compose up -d mysql`, already running) —
  **40/40 passed**, including the 5 new tests in
  `CreateRequestWizardEndpointTests.cs` (full-payload round-trip incl.
  server-computed `upliftPercent`; 400 for empty resources, backwards
  dates, unknown resource type, negative requested value).
- `npm run build` / `npm run lint` / `npm run test` (web/) — all pass.
- Live browser verification end-to-end:
  1. Started the API (`dotnet run`, port 5000) and web dev server, logged in
     as `requestor.dev` via a REST client for the create-request calls
     (wizard UI doesn't exist yet — see Section 4).
  2. `POST /api/v1/requests` with a full payload (title/department/
     projectName/projectCode/sponsor/dates/description, one Storage
     resource current=200/requested=260, one server, one justification) →
     `201 Created`, `CAP-2026-0278`, **`upliftPercent: 30.0`** computed
     server-side.
  3. `GET /api/v1/requests/{id}` round-tripped every field correctly.
  4. Confirmed `400` for empty `resources` and for `endDate < startDate`.
  5. Logged into the real app (`http://localhost:5173`) as `requestor.dev`,
     opened the request's detail page — confirmed the new fields render
     (title, department, sponsor, project name/code, requestor, planned
     dates, description) and the **"Submit Request" button is visible**.
  6. Clicked **Submit Request** — status transitioned `Draft → Submitted`
     live, workflow timeline updated correctly (`draft` closed
     Approved, `submitted` opened InProgress, assigned role Requestor).
  7. Created a second Draft request, logged in as `capacitymanager.dev`
     (non-owner, non-Admin) and confirmed the Submit button is **correctly
     absent** on that request's detail page — only "Download Excel Report"
     shows, confirming the ownership gate works both ways.
- Killed the `dotnet run` API process (port 5000) and stopped the web
  preview server before finishing; no stray processes left running.

## Known follow-ups (not this task's scope)

- `NewRequestPage.tsx` needs the real 5-step wizard (Phase 7c) — see
  Section 4.
- `ai_evaluation → ai_reviewed` auto-transition (Phase 7b) is required
  before the "Revise"/"Confirm & Send to Capacity Review" buttons built
  here can be exercised live.
- `wip/phase-7a-partial-stopped` should be deleted rather than merged (see
  Section 1) — it's superseded by this work and its schema is incompatible.
