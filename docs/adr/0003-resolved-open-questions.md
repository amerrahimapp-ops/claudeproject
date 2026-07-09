# ADR 0003: Resolved technical open questions

## Status
Accepted

## Context
The design spec (Section 24) left several open questions. The two business
questions (pilot team, go-live date) can't be resolved by agents and remain
tracked in `CLAUDE.md`. The AD group→role mapping is also a business/IT-admin
decision, tracked alongside them. This ADR resolves the remaining technical
ones so Phases 2-4 aren't blocked on "TBD".

## Decisions
- **Dev MySQL hostname**: `localhost`, documented in
  `appsettings.Development.json` (via `docker-compose.yml` from Phase 2).
- **Attachment retention**: confirmed at the spec's default of 90 days.
  No scheduled cleanup job is built in Phase 1 (out of proportion for an
  internal tool at this scale) — retention is enforced manually/documented
  in `docs/runbook.md` (Phase 6).
- **Grafana metrics pulled for AI evaluation**: pinned to CPU utilization %,
  memory utilization %, and disk utilization %, per hostname, averaged over
  the trailing 30 days (avg/max/p95) — matches the spec's Section 8 query
  detail, removing the "others?" ambiguity.
