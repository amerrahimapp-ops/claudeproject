# ADR 0001: Excel generation library — ClosedXML

## Status
Accepted

## Context
The design spec left the Excel library choice unresolved ("ClosedXML or
EPPlus") for generating the Phase-1 report matching the legacy
`Capacity_Request_Form_V2.xlsx` template (Sheet 1 Request Summary, Sheet 2
AI Evaluation Report, Sheet 3 Approval Chain).

## Decision
Use **ClosedXML**.

## Rationale
- EPPlus requires a commercial license for commercial use as of v5+
  (Polyform Noncommercial / paid license).
- ClosedXML is MIT-licensed and free for any use.
- This is an internal enterprise tool — no reason to take on a licensing
  dependency when a free, actively maintained alternative covers the same
  needs (cell styling, formulas, multi-sheet workbooks).

## Consequences
- The Reports module's Excel generator (Phase 4) is built against
  ClosedXML's API.
- An automated snapshot test (Phase 4) pins the generated workbook structure
  so future changes don't silently drift from the legacy V2 template.
