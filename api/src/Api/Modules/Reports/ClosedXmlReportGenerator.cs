using Api.Data.Entities;
using ClosedXML.Excel;

namespace Api.Modules.Reports;

/// <summary>
/// ClosedXML-backed implementation (ADR 0001 — ClosedXML over EPPlus for
/// licensing reasons). Produces the 3-sheet workbook described in spec
/// Section 9: Request Summary, AI Evaluation Report, Approval Chain.
/// Formatting is deliberately plain (bold headers, auto-sized columns) per
/// the project's "functional first" design philosophy — no elaborate
/// styling.
/// </summary>
public class ClosedXmlReportGenerator : IReportGenerator
{
    public byte[] GenerateRequestReport(Request request)
    {
        using var workbook = new XLWorkbook();

        AddRequestSummarySheet(workbook, request);
        AddAiEvaluationSheet(workbook);
        AddApprovalChainSheet(workbook, request);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AddRequestSummarySheet(XLWorkbook workbook, Request request)
    {
        var sheet = workbook.Worksheets.Add("Request Summary");

        var rows = new (string Label, string Value)[]
        {
            ("Request Number", request.RequestNumber),
            ("Status", request.Status.ToString()),
            ("Environment", request.Environment.ToString()),
            ("Project Type", request.ProjectType.ToString()),
            ("Priority", request.Priority.ToString()),
            ("Requestor", request.RequestorUser?.DisplayName ?? $"User #{request.RequestorUserId}"),
            ("Created At", request.CreatedAt.ToString("u")),
            ("Updated At", request.UpdatedAt.ToString("u")),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var row = i + 1;
            var labelCell = sheet.Cell(row, 1);
            labelCell.Value = rows[i].Label;
            labelCell.Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = rows[i].Value;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void AddAiEvaluationSheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("AI Evaluation Report");

        SetHeaderRow(sheet, "Evaluated At", "Score", "Recommendation", "Flags");

        // TODO: populate from the `ai_evaluations` table once the AI adapter
        // module (Api.Modules.Ai — built in parallel, ADR 0002) exposes a
        // queryable evaluation history for a request. Left empty (aside from
        // a placeholder row) rather than blocked on that work landing first.
        sheet.Cell(2, 1).Value = "No AI evaluation data available";

        sheet.Columns().AdjustToContents();
    }

    private static void AddApprovalChainSheet(XLWorkbook workbook, Request request)
    {
        var sheet = workbook.Worksheets.Add("Approval Chain");

        SetHeaderRow(sheet, "Stage Name", "Status", "Assigned Role", "Started At", "Completed At", "Comments");

        var row = 2;
        foreach (var stage in request.WorkflowStages.OrderBy(ws => ws.StartedAt))
        {
            sheet.Cell(row, 1).Value = stage.StageName;
            sheet.Cell(row, 2).Value = stage.Status.ToString();
            sheet.Cell(row, 3).Value = stage.AssignedRole ?? string.Empty;

            if (stage.StartedAt.HasValue)
            {
                sheet.Cell(row, 4).Value = stage.StartedAt.Value;
            }

            if (stage.CompletedAt.HasValue)
            {
                sheet.Cell(row, 5).Value = stage.CompletedAt.Value;
            }

            sheet.Cell(row, 6).Value = stage.Comments ?? string.Empty;
            row++;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void SetHeaderRow(IXLWorksheet sheet, params string[] headers)
    {
        for (var col = 0; col < headers.Length; col++)
        {
            var cell = sheet.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
        }
    }
}
