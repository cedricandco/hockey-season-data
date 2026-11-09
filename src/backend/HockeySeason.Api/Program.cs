using HockeySeason.Agents;
using HockeySeason.Application;
using HockeySeason.Domain;
using HockeySeason.Infrastructure;
using HockeySeason.LeagueApi.Extensions;
using HockeySeason.Mcp;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using ModelContextProtocol.Protocol;
var builder = WebApplication.CreateBuilder(args);

// Respect Aspire's URL configuration while retaining PORT for hosted deployments.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://*:{port}");
}
else if (string.IsNullOrEmpty(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls("http://*:5000");
}

// Clean Architecture composition root: infrastructure adapters and
// application use cases are wired here only (Dependency Inversion).
builder.Services.AddSingleton<ITimeProvider, UtcTimeProvider>();
builder.Services.AddSingleton<IHealthService, HealthService>();
builder.Services.AddLeagueApi();

// LeagueTools must be registered explicitly: WithTools<LeagueTools>() exposes
// the [McpServerTool] methods over /mcp but does not add the instance to DI,
// and the agent resolves its AIFunctions from this registration.
builder.Services.AddSingleton<LeagueTools>();

// CORS: permissive in development so local frontends can call the API.
const string CorsPolicy = "AllowFrontend";
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
    policy.SetIsOriginAllowed(_ => builder.Environment.IsDevelopment())
          .AllowAnyHeader()
          .AllowAnyMethod()));

// MCP server (Streamable HTTP) with tools from the HockeySeason.Mcp project.
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "HockeySeasonMcp", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithTools<TimeTool>()
    .WithTools<LeagueTools>();

// Microsoft Agent Framework: AG-UI server support plus the
// "hockey-season-data.agent" agent (instructions loaded from
// Agents/hockey-season-data.agent.md, model served through an Ollama-compatible API,
// tools resolved from the HockeySeason.Mcp league tools).
builder.Services.AddAGUIServer();
var hockeyAgentBuilder = builder.Services.AddHockeySeasonDataAgent(builder.Configuration);

var app = builder.Build();

app.UseCors(CorsPolicy);

// Liveness and readiness probes (anonymous, no redirects).
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/readyz", () => Results.Ok(new { status = "ready" }));

// Health report consumed by the frontend "Ping API" button.
app.MapGet("/api/health", (IHealthService healthService) =>
{
    var time = healthService.GetServerTime();
    return Results.Ok(new HealthReport(time, app.Environment.EnvironmentName));
});

// Streamable HTTP MCP endpoint.
app.MapMcp("/mcp");

// AG-UI endpoint consumed by the CopilotKit frontend (proxied through /api).
app.MapAGUIServer(hockeyAgentBuilder, "/api/agui/hockey-season-data.agent");

app.MapFallback(() => Results.NotFound(new { status = "not found" }));

app.Run();
