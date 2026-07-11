# Phase 4 — Integrations: status (complete)

Done:
- **Email (Mailtrap)**: `IEmailClient`/`MailtrapSmtpEmailClient` (MailKit
  SMTP) + `MockEmailClient`. Config-driven `Email:Provider` switch.
  `POST /api/v1/admin/test-email` diagnostic endpoint (Admin-only).
  MailKit pinned to 4.16.0 (4.9.0 had a STARTTLS response-injection
  vulnerability, GHSA-9j88-vvj5-vhgr). **Verified live**: real email sent
  successfully against a Mailtrap Email Testing sandbox inbox.
- **Grafana**: `IGrafanaClient`/`GrafanaClient` (queries the configured
  Prometheus datasource via `query_range`, spec Section 8) +
  `MockGrafanaClient`. Config-driven `Grafana:Provider` switch.
  `GET /api/v1/admin/test-grafana` diagnostic endpoint (Admin-only).
  **Verified live** against a real Grafana Cloud account.
- **AI adapter** (ADR 0002): `IAiEvaluationClient`/`OllamaAiEvaluationClient`
  (local Ollama, `format: "json"` structured output) + `MockAiEvaluationClient`.
  Config-driven `Ai:Provider` switch. Every evaluation attempt logged to a
  new `AiEvaluations` table (prompt, raw response, parsed score/
  recommendation/flags) regardless of success/failure - full audit trail
  for an approval-affecting decision. `POST /api/v1/admin/test-ai-evaluation`
  diagnostic endpoint (Admin-only). **Verified live** against real local
  Ollama (`qwen2.5-coder:7b`): real request evaluated, structured response
  parsed correctly, logged to the database.
- **Excel report** (ADR 0001): `IReportGenerator`/`ClosedXmlReportGenerator`
  produces a 3-sheet workbook (Request Summary, AI Evaluation Report,
  Approval Chain) via `GET /api/v1/requests/{id}/report`. Structural
  snapshot test (sheet names/count, key content present) rather than a
  fragile byte-for-byte comparison.
- **Outbox pattern**: `OutboxMessage` entity/table, `IOutboxWriter`
  (enqueues only, never delivers directly), `OutboxProcessor`
  (`BackgroundService` on a `PeriodicTimer`, `IServiceScopeFactory` per
  tick for proper `DbContext`/`IEmailClient` scoping, retries failed
  deliveries up to `MaxAttempts` before giving up permanently - no
  exponential backoff/dead-letter queue, proportional to Phase-1 scale).
  Test exercises real background processing end-to-end (enqueue → wait →
  verify delivery via the mock client's call-capture).
- Jira/HPSM: intentionally not built - interface + mock only was never
  needed since Phase 1's workflow scope never reaches that integration
  point (Phase 2+ per the spec).

Real bugs found and fixed while integrating 3 parallel subagents' work:
- **Migration collision avoided**: the outbox agent discovered mid-work
  that its migration generation would have bundled the AI adapter's
  `AiEvaluation` entity in too (both were present in `CapacityDbContext`
  at the time). It split this into two clean migrations
  (`AddAiEvaluations`, `AddOutboxMessages`) rather than leaving a tangled
  one.
- **Test-isolation bug, found via a genuinely flaky test**: `EmailEndpointTests`/
  `GrafanaEndpointTests`/`AiEndpointTests` originally forced `Provider=Mock`
  via `ConfigureAppConfiguration` (config-key override). This is NOT
  reliable - the Provider switch is read once during host build in each
  module's `Add*Module()` call, and config added via
  `ConfigureAppConfiguration` doesn't consistently win that race. Caught
  directly: the AI test flakily called the real local Ollama instead of
  the mock and got "challenge" instead of the expected mocked "approve".
  Fixed across all three test files by replacing the DI registration
  directly instead (`services.RemoveAll<T>()` + `AddSingleton<T, Mock...>()`
  in `ConfigureTestServices`) - deterministic regardless of config timing.

23/23 tests passing (stable across repeated runs).

Next: Phase 5 — Frontend (the actual wizard, approval queues, admin UI,
real login - currently all placeholder pages from Phase 2).
