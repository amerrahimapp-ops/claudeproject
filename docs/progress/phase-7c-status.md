# Phase 7c Status — 5-Step Request Wizard, AI Insights Panel, Logout

Built directly on `phase/6-polish` (no isolated worktree — same
worktree-isolation workaround noted in 7a/7b). Confirmed the working tree
was clean at `7d9260c Allow ai_reviewed -> submitted transition (Revise
action)` before starting. Builds directly on Phase 7a's create-request
contract and Phase 7b's auto-evaluation chain / `ai-insights` endpoint —
both status docs were read directly per this task's brief (not delegated),
since they're the exact API contracts this phase's frontend had to match.

## 1. `web/src/pages/NewRequestPage.tsx` — real 5-step wizard

Replaces the old single-step (Environment/ProjectType/Priority only) stub
entirely. Uses AntD `Steps` with local component state carrying the wizard
across steps (a `Form` instance for Step 2's fields via `Form.useForm()`,
plain React state for Steps 3-5's dynamic lists) and a single
`POST /api/v1/requests` call at the very end, matching Phase 7a's
single-POST-at-the-end contract.

- **Step 1 — Requestor Info**: read-only `Descriptions` block showing
  `user.name` / `user.email` from `useAuth()`. No fields collected, per
  Phase 7a's decision not to add PF/Contact/Department to `User` — matches
  `docs/progress/phase-7a-status.md` section 3 exactly.
- **Step 2 — Project Info**: `title`, `department`, `projectName`,
  `projectCode`, `sponsor`, `environment` (Select), `projectType` (Select),
  `priority` (Select), a `RangePicker` mapped to `startDate`/`endDate`
  (`.toISOString()`), `description` (optional). Field names match
  `CreateRequestRequest` in `api/src/Api/Modules/Requests/RequestsDtos.cs`
  exactly. Required-field validation via `Form.validateFields()`.
- **Step 3 — Resources**: `Checkbox.Group` over the `ResourceType` enum
  (`Storage`/`Cpu`/`Ram`); each selected type gets its own current/requested
  `InputNumber` pair and a live-computed uplift % (`(requested - current) /
  current * 100`, client-side only, display-only — the server recomputes
  and enforces this itself regardless of what's shown here, per
  `ResourceSummaryResponse`'s doc comment in `RequestsDtos.cs`). At least
  one resource type is required to proceed.
- **Step 4 — Server Details**: one AntD `Table` per selected resource type,
  each row editable in-place (hostname, IP address, OS, platform
  Unix/Wintel, is-physical, current/requested, mount point — Storage rows
  only — DR applicable, app tier), with Add/Remove row buttons. Optional
  overall (contract allows an empty `servers[]`), but any row that exists
  must have hostname/IP/current/requested filled before advancing.
- **Step 5 — Justifications**: two fixed questions per selected resource
  type (`current_utilization`, `business_justification`) — a small, fixed
  Q&A set rather than a dynamic form builder, per the task's explicit
  scope note. All shown questions are required before Submit.

Final submit combines all 5 steps' state into one
`CreateRequestRequest`-shaped payload, calls
`POST /api/v1/requests`, and navigates to `/requests/{id}` on success —
same pattern as the old page.

## 2. AI Insights panel — `RequestDetailPage.tsx`

New `Card` ("AI Insights") fetching `GET /api/v1/requests/{id}/ai-insights`
via a second `useQuery` (same React Query pattern as the existing request
detail fetch), response-typed to match
`api/src/Api/Modules/Ai/AiInsightsEndpoints.cs`'s `AiInsightsResponse`
exactly.

- **`latestEvaluation: null`** (request never evaluated — still
  Draft/Submitted): renders an AntD `Empty` with explanatory text, not an
  error state.
- **Evaluation present**: score, a colored `Tag` for recommendation
  (approve=green, challenge=orange, reject=red), and a bullet list of
  flags.
- **`serverUtilization`**: a `Table` with one row per hostname; a row with
  `success: false` renders its `errorMessage` in place of the
  cpu/memory/disk columns (in red `Text type="danger"`) instead of showing
  blank/null metrics — the null-but-`success:true` case (no Grafana data
  points yet, expected per Phase 7b's documented metric-name-placeholder
  caveat) renders `avg — · max — · p95 —` instead.

**Bug found and fixed during live verification, not caught by unit
tests**: the existing Submit/Revise/Confirm transition mutation's
`onSuccess` only invalidated the `['request', id]` and `['requests']`
React Query keys, not `['requestAiInsights', id]`. Since Submit
auto-cascades all the way to `AiReviewed` and produces a brand new
`AiEvaluation` row in the same request/response cycle, the AI Insights
panel kept showing the stale "not evaluated yet" empty state after
clicking Submit until some unrelated refetch happened to fire. Fixed by
adding the missing `queryClient.invalidateQueries({ queryKey:
['requestAiInsights', id] })` call alongside the other two. Confirmed
fixed live (see section 5) — this is exactly the kind of gap that only
shows up when actually driving the real cascade in a browser, not in a
mocked-fetch unit test, which is why the live end-to-end pass mattered
here.

## 3. Logout button — `AuthenticatedLayout.tsx`

A plain `Button` (icon + "Logout" label) next to the existing
user-name/role `Tag` in the header — no dropdown menu, per the task's
explicit "don't over-build it" note. `onClick` calls `logout()` (already
existed and worked in `AuthProvider.tsx`, just never wired to a control)
and then `navigate('/login')`.

## 4. OpenCode + Ollama experiment — third confirmation, same failure

Per the standing instruction, tried it once more on a narrow, low-stakes
context question before falling back to direct reads:

```
opencode run -m "ollama/qwen2.5-coder:7b" "Read the file
api/src/Api/Modules/Requests/RequestsDtos.cs relative to the current
working directory and summarize the exact JSON property names and types
of the CreateRequestRequest record and its nested
Resources/Servers/Justifications records. Do not modify any file. Just
report the field list."
```

Result: identical failure mode to both Phase 7a's and 7b's documented
attempts. It printed a single raw, unexecuted tool-call JSON blob as plain
text — `{"name": "read", "arguments": {"filePath":
"./api/src/Api/Modules/Requests/RequestsDtos.cs"}}` — instead of actually
invoking a file-read tool, in ~5 seconds, with no file access and nothing
to fix or patch. This is now confirmed three times across three different
tasks (test-writing in 7a, pure summarization in 7b, and again here) —
consistent enough that this session did not retry with a different prompt
again and went straight to reading `phase-7a-status.md`,
`phase-7b-status.md`, `RequestsDtos.cs`, `AiInsightsEndpoints.cs`, and all
the relevant frontend files directly. No time was spent iterating on
prompts beyond this one confirming attempt.

## 5. Verification performed

**Automated**:
- `npx tsc -b` — 0 errors.
- `npx eslint .` — 0 warnings/errors.
- `npx vitest run` — **15/15 passed** across 7 test files, including 3 new
  ones added this phase:
  - `web/src/pages/NewRequestPage.test.tsx` (4 tests): read-only requestor
    info from `useAuth()`; Project Info step blocks `Next` until required
    fields validate; Resources step blocks `Next` until a resource type is
    selected; a full happy-path walk through all 5 steps (including
    driving AntD `Select`/`RangePicker`/`Table` inputs via `fireEvent`)
    asserting the exact submitted JSON body (title/department/.../
    resources/servers/justifications) matches the wizard's inputs.
  - `web/src/pages/RequestDetailPage.test.tsx` (3 tests): AI Insights empty
    state when `latestEvaluation` is `null`; full render of score/
    recommendation/flags/server utilization stats; error-message-instead-
    of-blank-metrics for a `success: false` server utilization entry.
  - `web/src/layouts/AuthenticatedLayout.test.tsx` (1 test): clicking
    Logout calls `logout()` and navigates to `/login`.
- `npm run build` — clean production build (pre-existing >500kB single-
  chunk warning, unrelated to this phase's changes).

**Live, end-to-end** (MySQL via existing `docker compose`, `dotnet run
--urls http://localhost:5000` — note the repo's `launchSettings.json`
default profile is actually port 5030; explicitly overrode with `--urls`
to match the frontend's hardcoded `API_BASE_URL`, same as Phase 7a/7b did
implicitly — `npm run dev` on port 5173 via `preview_start`):

1. Logged in as `requestor.dev`.
2. Drove the full 5-step wizard: title "Q3 Storage Uplift", department
   Engineering, sponsor Jane Sponsor, project name/code, Environment=Prod,
   ProjectType=Enhancement, Priority=High, dates 2026-08-01 – 2026-09-30;
   Resources: Storage current=200/requested=260 (uplift showed **+30.0%**
   live); Server Details: one row (`app01`, `10.0.0.5`, RHEL 8.6, Unix,
   200/260, `/data`, DR applicable, Tier 1); Justifications: both Storage
   questions answered.
3. Submit → `201 Created`, `CAP-2026-0337`, navigated to the detail page —
   confirmed every entered field rendered correctly in the Descriptions
   block, and the AI Insights panel correctly showed the "not evaluated
   yet" empty state (request still Draft) plus the server utilization
   table for `app01` (all `—`, expected per Phase 7b's placeholder-metric-
   names caveat).
4. Clicked **Submit Request** → status auto-cascaded live to **AiReviewed**
   (`draft→submitted→ai_evaluation→ai_reviewed`, all `Approved`/
   `InProgress` per the timeline) — confirmed the same real live Ollama
   call Phase 7b documented (this run: score 70, "challenge", flags
   `missing_utilization_data` / `inadequate_justification`). **Found the
   stale-query bug here** (section 2) — fixed, then reloaded and
   re-verified the panel shows the real score/recommendation/flags
   correctly.
5. Clicked **Revise** → transitioned `AiReviewed → Submitted`, re-cascaded
   through `ai_evaluation → ai_reviewed` again live, landing back on
   AiReviewed with a **second, genuinely different** live evaluation
   (score 80, "approve", no flags this time) — confirms both the Revise
   transition itself and that the AI Insights panel's fix correctly
   refetches and displays the newest evaluation each time.
6. Clicked **Logout** → returned to `/login`. Direct navigation to
   `/dashboard` afterward redirected back to `/login` — confirmed the
   session was actually cleared (`sessionStorage`), not just a UI-level
   redirect.
7. Killed the `dotnet run` API process (confirmed `Get-NetTCPConnection
   -LocalPort 5000` returns empty afterward) and stopped the `npm run dev`
   preview server before finishing.

## Files touched

- `web/src/pages/NewRequestPage.tsx` (rewritten — 5-step wizard)
- `web/src/pages/NewRequestPage.test.tsx` (new)
- `web/src/pages/RequestDetailPage.tsx` (AI Insights panel + query-
  invalidation fix)
- `web/src/pages/RequestDetailPage.test.tsx` (new)
- `web/src/layouts/AuthenticatedLayout.tsx` (Logout button)
- `web/src/layouts/AuthenticatedLayout.test.tsx` (new)

## Known follow-ups (not this task's scope)

- Real PromQL metric names for Grafana utilization — still placeholders
  (Phase 7b's follow-up, unchanged here); the AI Insights panel already
  handles the resulting all-`null` stats correctly, so this is a pure
  backend follow-up with no frontend work needed once it lands.
- No "Skip AI" manual-override UI for the `ai_evaluation` failure case —
  still out of scope (Phase 7b's follow-up, unchanged here).
- Email-on-transition — still not wired (Phase 7b's follow-up, unchanged
  here).
- The three pending business decisions (pilot team, go-live date, real AD
  group → role mapping) remain unresolved per `CLAUDE.md` — re-surfacing
  them here as instructed, since this closes out the phases feeding into
  Phase 6 (Polish).
