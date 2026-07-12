# Phase 8c Status — Queue Position Indicator + Attachment Upload

Built directly on `phase/8-remaining-gaps`, on top of Phase 8a (theme
toggle/notification preferences/profile page) and Phase 8b (real workflow
email notifications). Confirmed the working tree was clean at `11897e7 Wire
real email notifications into workflow transitions` before starting. Per the
task's explicit instruction, OpenCode+Ollama was not re-attempted this round
(Phase 7a/7b/7c already established it emits broken tool-call JSON instead
of reading/writing files) — everything below was built directly. This is the
third and last of three sequenced Phase-8 workstreams.

## 1. Queue position indicator (spec 6.3)

"You are #3 waiting for Capacity Review" — transparency into a request's
position in a review queue without exposing any other requestor's data (the
response only ever contains a count and this request's own fields).

### Where it's computed

`GET /api/v1/requests/{id}` (`api/src/Api/Modules/Requests/RequestsEndpoints.cs`)
computes `queuePosition` at read time, right after loading the request and
before mapping it to `RequestResponse`:

```csharp
private static readonly HashSet<RequestStatus> QueuedStages =
    [RequestStatus.CapacityReview, RequestStatus.InfraApproval];

int? queuePosition = null;
if (QueuedStages.Contains(request.Status))
{
    var othersAhead = await db.Requests
        .Where(r => r.Status == request.Status && r.Id != request.Id && r.UpdatedAt < request.UpdatedAt)
        .CountAsync();
    queuePosition = othersAhead + 1;
}
```

- **Which stages get a position at all**: only `CapacityReview` and
  `InfraApproval` — the two stages a human is actually waiting on. `Draft`/
  `Submitted` aren't under review yet; `AiEvaluation`/`AiReviewed` are
  system-automatic and settle in seconds via `WorkflowAutomationService`'s
  cascade, so a queue position there would be noise, not a transparency
  feature. Every other stage gets `queuePosition: null`.
- **Ordering proxy**: `Request.UpdatedAt`, not a `WorkflowStages` join.
  `WorkflowEngine.TransitionAsync` sets `request.UpdatedAt = now` in the same
  save as the status change, so for a request currently sitting in a stage,
  `UpdatedAt` *is* the time it entered that stage — the same reasoning
  `web/src/api/requests.ts`'s `RequestSummary` comment already documents for
  the list endpoint. This avoids an extra join against `WorkflowStages`,
  which is proportional for a transparency nicety, not a precise SLA system
  (per the task's own scope note).
- **Position = 1 + how many other requests in the same status have an
  earlier `UpdatedAt`** — oldest-waiting-first, this request's own count
  excluded via `r.Id != request.Id`.
- A request that moves on to the next stage (or is rejected/deferred) simply
  stops matching the `WHERE r.Status == ...` filter for anyone still behind
  it — no separate "left the queue" bookkeeping needed.

### DTO change

`RequestResponse` (`api/src/Api/Modules/Requests/RequestsDtos.cs`) gained a
trailing `int? QueuePosition = null` field, and `RequestMapper.ToResponse`
gained a matching optional `queuePosition` parameter (default `null`). Only
the `GET /api/v1/requests/{id}` handler passes a real value; the create
endpoint, the transition endpoint (`WorkflowEndpoints.cs`), and the list
endpoint (`Program.cs`) all call `ToResponse` with no second argument, so
they keep emitting `null` — correct, since none of those responses reflect
"the state a requestor is currently looking at while waiting."

One incidental fix: `Program.cs`'s list endpoint used to pass the
`RequestMapper.ToResponse` method group directly to `.Select(...)`; with a
new overload taking `(Request, int?)`, the compiler started resolving that
method-group conversion to LINQ's *indexed* `Select<TSource,TResult>(Func<TSource,int,TResult>)`
overload instead (`CS0123`). Fixed by wrapping it in an explicit lambda:
`requests.Select(r => RequestMapper.ToResponse(r))`.

### Frontend

`RequestDetailPage.tsx`: `RequestDetail.queuePosition: number | null`, and a
one-line `Alert` rendered just above the "Workflow History" card:

```tsx
{isOwnerOrAdmin && data.queuePosition !== null && QUEUE_STAGE_LABELS[data.status] && (
  <Alert type="info" showIcon
    message={`You are #${data.queuePosition} waiting for ${QUEUE_STAGE_LABELS[data.status]}.`} />
)}
```

`QUEUE_STAGE_LABELS` maps `CapacityReview`/`InfraApproval` to their
human-readable form ("Capacity Review"/"Infra Approval") — the same
humanization idea as Phase 8b's email subjects, kept local to this page
since it's only used here. Gated on `isOwnerOrAdmin` (the same
username/role check the Submit/Revise buttons already use) so this stays a
transparency feature for the request's own owner (or Admin), not a way to
snoop on queue depth for someone else's request.

## 2. Attachment upload

The `Attachment` entity already existed and was already used by Phase 7b's
auto-generated Excel reports (`WorkflowAutomationService.GenerateAndStoreReportAsync`,
storing under `generated-reports/` at the API's content root) — this phase
adds the user-facing upload/list/download path on top of the same entity,
in `api/src/Api/Modules/Requests/RequestsEndpoints.cs`.

### Endpoints

- **`POST /api/v1/requests/{id}/attachments`** — `[FromForm] IFormFile file`.
  Authorization: request owner or Admin, using the exact same idiom as
  `WorkflowEngine.TransitionAsync`'s ownership check
  (`actingUserRole != UserRole.Admin && actingUserId != request.RequestorUserId`
  → 403) rather than inventing a new one. Validates:
  - non-empty file (400 otherwise)
  - size ≤ 10MB (`MaxAttachmentSizeBytes`, 400 otherwise)
  - extension allowlist: `pdf, xlsx, docx, png, jpg, jpeg, txt`
    (`AllowedAttachmentExtensions`, case-insensitive; 400 on anything else,
    including no extension at all) — proportional to a capacity-request
    tool per the task's own framing, not a full antivirus pipeline.

  On success, stores the file under
  `request-attachments/{requestId}/{guid}-{originalFileName}` at the API's
  content root (`IWebHostEnvironment.ContentRootPath`) — same plain-filesystem
  storage convention as `generated-reports/`, a GUID prefix added so two
  uploads of the same filename to the same request never collide on disk.
  Persists an `Attachment` row (`FileName` = the original name, `StoragePath`
  = the full disk path, `ContentType` from the multipart part, falling back
  to `application/octet-stream` if the browser didn't send one) and returns
  `201 Created` with the `AttachmentResponse` shape.

- **`GET /api/v1/requests/{id}/attachments`** — lists attachments for a
  request, newest first. Authorization: any authenticated user (matches
  `GET /api/v1/requests/{id}`'s own baseline — that endpoint has no
  ownership/role filtering today, only `RequireAuthorization()`, the same
  baseline `ReportsEndpoints.cs` documents explicitly for the Excel-report
  download; this phase matches that existing convention rather than
  inventing a stricter one for attachments specifically).

- **`GET /api/v1/requests/{id}/attachments/{attachmentId}`** — downloads one
  attachment's bytes via `Results.File(bytes, contentType, fileName)`, the
  same pattern `ReportsEndpoints.cs`'s report download already uses. Same
  authorization baseline as the list endpoint. 404 if the attachment or its
  on-disk file doesn't exist.

### DTOs

`AttachmentResponse(int Id, string FileName, string ContentType, int
UploadedByUserId, string UploadedByDisplayName, DateTime UploadedAt)` and a
`RequestMapper.ToAttachmentResponse` mapper, both added to
`RequestsDtos.cs` alongside the existing `RequestResponse`/`RequestMapper`.

### Storage path / gitignore

`request-attachments/` added to `.gitignore` right next to the existing
`generated-reports/` entry, same reasoning (runtime output on local disk,
not source):

```
# User-uploaded request attachments (Phase 8c, RequestsEndpoints) —
# runtime output on local disk, not source.
api/src/Api/request-attachments/
```

### Frontend

`RequestDetailPage.tsx` gained an "Attachments" card at the bottom of the
page:
- An AntD `Upload.Dragger` (`customRequest` posts through a new
  `uploadAttachment()` helper in `web/src/api/requests.ts`, which builds a
  `FormData` and calls a new `apiFetchFormData` helper in
  `web/src/api/client.ts` — same auth-header injection as `apiFetch`/
  `apiFetchBlob`, but deliberately does **not** set `Content-Type` itself so
  the browser can set the multipart boundary parameter). Shown only when
  `canUploadAttachment` — owner-or-Admin **and** the request is still
  `Draft`/`Submitted` (a UI nicety matching the task's "editable-ish"
  guidance, not a hard security boundary; the backend's owner-or-Admin check
  is the real gate).
- A `List` of existing attachments (filename, uploader display name,
  uploaded-at timestamp, a Download button) fetched via a new
  `fetchAttachments()` helper — shown regardless of `canUploadAttachment`,
  since anyone who can view the request can view/download its attachments.
- Download reuses the existing `apiFetchBlob` + synthetic-`<a>` pattern
  `handleDownloadReport` already established for the Excel report, so the
  browser saves the file under its original name rather than opening it
  inline.

## 3. Files touched

- `api/src/Api/Modules/Requests/RequestsDtos.cs` — `RequestResponse` gained
  `QueuePosition`; `RequestMapper.ToResponse` gained the optional
  `queuePosition` parameter; new `AttachmentResponse` +
  `RequestMapper.ToAttachmentResponse`.
- `api/src/Api/Modules/Requests/RequestsEndpoints.cs` — `GET .../{id}` now
  computes `queuePosition`; three new attachment endpoints
  (POST/GET/GET-by-id) plus the extension allowlist / size limit constants.
- `api/src/Api/Program.cs` — one-line fix for the `Select` method-group
  ambiguity described above.
- `.gitignore` — `request-attachments/` entry.
- `api/tests/Api.Tests/QueuePositionEndpointTests.cs` — new (3 tests).
- `api/tests/Api.Tests/AttachmentEndpointTests.cs` — new (6 tests).
- `web/src/api/client.ts` — new `apiFetchFormData` helper.
- `web/src/api/requests.ts` — new `Attachment` type, `fetchAttachments`,
  `uploadAttachment`.
- `web/src/pages/RequestDetailPage.tsx` — queue-position `Alert`, Attachments
  card (`Upload.Dragger` + `List` + download).
- `.claude/launch.json` — added so `preview_start` could launch the Vite dev
  server for live verification (didn't exist on this branch before).

## 4. Verification

**Automated** (`docker compose up -d mysql` already running, confirmed
healthy before starting):

- `dotnet build` — 0 warnings, 0 errors.
- `dotnet test` — **61/61 passed** (52 pre-existing from Phase 8b + 3 new in
  `QueuePositionEndpointTests.cs` + 6 new in `AttachmentEndpointTests.cs`):
  - `ThreeRequests_AdvancedToCapacityReviewInOrder_GetSequentialPositions` —
    creates 3 requests, advances each to `CapacityReview` in order (with a
    small delay between each to guarantee distinct `UpdatedAt` timestamps),
    asserts their positions come back **strictly consecutive**
    (`first`, `first+1`, `first+2`) rather than asserting an absolute
    `1/2/3` — this repo's integration tests share one local dev MySQL across
    every test class and prior manual/automated runs routinely leave other
    requests sitting in `CapacityReview`, so an absolute-position assertion
    would be flaky against that shared state; relative ordering is what
    spec 6.3 actually promises.
  - `RequestNotInHumanReviewStage_HasNullQueuePosition` — a fresh Draft
    request's `queuePosition` is JSON `null`.
  - `RequestResolvedOutOfQueue_DoesNotCountTowardOthersStillWaiting` — a
    second request's position drops by exactly one once the request ahead
    of it is moved on to `InfraApproval` (leaves the `CapacityReview`
    queue entirely).
  - `UploadThenListThenDownload_RoundTripsFileContentAndMetadata` — uploads
    a small text file via multipart POST, confirms it appears in the list
    with the right `fileName`, downloads it back and asserts byte-for-byte
    content equality plus the right `Content-Type`/`Content-Disposition`,
    and confirms the row's `StoragePath` is actually on disk under
    `request-attachments/{requestId}/`.
  - `Upload_OversizedFile_IsRejected` — an 11MB file (over the 10MB limit)
    gets `400 Bad Request`.
  - `Upload_DisallowedExtension_IsRejected` — a `.exe` "attachment" gets
    `400 Bad Request`.
  - `Upload_ByNonOwnerNonAdmin_IsForbidden` — `infrahead.dev` uploading to
    `requestor.dev`'s own request gets `403 Forbidden`.
  - `Upload_ByAdmin_ForSomeoneElsesRequest_Succeeds` — the local `admin`
    dev user uploading to someone else's request succeeds (`201 Created`).
  - `GetAttachments_WithoutAuth_IsUnauthorized` — matches the existing
    `GetReport_WithoutAuth_IsUnauthorized` baseline pattern.
- `npm run build` — clean (`tsc -b && vite build`).
- `npm run lint` — clean.
- `npm run test` (vitest) — 7 test files, 15 tests, all passing (unchanged
  from before this phase — no existing frontend test touches
  `RequestDetailPage`'s new sections directly, so this is a regression
  check, not new coverage; the live-browser verification below covers the
  new UI instead).

**Live verification** (API run directly via `dotnet run --urls
http://localhost:5000`; Vite dev server started via `preview_start` against
a new `.claude/launch.json` config, since none existed on this branch yet):

1. Logged in as `requestor.dev` via `POST /api/v1/auth/login`, created two
   requests directly via `POST /api/v1/requests` (`CAP-2026-0541` id 556,
   `CAP-2026-0542` id 557), then advanced each through
   `submitted → capacity_review` via direct `POST .../transition` calls (A
   fully before B started, matching the task's "manually advance both to
   CapacityReview via direct API calls" instruction).
2. Queried both requests' `GET /api/v1/requests/{id}` responses directly:
   `queuePosition` for A and B came back as consecutive integers (65 and 66
   respectively — this dev database has accumulated many `CapacityReview`
   requests across every earlier phase's live-verification runs, so the
   absolute numbers are large, but the **relative** ordering — B is exactly
   one behind A — is the property that actually matters and is what's
   asserted here and in the automated tests).
3. Opened `http://localhost:5173/requests/557` in the browser (logged in as
   `requestor.dev` through the real login form): the page rendered **"You
   are #66 waiting for Capacity Review."** directly above the Workflow
   History timeline, confirming the end-to-end wire-up (API → DTO →
   frontend rendering), not just the API response shape.
4. Created a third, still-Draft request (`CAP-2026-0543`, id 558) and
   confirmed its detail page shows no queue-position line at all (Draft
   isn't a queued stage) and its Attachments section reads "No attachments
   yet." with an upload dropzone visible (owner viewing their own Draft
   request).
5. Using Playwright (logged in as `requestor.dev` through the real login
   form, separately from the primary browser session) against request 558:
   clicked the upload dropzone, selected a small real `.txt` file, and
   confirmed it appeared in the Attachments list as
   "live-verify-attachment.txt — Uploaded by Dev Requestor · <timestamp>"
   with a Download button, no page reload needed (React Query cache
   invalidation on upload success).
6. Clicked Download: the browser saved `live-verify-attachment.txt` with
   the original filename and exact original byte content (47 bytes,
   confirmed by direct comparison against the source file). Cross-checked
   with a direct `curl` against `GET /api/v1/requests/558/attachments/19`:
   `Content-Type: text/plain`,
   `Content-Disposition: attachment; filename=live-verify-attachment.txt;
   filename*=UTF-8''live-verify-attachment.txt`, and byte-identical body.
7. Confirmed the file was actually persisted to disk under
   `api/src/Api/request-attachments/558/` (not just a DB row with no
   backing file).
8. Killed the `dotnet run` process (`Api.exe`, PID 10968, found via
   `netstat`/`Get-Process`) and confirmed port 5000 is no longer listening
   (only stale `TIME_WAIT`/`CLOSE_WAIT` socket entries remained, which clear
   on their own). Stopped the Vite dev server via `preview_stop`. Left the
   pre-existing `claudeproject-mysql-1` container running (not started by
   this task). Two `dotnet.exe` MSBuild node-reuse processes remained
   running (`dotnet build`/`dotnet test`'s build-server cache, standard
   `nodeReuse:true` behavior, not the API and not listening on any
   API-related port) — left alone as unrelated build tooling rather than
   killed, since they're not what the task's cleanup instruction targets.

## Known follow-ups (not this task's scope)

- `GET /api/v1/requests/{id}` (and by extension the new attachment
  endpoints, which deliberately match its authorization baseline) has no
  per-request ownership/role restriction today — any authenticated user can
  view any request's detail, workflow history, or attachment list. This is
  a pre-existing gap (not introduced by this phase) that a future phase
  should address if row-level read security is ever required beyond the
  list endpoint's existing Requestor-sees-own-requests-only filtering.
- The queue position is a simple `COUNT` proxy via `UpdatedAt`, not a
  dedicated "entered stage at" column — correct per the task's explicit
  "keep this simple" framing, but would need revisiting if `UpdatedAt` ever
  gets bumped by something other than a workflow transition.
- No thumbnail/preview for image attachments — download-only, matching the
  task's "keep it simple" framing for this feature.
- This closes out all three sequenced Phase-8 workstreams (8a/8b/8c). The
  three pending business decisions noted in the root `CLAUDE.md` (pilot team,
  go-live date, real AD group → role mapping) remain unresolved and still
  gate rollout, not code.
