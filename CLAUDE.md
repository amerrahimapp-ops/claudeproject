# Project Alpha — Capacity Request Management System

Implementation of the design spec at
`C:\Users\User\Documents\Github_Workspace\capacity-request-system\docs\superpowers\specs\2026-07-08-capacity-request-system-design.md`
(treat as the source of truth for entities, workflow, and modules — don't
re-derive it from conversation; re-read only if a genuinely new question
comes up that isn't already answered here or in an ADR).

## Phase 1 scope boundary

Workflow: `draft → submitted → ai_evaluation → ai_reviewed → capacity_review
→ infra_approval → done` (Excel generated). Stages 8-9 of the full 9-stage
flow are Phase 2+ — do not build them now.

Roles: Requestor, Capacity Manager, Infra Head, Admin. (Group Capacity,
Group Capacity Head, HOD, Group Infra Fulfillment are Phase 2+.)

## Tech stack

- Frontend: React 18 + TypeScript + Vite + Ant Design 5, React Router 6,
  React Query + Context (no Redux). Dense/functional UI, dark mode, no
  animations.
- Backend: .NET 8/9 Web API, modular monolith (Requests, Workflow Engine,
  Integrations, Notifications, Auth, Reports, AI, Admin), EF Core.
- DB: MySQL.
- Excel: **ClosedXML** (not EPPlus — EPPlus requires a commercial license
  from v5+, ClosedXML is MIT/free). See `docs/adr/0001-excel-library.md`.
- AI adapter: local Ollama, REST, JSON-schema-constrained structured output
  (`score`, `recommendation`, `flags[]`), every prompt+response logged to an
  `ai_evaluations` table. Containerized as a Docker sidecar. See
  `docs/adr/0002-ai-adapter.md`.
- Auth: `IIdentityProvider` interface — `MockIdentityProvider` for local dev
  now, a real `AdIdentityProvider` deferred to Phase 2+ (needs real AD
  endpoint/group names from the user). Same interface+mock pattern as
  Grafana/Email/Jira.

## Repo structure

(filled in once Phase 2 — Foundation — lands)

## Build / test / run

(filled in once Phase 2 scaffolds `api/` and `web/` — will include
`docker-compose.yml` for local MySQL, `dotnet build`/`dotnet test`,
`npm run build`/`npm test`, Playwright commands)

## Conventions

Defer to `~/.claude/CLAUDE.md` for commit message style and branch naming —
don't restate here. Package manager per project: check the lockfile before
assuming npm vs pnpm (this repo uses npm — see `web/package-lock.json` once
it exists).

Local dev config switch: a single `"Provider": "Mock"|"Ollama"|"Production"`
value per integration in `appsettings.Development.json` (see spec Section
10). Never commit real credentials — local secrets go in `dotnet
user-secrets` / a git-ignored `.env`; CI secrets go in GitHub Actions
encrypted secrets. The CI secrets-scan step enforces this.

**Any change to `.github/workflows/*.yml` must be validated with
`actionlint` before pushing** — generic YAML parsers (e.g. PyYAML) only
check syntax and will happily pass a file that's semantically invalid per
GitHub's own schema (e.g. `hashFiles()` used in a job-level `if:`, which
GitHub rejects at parse time with zero jobs and a near-useless "workflow
file issue" error that doesn't surface via the API, only the web UI — this
cost real time to diagnose once already, see `docs/progress/phase-3-status.md`).
Run via Docker, no local install needed:
```
docker run --rm -v "<repo-path>:/repo" -w /repo rhysd/actionlint:latest -color
```
Exit code 0 = clean.

## Never delegate to OpenCode/Ollama

Workflow engine state machine, security/RBAC/auth code, AI adapter design,
and the `/code-review` + `/verify` gates themselves. These stay on Claude
Code regardless of usage/quota state. Everything else (CRUD boilerplate,
DTOs, presentational components, test stubs, docs) is fair game to delegate
— see the orchestration plan for the hand-off procedure.

## Token usage — switch to OpenCode/Ollama proactively, not reactively

Do not wait for a hard rate-limit error before delegating — by then a task
is already cut off mid-way with nothing queued. Instead:
- Delegate eligible tasks to OpenCode+Ollama **as soon as they're
  identified as delegatable**, not held back as a pure emergency fallback.
- Treat a context compaction, a low-usage indicator, or the user saying
  usage is running low as the hand-off trigger — wrap up the current unit
  of work, commit/push, then shift remaining pending subtasks to
  OpenCode+Ollama before doing more Claude-side work.
- Only treat an actual rate-limit error as the trigger if no earlier
  warning was available.

## Task tracking

Run `TaskList` before doing anything — don't re-derive phase context from
conversation. Every PR names the exact `TaskUpdate` subtask IDs it closes.
A short `docs/progress/phase-N-status.md` note gets updated after each
subagent finishes, not just at phase end.

## Pending business decisions (not blocking the build)

- Pilot team identification — unresolved.
- Go-live date — unresolved.
- Real AD group → role mapping — unresolved, needed before the
  `AdIdentityProvider` can be wired to production.

Re-surface these to the user at the end of Phase 6 (Polish) — they gate
rollout, not code.

## Docs

- `docs/adr/` — architecture decision records (Excel library, AI adapter,
  etc.)
- `docs/progress/` — per-phase status notes for session resumption
- `docs/runbook.md` — deploy steps, health-check usage, backup/restore,
  rollback plan (created in Phase 6, required by the spec's Definition of
  Done)
