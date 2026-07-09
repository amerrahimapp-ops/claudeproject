# ADR 0002: AI adapter — local Ollama, structured output, logged

## Status
Accepted

## Context
The design spec's AI module was vague: no model specifics, no defined
output contract, no observability. The spec already names Ollama as the dev
fallback (adapter pattern, swappable to a hosted/on-prem model later).

## Decision
- Dev/Phase-1 implementation calls a **local Ollama** instance over REST
  (`http://localhost:11434`), using `qwen2.5-coder:7b` as the default model
  (`qwen2.5-coder:3b` available as a faster fallback).
- The adapter enforces a **JSON-schema-constrained response contract**:
  `{ score: number, recommendation: "approve"|"challenge"|"reject", flags: string[] }`.
  Requests use Ollama's structured-output support (`format` field / JSON
  schema) rather than parsing freeform text.
- **Every prompt and response is logged** to a new `ai_evaluations` table
  (not just `audit_log`, since it needs full prompt/response bodies) for
  traceability of an approval-affecting decision.
- The adapter is packaged as a **Docker sidecar**, isolating
  Ollama-calling code and prompt templates from the .NET monolith.

## Rationale
- Structured output avoids brittle text parsing and makes the AI module
  testable (mocked HTTP responses in unit tests).
- Logging every prompt/response is the minimum bar for an approval-affecting
  AI feature to be auditable — the original spec had no equivalent.
- Local Ollama costs nothing and matches the spec's own stated dev fallback;
  the interface+mock pattern (matching Grafana/Email/Jira) means swapping to
  a hosted model later is a config change, not a rewrite.

## Consequences
- `IAiEvaluationClient` interface with `OllamaAiEvaluationClient` (real) and
  a mock implementation for tests/dev-without-Ollama.
- Phase 4 adds unit tests mocking the Ollama HTTP call and asserting on the
  response contract.
