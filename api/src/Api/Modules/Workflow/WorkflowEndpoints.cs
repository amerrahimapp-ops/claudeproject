using System.Security.Claims;
using Api.Data.Entities;
using Api.Modules.Requests;

namespace Api.Modules.Workflow;

public record TransitionRequest(string TargetStage, string? Comments);

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/requests/{id:int}/transition", async (
            int id,
            TransitionRequest body,
            ClaimsPrincipal user,
            IWorkflowEngine workflowEngine) =>
        {
            var actingUserId = int.Parse(user.FindFirstValue("user_id")!);
            var actingUserRole = Enum.Parse<UserRole>(user.FindFirstValue(ClaimTypes.Role)!);

            var result = await workflowEngine.TransitionAsync(
                id, body.TargetStage, actingUserId, actingUserRole, body.Comments);

            return result.Outcome switch
            {
                WorkflowTransitionOutcome.Success => Results.Ok(RequestMapper.ToResponse(result.Request!)),
                WorkflowTransitionOutcome.RequestNotFound => Results.NotFound(),
                WorkflowTransitionOutcome.IllegalTransition => Results.Conflict(new { error = result.FailureReason }),
                WorkflowTransitionOutcome.WrongRole =>
                    Results.Json(new { error = result.FailureReason }, statusCode: StatusCodes.Status403Forbidden),
                _ => Results.Problem(),
            };
        })
        .WithName("TransitionRequestWorkflow")
        .RequireAuthorization();

        return app;
    }
}
