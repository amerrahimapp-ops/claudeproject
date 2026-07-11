using Api.Data;
using Api.Modules.Admin;
using Api.Modules.Ai;
using Api.Modules.Auth;
using Api.Modules.Integrations;
using Api.Modules.Integrations.Email;
using Api.Modules.Integrations.Grafana;
using Api.Modules.Notifications;
using Api.Modules.Reports;
using Api.Modules.Requests;
using Api.Modules.Workflow;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddAiModule();
builder.Services.AddAdminModule();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapRequestsEndpoints();
app.MapWorkflowEndpoints();
app.MapEmailEndpoints();
app.MapGrafanaEndpoints();

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

// Thin placeholder so the Foundation-phase integration test can exercise
// the full EF Core + API + MySQL pipeline. Real Requests module logic
// (filtering, DTOs, request-number generation, etc.) lands in a later phase.
app.MapGet("/api/v1/requests", async (CapacityDbContext db) =>
{
    var requests = await db.Requests.AsNoTracking().ToListAsync();
    return Results.Ok(requests);
})
.WithName("GetRequests")
.RequireAuthorization();

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
