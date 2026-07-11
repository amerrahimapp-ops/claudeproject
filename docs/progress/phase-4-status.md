# Phase 4 — Integrations: status

Done:
- **Email (Mailtrap)**: `IEmailClient`/`MailtrapSmtpEmailClient` (MailKit
  SMTP) + `MockEmailClient`. Config-driven `Email:Provider` switch.
  `POST /api/v1/admin/test-email` diagnostic endpoint (Admin-only).
  MailKit pinned to 4.16.0 (4.9.0 had a STARTTLS response-injection
  vulnerability, GHSA-9j88-vvj5-vhgr, directly relevant since this client
  uses StartTls). **Verified live**: real email sent successfully against
  a Mailtrap Email Testing sandbox inbox.
- **Grafana**: `IGrafanaClient`/`GrafanaClient` (queries the configured
  Prometheus datasource via `query_range`, spec Section 8) +
  `MockGrafanaClient`. Config-driven `Grafana:Provider` switch.
  `GET /api/v1/admin/test-grafana` diagnostic endpoint (Admin-only).
  **Verified live**: real query executed successfully against a Grafana
  Cloud account, correct datasource id discovered and confirmed working.
- Fixed a test-isolation bug along the way: `EmailEndpointTests`/
  `GrafanaEndpointTests` now force `Provider=Mock` via in-memory config
  override in the test host, so they always exercise the mock path
  regardless of what a developer's local `appsettings.Development.json`
  has configured — otherwise these tests would have silently started
  hitting live services once real credentials were added locally.
- 17/17 tests passing throughout.

Remaining Phase 4 scope:
- AI adapter (Ollama, structured output, `ai_evaluations` logging — ADR 0002)
- Excel report (ClosedXML — ADR 0001)
- Outbox pattern for async integration delivery
- Jira/HPSM interface + mock stub (no real client needed, Phase 1 workflow
  never reaches that stage)

Next after that: Phase 5 — Frontend.
