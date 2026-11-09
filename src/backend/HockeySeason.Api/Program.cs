using HockeySeason.Agents;
using HockeySeason.Application;
using HockeySeason.Auth;
using HockeySeason.Domain;
using HockeySeason.Infrastructure;
using HockeySeason.LeagueApi.Extensions;
using HockeySeason.LeagueApi;
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

// OIDC-style JWT security: login with the shared 6-digit access code from
// configuration, HS256 tokens, JwtBearer validation and rate limiting.
builder.Services.AddHockeySeasonAuth(builder.Configuration);

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
app.UseAuthentication();
app.UseAuthorization();

// Liveness and readiness probes (anonymous, no redirects).
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/readyz", () => Results.Ok(new { status = "ready" }));

// Login endpoint: exchanges the 6-digit access code for a signed JWT.
app.MapHockeySeasonLogin(app.Services.GetRequiredService<LoginRateGuard>());

// Health report consumed by the frontend "Ping API" button.
app.MapGet("/api/health", (IHealthService healthService) =>
{
    var time = healthService.GetServerTime();
    return Results.Ok(new HealthReport(time, app.Environment.EnvironmentName));
}).RequireAuthorization(HockeySeasonAuthExtensions.AuthenticatedPolicy);

app.MapGet("/api/weekly-report", async (HttpRequest httpRequest, IWeeklyReportService reports, CancellationToken cancellationToken) =>
{
    var query = httpRequest.Query;
    if (!WeeklyReportRequest.TryParse(query["startDate"], query["endDate"], query["divisionId"], query["teamId"], out var request))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Provide valid yyyy-MM-dd dates and positive divisionId and teamId parameters (at most 20 each)."] });
    return Results.Ok(await reports.GetAsync(request!, cancellationToken));
})
    .WithName("GetWeeklyReport")
    .WithSummary("Retrieve raw Spordle data for a weekly report.")
    .RequireAuthorization(HockeySeasonAuthExtensions.AuthenticatedPolicy);

// Streamable HTTP MCP endpoint.
app.MapMcp("/mcp").RequireAuthorization(HockeySeasonAuthExtensions.AuthenticatedPolicy);

// AG-UI endpoint consumed by the CopilotKit frontend (proxied through /api).
app.MapAGUIServer(hockeyAgentBuilder, "/api/agui/hockey-season-data.agent")
    .RequireAuthorization(HockeySeasonAuthExtensions.AuthenticatedPolicy);

app.MapFallback(() => Results.NotFound(new { status = "not found" }));

app.Run();
