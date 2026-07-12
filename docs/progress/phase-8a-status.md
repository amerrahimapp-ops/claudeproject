# Phase 8a Status — Theme Toggle, Notification Preferences, User Profile Page

Built directly on `phase/8-remaining-gaps` (no isolated worktree — same
worktree-isolation workaround noted in Phase 7a/7b/7c). Confirmed the
working tree was clean at `fddd419 Phase 6: Polish (audit log, security
hardening, Playwright CI, runbook) (#6)` before starting. This is the first
of three sequenced Phase-8 workstreams; real email notifications and
queue-position/attachment-upload follow once this lands and is reviewed.

Per the task's explicit instruction, OpenCode+Ollama was not re-attempted
this round — three prior attempts (7a/7b/7c) already established it emits
broken tool-call JSON instead of reading/writing files. Everything below
was built directly.

## 1. API contract — `GET`/`PUT /api/v1/me/preferences`

No migration was needed: `UserPreference.NotificationPrefs` (json column)
and `.Theme` (enum, already `HasConversion<string>()` in
`CapacityDbContext.cs`) existed in the schema from an earlier phase but were
completely unused by the endpoint. This task only extends
`api/src/Api/Modules/Auth/MeEndpoints.cs`'s existing DTOs/handlers — no new
endpoint, no schema change.

### Response shape (`UserPreferencesResponse`)

```jsonc
{
  "defaultView": "Dashboard",      // unchanged from Phase 6
  "theme": "Dark",                 // "Light" | "Dark"
  "notificationPrefs": {
    "requestStatusChanged": true,
    "newAssignedTask": true
  }
}
```

### Request shape (`UpdateUserPreferencesRequest`)

```jsonc
{
  "defaultView": "Dashboard",       // required, unchanged validation
  "theme": "Light",                 // optional
  "notificationPrefs": {            // optional
    "requestStatusChanged": false,
    "newAssignedTask": true
  }
}
```

**`defaultView` stays required** (matches the pre-existing contract exactly
— no behavior change for old callers). **`theme` and `notificationPrefs`
are both optional** — a PUT that omits either field leaves the
corresponding stored value untouched (or falls back to Dark / all-true on
the very first PUT for a user, same defaults the row already had). This
was a deliberate design choice so the existing `AuthenticatedLayout.tsx`
dropdown (which only ever sent `{ defaultView }`) needed zero backend
changes to keep working, and so `ProfilePage.tsx`'s theme switch and
notification switches can each PUT independently without first reading
back and re-sending the other two fields blind — in practice the frontend
call sites do read the current cached preferences and pass all three
through anyway (see Section 2), but the backend doesn't require it.

**Validation**: `theme`, if present, must be `"Light"` or `"Dark"` (400
otherwise, same message style as the existing `defaultView` check).
`notificationPrefs`, if present, deserializes directly into the
`NotificationPreferences` record — no separate validation needed since it's
strongly typed (unknown JSON keys are ignored, missing keys default to
`true` per the record's default parameter values).

**`NotificationPreferences` is a fixed, small record** —
`RequestStatusChanged` and `NewAssignedTask`, both default `true` —
deliberately not a dynamic/keyed schema, per the task's explicit scope
instruction. **Phase 8b (real email notifications) should read this back
per-user before sending** to respect opt-outs: `false` on either key means
suppress that notification type for that user. Adding a third event type
later is a one-line change to the record (new `bool` param with a `true`
default) plus a frontend label entry in `ProfilePage.tsx`'s
`NOTIFICATION_PREF_LABELS` — no migration needed since it's stored as an
opaque JSON blob.

### Tests

`api/tests/Api.Tests/MePreferencesEndpointTests.cs` — added 4 new tests
alongside the existing 5:
- `GetPreferences_BeforeAnyPutHasHappened_DefaultsToDarkAndAllNotificationsOn`
- `PutThenGetPreferences_RoundTripsThemeAndNotificationPrefs` (also covers
  the "partial PUT with just `defaultView` leaves theme/notificationPrefs
  untouched" backward-compatibility case, and resets to defaults at the end
  for idempotency across reruns against the shared dev DB)
- `PutPreferences_WithUnknownTheme_ReturnsBadRequest`

## 2. Frontend theme mechanism

`web/src/theme/theme.ts` now exports `darkTheme` and `lightTheme` — both
share one `sharedTokens` object (accent `#1677ff`, `borderRadius: 2`,
`motion: false`, the same Layout/Button/Card component overrides) and differ
**only** in `algorithm` (`theme.darkAlgorithm` vs. `theme.defaultAlgorithm`).
No new colors, no redesign — matches the "anti-AI-slop" philosophy comment
already in the file. `appTheme` is kept as a `darkTheme` alias for
back-compat (nothing external referenced it besides `App.tsx`, which no
longer needs the alias, but it's harmless to leave).

**Wiring** (`web/src/App.tsx`): the AntD `ConfigProvider` used to wrap
`QueryClientProvider` (outside React Query entirely), which is incompatible
with needing to read the `['myPreferences']` query cache to pick a theme.
Restructured so `ConfigProvider` now lives *inside* `QueryClientProvider` +
`AuthProvider`, in a new `ThemedApp` component that runs its own
`useQuery(['myPreferences'], fetchMyPreferences)` — same queryKey and
queryFn as `AuthenticatedLayout.tsx`'s existing call, so React Query dedupes
the two subscriptions onto one shared cache entry rather than double-
fetching. `ThemedApp` picks `lightTheme` when `preferences?.theme ===
'Light'`, else `darkTheme` — **before login / before the preference has
loaded, `preferences` is `undefined` and the query is disabled
(`enabled: isAuthenticated`), so it falls through to `darkTheme`** — no
light flash before auth, confirmed live (see Verification).

`web/src/api/preferences.ts` gained `ThemePreference` and
`NotificationPreferences` types, and `UserPreferences` now carries all
three fields. `UpdateUserPreferences` is typed as `defaultView` required +
`theme`/`notificationPrefs` optional, matching the backend contract exactly
(`Pick<UserPreferences, 'defaultView'> & Partial<Omit<UserPreferences,
'defaultView'>>`). `updateMyPreferences`'s signature changed from
`(defaultView: DefaultView)` to `(update: UpdateUserPreferences)` — the one
existing call site (`AuthenticatedLayout.tsx`'s landing-page `Select`) was
updated to `updatePreference.mutate({ defaultView: value })`.

## 3. User Profile page

New route `/profile` → `web/src/pages/ProfilePage.tsx`, added to
`web/src/routes/AppRoutes.tsx` inside the existing `AuthenticatedLayout`
route group (no `RequireRole` wrapper — every authenticated role can view
their own profile). Linked from the header in
`web/src/layouts/AuthenticatedLayout.tsx`, next to the existing Logout
button (a small "Profile" button with `UserOutlined` icon, not a new
sidebar section — proportional to a single page, per the task's guidance).

Page contents:
- **Account** card: Name / Email / Role, read-only, straight from
  `useAuth()`. No editable contact fields — consistent with Phase 7a's
  "Requestor Info decision" (`docs/progress/phase-7a-status.md` section 3)
  not to add PF/Contact/Department to `User`; nothing new invented here.
- **Appearance** card: one `Switch` (Dark/Light), reusing the
  `['myPreferences']` query. Toggling calls `updateMyPreferences` with the
  current `defaultView`/`notificationPrefs` plus the new `theme`, then
  writes the mutation result straight into the shared query cache via
  `queryClient.setQueryData(['myPreferences'], updated)` — this is what
  makes `App.tsx`'s `ThemedApp` re-render with the new algorithm
  immediately, with no extra plumbing between the two components.
- **Notifications** card: two `Switch`es, one per
  `NotificationPreferences` key, same update-then-cache-write pattern.

## 4. Verification performed

- `dotnet build` — 0 warnings, 0 errors (after killing a stray leftover
  `Api.exe` process (PID 10748) from a prior session that was holding a
  file lock on `Api.Data.dll` and briefly failed the build with MSB3027 —
  unrelated to this change, just a leftover process).
- `dotnet test` (MySQL via existing `docker compose` container, already
  running/healthy) — **47/47 passed**, including the 4 new
  `MePreferencesEndpointTests` cases.
- `npm run build` — clean production build (pre-existing >500kB single-
  chunk warning, unrelated to this phase).
- `npm run lint` — clean.
- `npm run test` — **15/15 passed** across all 7 existing test files
  (`AuthenticatedLayout.test.tsx` needed no changes — it mocks
  `{ defaultView: 'Dashboard' }` and doesn't touch theme/notificationPrefs).
- Live browser verification, end-to-end (API on `http://localhost:5000`
  via `dotnet run --urls http://localhost:5000`, web dev server via
  `preview_start` on port 5173 — had to kill a stray leftover `node.exe`
  (PID 21668) already squatting on 5173 from a prior session first):
  1. Logged in as `requestor.dev`. Confirmed dark theme by default
     (`.ant-layout` background `rgb(0, 0, 0)`).
  2. Opened **Profile** via the new header link — Account/Appearance/
     Notifications cards all rendered correctly.
  3. Toggled the theme switch to **Light** — `.ant-layout` background
     changed to `rgb(245, 245, 245)` **immediately**, confirmed via the
     network tab that the `PUT /api/v1/me/preferences` fired and returned
     `200`.
  4. **Refreshed the page** — theme was still **Light** after reload
     (fetched fresh from `GET /api/v1/me/preferences`, not just client
     state).
  5. Toggled **"My request status changed"** off — confirmed `PUT` fired,
     switch state updated.
  6. **Refreshed again** — the notification pref was still **off**, and
     theme was still **Light** (both independently persisted).
  7. Toggled theme back to **Dark** — background reverted to
     `rgb(0, 0, 0)` immediately; **refreshed once more** — still Dark.
  8. Reset the notification switch back to on, confirmed reset, then
     cleared `sessionStorage` and reloaded `/login` — confirmed dark theme
     (login card background `rgb(20, 20, 20)`, AntD's dark component
     background) is still the default **before** any login/preference
     fetch happens, i.e. no light-mode flash pre-auth.
  9. Left `requestor.dev`'s stored preferences reset to defaults (Dark,
     both notifications on) at the end, matching how the backend's own
     round-trip test resets itself — this is a shared dev DB.
- Killed the `dotnet run` API process and stopped the `preview_start` web
  server before finishing. Verified no process is listening on 5000 or
  5173 afterward. The pre-existing `claudeproject-mysql-1` Docker container
  (already running/healthy before this task started) was left running, as
  it wasn't started by this task.

## Known follow-ups (not this task's scope)

- Phase 8b (real email notifications) needs to read
  `UserPreference.NotificationPrefs` (via the same
  `ParseNotificationPrefs`-style deserialization, or directly) before
  sending each notification type, to respect the opt-outs recorded here.
- A stray `Api.exe` and a stray `node.exe` (Vite) process were both found
  already running at the start of this task, left over from some earlier
  session — worth a reminder in whatever runbook/checklist covers "starting
  a new phase" to check for and kill leftover dev processes before running
  `dotnet build`/`preview_start`, since both silently caused otherwise
  confusing failures (a file-lock build error, and a "port already in use"
  preview_start error) that had nothing to do with this phase's actual
  code changes.
