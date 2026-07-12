# Phase 8b Status — Real Workflow Email Notifications

Built directly on `phase/8-remaining-gaps`, on top of Phase 8a (theme
toggle/notification preferences/profile page). Confirmed the working tree
was clean at `8fbe8c4 Add theme toggle, notification preferences, and User
Profile page` before starting. Per the task's explicit instruction,
OpenCode+Ollama was not re-attempted this round (Phase 7a/7b/7c already
established it emits broken tool-call JSON instead of reading/writing
files) — everything below was built directly. This is the second of three
sequenced Phase-8 workstreams; queue position + attachment upload follows
once this lands and is reviewed.

## 1. What notifies whom

Every successful workflow transition, once it *settles* (see cascade
reasoning below), triggers up to two notifications, both routed through
`IOutboxWriter.EnqueueEmailAsync` (spec 2.3/4.4/10.5 — API enqueues, the
existing `OutboxProcessor` background service delivers out of band):

1. **Status changed → the request's owner** (`Request.RequestorUser.Email`).
   Always attempted, subject to that user's
   `notificationPrefs.requestStatusChanged` (Phase 8a). Reports the status
   *before* the whole settled transition/cascade and the status it landed
   on, plus the human-provided comment if any (see origin-tracking below).
2. **New task waiting → every user with the settled stage's
   `WorkflowConfig.RequiredRole`** (e.g. `capacity_review` → CapacityManager,
   `infra_approval` → InfraHead). There's no single "assigned user" per
   stage in this app — roles, not individuals, own queues — so every user
   with the role is a recipient, each independently subject to their own
   `notificationPrefs.newAssignedTask`. Stages with `RequiredRole == null`
   (`draft`, `ai_evaluation`, `ai_reviewed`, `done`) get no role
   notification, only the owner's status-changed email.

Email content is plain subject + body text (no HTML templating — matches
the existing `MockEmailClient`/`MailtrapSmtpEmailClient` usage pattern,
proportional to Phase 1 scale):

- Status-changed subject: `Capacity Request {RequestNumber}: status changed
  to {NewStatus}`. Body: `Your capacity request {RequestNumber} ({Title})
  changed status: {OldStatus} -> {NewStatus}.` plus `Comment: {comment}` if
  one was provided.
- New-task subject: `Capacity Request {RequestNumber}: new task waiting in
  {Stage}`. Body: `Capacity request {RequestNumber} ({Title}) is now
  waiting for {Role} review at the {Stage} stage.` plus the comment if any.
- Statuses/stages are humanized via the existing `StageNameExtensions`
  snake_case mapping, title-cased (`"capacity_review"` → `"Capacity
  Review"`) rather than shown as raw PascalCase enum names.

## 2. Cascade-suppression reasoning

`WorkflowAutomationService`'s existing automatic chain (`submitted` →
`ai_evaluation` → `ai_reviewed`, all within one HTTP call) would otherwise
fire a notification at every hop — one Submit click producing three emails
about stages nobody is actually waiting to review (system stages have no
`RequiredRole`, and the requestor doesn't need three separate "status
changed" emails for one action).

**Approach**: a new `WorkflowCascadeOrigin(RequestStatus OldStatus, string?
Comments)` record (`api/src/Api/Modules/Workflow/WorkflowCascadeOrigin.cs`)
threads the cascade's *true* starting point through the recursion.
`WorkflowEngine.TransitionAsync` gained an optional `cascadeOrigin`
parameter (defaults `null` for every real caller — HTTP endpoint, tests):
on the first call in a chain it captures its own pre-transition
status/comment as the origin; `WorkflowAutomationService.AdvanceAsync`
passes that same origin back into its recursive `engine.TransitionAsync`
call so it survives unchanged no matter how many automatic hops follow.

`WorkflowAutomationService.RunPostTransitionHooksAsync`'s switch decides,
per target stage, whether this hop cascades further (no notification - the
recursive call's own hook will decide) or settles (notify using the
threaded origin, not this hop's own before/after):

- `"submitted"` → always cascades into `ai_evaluation` — never settles here.
- `"ai_evaluation"` on **success** → cascades into `ai_reviewed` — never
  settles here. On **failure** → does NOT cascade further; the request is
  genuinely stuck at `ai_evaluation`, which *is* a real state change worth
  telling the requestor about, so it notifies here.
- `"done"` → generates the Excel report (unchanged from Phase 7b), then
  notifies — always a leaf.
- default (`ai_reviewed`, `capacity_review`, `infra_approval`, `rejected`,
  `deferred`) → no further automation queued, always notifies.

Net effect: a plain Submit click settles at `ai_reviewed` (or
`ai_evaluation` on AI failure) and produces **exactly one** status-changed
email, reporting `Draft -> AiReviewed` (or wherever the cascade actually
started, e.g. `AiReviewed -> AiReviewed`... in practice `Draft` or
`AiReviewed` are the only two real starting points, since only `draft ->
submitted` and `ai_reviewed -> submitted` [resubmit] ever enter this
chain) — not the internal `AiEvaluation -> AiReviewed` hop, and using the
human's actual submit comment rather than
`WorkflowAutomationService`'s internal `"Automatic system transition..."`
placeholder text (which never leaks into a user-facing email).

Manual, non-cascading transitions (`ai_reviewed -> capacity_review`,
`capacity_review -> infra_approval`, `-> rejected`/`-> deferred`, `->
done`) settle immediately and always fire — origin is just that single
hop's own before/after/comment, since `cascadeOrigin` is null for the
outermost/only call.

## 3. Notifications module — given real content, not deleted

`NotificationsServiceCollectionExtensions.AddNotificationsModule()` was
empty scaffolding since the Foundation phase. Rather than deleting it, it
now registers `INotificationService` / `NotificationService`
(`api/src/Api/Modules/Notifications/NotificationService.cs`) — a thin
abstraction wrapping "notify owner" / "notify role" so
`WorkflowAutomationService` calls two clean methods instead of building
`EmailOutboxPayload`s inline and re-deriving the opt-out check at every call
site:

```csharp
public interface INotificationService
{
    Task NotifyRequestStatusChangedAsync(Request request, RequestStatus oldStatus, RequestStatus newStatus, string? comments, CancellationToken ct = default);
    Task NotifyRoleOfNewTaskAsync(Request request, string stageName, string requiredRole, string? comments, CancellationToken ct = default);
}
```

Each method reads the relevant recipient's `NotificationPreferences` (the
same JSON shape Phase 8a added to `UserPreference.NotificationPrefs`,
deserialized with the same fallback-to-defaults-on-corrupt-data behavior as
`MeEndpoints.ParseNotificationPrefs` — duplicated as a few lines rather than
calling into an endpoint-mapping class from another module) and silently
skips enqueueing (with an info/warning log line) when the recipient opted
out, when no users hold the required role, or when the role string doesn't
parse to a known `UserRole`.

This was a small enough addition (one interface, one implementation, a
one-line module registration) that the "clean abstraction" option was worth
it over just calling `IOutboxWriter` directly from
`WorkflowAutomationService` — it keeps every opt-out check in one place and
gives the previously-permanently-empty module an actual reason to exist,
addressing the exact gap flagged by the spec-gap audit.

## 4. Files touched

- `api/src/Api/Modules/Workflow/WorkflowCascadeOrigin.cs` — new record.
- `api/src/Api/Modules/Workflow/WorkflowEngine.cs` — `TransitionAsync`
  gained the optional `cascadeOrigin` parameter; computes/threads the
  origin into `RunPostTransitionHooksAsync`.
- `api/src/Api/Modules/Workflow/WorkflowAutomationService.cs` —
  `RunPostTransitionHooksAsync`/`AdvanceAsync` thread `WorkflowCascadeOrigin`
  through; new `NotifySettledTransitionAsync` helper calls
  `INotificationService` at every non-cascading branch.
- `api/src/Api/Modules/Notifications/NotificationService.cs` — new
  `INotificationService`/`NotificationService`.
- `api/src/Api/Modules/Notifications/NotificationsServiceCollectionExtensions.cs`
  — registers `INotificationService` (was a no-op).
- `api/tests/Api.Tests/NotificationWorkflowTests.cs` — new test file (5
  tests, see below).

No frontend changes — this phase is entirely backend (the emails are
delivered out of band; there's no in-app notification center in Phase 1
scope).

## 5. Verification

**Automated** (`docker compose up -d mysql` already running/healthy,
started before this task, left running):

- `dotnet build` — 0 warnings, 0 errors.
- `dotnet test` — **52/52 passed** (47 pre-existing + 5 new in
  `NotificationWorkflowTests.cs`):
  - `SubmittingRequest_EnqueuesExactlyOneStatusChangedEmail_ForTheSettledStage`
    — draft→submitted (cascades to ai_reviewed) enqueues exactly one email,
    to the requestor, reporting `Draft -> Ai Reviewed` (the cascade's true
    origin, not an intermediate hop's own before/after).
  - `AiEvaluationFailure_SettlesAtAiEvaluation_AndStillNotifiesOnce` — a
    failing AI evaluation (test double) settles at `ai_evaluation` instead
    of cascading further, and still fires exactly one email.
  - `ManualTransitionIntoStageWithRequiredRole_NotifiesRequestorAndRole` —
    `ai_reviewed -> capacity_review` fires both the requestor's
    status-changed email and capacitymanager.dev's new-task email; asserts
    the total outbox count for the request is exactly 3 (1 from the earlier
    submit cascade + these 2), i.e. nothing extra snuck in.
  - `Transition_RespectsRequestStatusChangedOptOut` — with
    `requestStatusChanged: false` on requestor.dev, the submit cascade
    enqueues zero emails. Resets the preference afterward (shared dev DB).
  - `Transition_RespectsNewAssignedTaskOptOut` — with `newAssignedTask:
    false` on capacitymanager.dev, `capacity_review`'s role email is
    skipped while the requestor's own status-changed email still fires.
    Resets afterward.
  - One implementation snag: `OutboxMessage.Payload` is a MySQL `json`
    column, so a naive EF `.Contains(requestNumber)` (and even
    `EF.Functions.Like`) translates to a JSON-aware function that throws
    ("Invalid JSON text... cast_as_json") when the argument isn't itself
    valid JSON. Fixed by fetching all `Email`-type rows and filtering by
    substring client-side instead of pushing the filter into SQL.

**Live verification** (API run directly via `dotnet run --urls
http://localhost:5000`, no web dev server needed since this phase is
backend-only; confirmed `Email:Provider` in `appsettings.Development.json`
is real `"Mailtrap"` sandbox, not Mock, before choosing how to verify
delivery):

1. Logged in as `requestor.dev`, created and submitted request
   `CAP-2026-0454` (title "Phase8b Live Verification Request", comment
   "Live verification submit comment"). Response status after the single
   `POST .../transition {targetStage: "submitted"}` call was `AiReviewed`
   (the automatic cascade completed).
2. Queried `OutboxMessages` directly (`docker exec claudeproject-mysql-1
   mysql ...`): **exactly one** row for this request — subject `Capacity
   Request CAP-2026-0454: status changed to Ai Reviewed`, body `Your
   capacity request CAP-2026-0454 (Phase8b Live Verification Request)
   changed status: Draft -> Ai Reviewed.\n\nComment: Live verification
   submit comment`, to `requestor.dev@dev.local` — confirming both the
   cascade-dedup and the origin/comment threading live, not just in tests.
3. Waited for `OutboxProcessor`'s poll tick (5s default): the row's status
   went `Pending` → `Processing` → `Sent`, with `ProcessedAt` populated and
   no `LastError`. Server log confirmed: `Sent email to
   requestor.dev@dev.local with subject Capacity Request CAP-2026-0454:
   status changed to Ai Reviewed` — a real SMTP send to Mailtrap's sandbox,
   not a mock.
4. Manually transitioned the same request `capacity_review` (comment
   "Moving to capacity review"). Queried `OutboxMessages` again: two new
   rows — one status-changed email to `requestor.dev@dev.local`
   (`AiReviewed -> CapacityReview`... rendered as `Ai Reviewed -> Capacity
   Review`) and one new-task email to `capacitymanager.dev@dev.local`
   (`Capacity Request CAP-2026-0454: new task waiting in Capacity Review`),
   confirming the `RequiredRole` → role-notification path live.
5. All three messages for this request eventually reached `Sent`. One
   (`capacitymanager.dev`'s new-task email) needed 2 retry attempts,
   `LastError` briefly showing `5.7.0 Too many emails per second... upgrade
   your plan` — the Mailtrap sandbox's rate limit, hit because the
   automated test suite (which does *not* mock `IEmailClient`, so every
   test run sends real emails to the same sandbox) had just run
   immediately before. `OutboxProcessor`'s existing retry-on-Failure logic
   handled it correctly with no code changes needed — this is exactly the
   scenario the async outbox pattern (spec 2.3) exists to make non-fatal.
6. Killed the `dotnet run` process (`Api.exe`, found via `Get-NetTCPConnection
   -LocalPort 5000` → PID 6328, plus its `dotnet run` wrapper PID 23220)
   and confirmed port 5000 is free and no `Api.exe` process remains. No web
   dev server was started this phase (backend-only change), so nothing to
   stop there. The pre-existing `claudeproject-mysql-1` container was left
   running (not started by this task).

## Known follow-ups (not this task's scope)

- The Mailtrap sandbox's per-second rate limit means running the full test
  suite repeatedly in quick succession can cause transient `Failed`→retry
  cycles on some outbox messages (self-healing, not a correctness bug, but
  worth knowing if a future CI run's outbox-related assertions ever flake
  on timing). Not addressed here since none of this phase's own tests
  assert delivery timing precisely enough to be affected — they only
  assert on the outbox row's existence/content, not `Sent` status.
- No in-app "notification center" / unread-count UI exists yet (out of
  Phase 1 scope per CLAUDE.md's scope boundary) — these are email-only
  notifications for now.
- Phase 8c (queue position + attachment upload) is next.
