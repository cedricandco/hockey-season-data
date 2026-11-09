using HockeySeason.Application;
using HockeySeason.Domain;
using HockeySeason.Infrastructure;
using HockeySeason.Mcp;
using ModelContextProtocol.Protocol;
var builder = WebApplication.CreateBuilder(args);

// Bind to PORT (injected by Azure App Service / hosting environments), default 5000.
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://*:{port}");

// Clean Architecture composition root: infrastructure adapters and
// application use cases are wired here only (Dependency Inversion).
builder.Services.AddSingleton<ITimeProvider, UtcTimeProvider>();
builder.Services.AddSingleton<IHealthService, HealthService>();

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
    .WithTools<TimeTool>();

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

app.MapFallback(() => Results.NotFound(new { status = "not found" }));

app.Run();
