using System.Security.Claims;
using Api.Data;
using Api.Modules.Admin;
using Api.Modules.Ai;
using Api.Modules.Auth;
using Api.Modules.Integrations;
using Api.Modules.Integrations.Email;
using Api.Modules.Integrations.Grafana;
using Api.Modules.Integrations.Outbox;
using Api.Modules.Notifications;
using Api.Modules.Reports;
using Api.Modules.Requests;
using Api.Modules.Workflow;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- CORS (dev only: the Vite dev server on :5173 calling this API on :5000
// is itself cross-origin) ---
const string DevCorsPolicy = "DevCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// --- Logging (Serilog console sink) ---
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .Enrich.FromLogContext()
    .CreateLogger();
builder.Host.UseSerilog();

// --- Swagger / OpenAPI ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- EF Core / MySQL ---
var connectionString = builder.Configuration.GetConnectionString("CapacityDb")
    ?? throw new InvalidOperationException("ConnectionStrings:CapacityDb is not configured.");
builder.Services.AddDbContext<CapacityDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// --- Modular monolith wiring: one registration call per module ---
builder.Services.AddRequestsModule();
builder.Services.AddWorkflowModule();
builder.Services.AddIntegrationsModule(builder.Configuration);
builder.Services.AddNotificationsModule();
builder.Services.AddAuthModule(builder.Configuration, builder.Environment);
builder.Services.AddReportsModule();
builder.Services.AddAiModule(builder.Configuration);
builder.Services.AddAdminModule();
builder.Services.AddOutboxModule();

var app = builder.Build();

// --- Dev-only: apply migrations + seed reference data ---
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();
    await db.Database.MigrateAsync();
    await DbInitializer.SeedAsync(db);
}

// --- HTTP request pipeline ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCorsPolicy);
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapRequestsEndpoints();
app.MapWorkflowEndpoints();
app.MapEmailEndpoints();
app.MapGrafanaEndpoints();
app.MapReportsEndpoints();
app.MapAiEndpoints();
app.MapAiInsightsEndpoints();
app.MapAdminEndpoints();

app.MapGet("/health", async (CapacityDbContext db) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        return canConnect
            ? Results.Ok(new { status = "healthy", database = "connected" })
            : Results.Json(new { status = "unhealthy", database = "unreachable" }, statusCode: 503);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "unhealthy", database = "error", detail = ex.Message }, statusCode: 503);
    }
})
.WithName("HealthCheck");

// List endpoint — row-level security per spec 6.2: a Requestor sees only
// their own requests; CapacityManager/InfraHead/Admin see every request
// (they need the full queue to review/approve). Mapped through
// RequestMapper for the same string-enum shape GET /api/v1/requests/{id}
// already uses.
app.MapGet("/api/v1/requests", async (ClaimsPrincipal user, CapacityDbContext db) =>
{
    var query = db.Requests
        .Include(r => r.RequestorUser)
        .Include(r => r.WorkflowStages)
        .AsNoTracking();

    if (user.IsInRole("Requestor"))
    {
        var actingUserId = int.Parse(user.FindFirstValue("user_id")!);
        query = query.Where(r => r.RequestorUserId == actingUserId);
    }

    var requests = await query.ToListAsync();
    return Results.Ok(requests.Select(r => RequestMapper.ToResponse(r)));
})
.WithName("GetRequests")
.RequireAuthorization();

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
