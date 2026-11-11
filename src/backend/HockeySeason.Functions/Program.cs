using HockeySeason.Agents;
using HockeySeason.Application;
using HockeySeason.Domain;
using HockeySeason.Functions;
using HockeySeason.Infrastructure;
using HockeySeason.LeagueApi.Extensions;
using HockeySeason.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

// Application Insights: when APPLICATIONINSIGHTS_CONNECTION_STRING is set
// (Static Web Apps managed functions / Azure Function App), ILogger output —
// including the JwtValidator and JwtBearer diagnostics — is exported to the
// "hockey-season-data" component. Without the variable it is a no-op, so
// local development keeps console-only logging.
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

if (builder.Environment.IsDevelopment())
{
    // Local development only: reuse the Api project's user secrets (OIDC
    // authority overrides, Ollama key) so both hosts share configuration.
    builder.Configuration.AddUserSecrets<HealthFunction>(optional: true);
}

builder.Services.AddSingleton<ITimeProvider, UtcTimeProvider>();
builder.Services.AddSingleton<IHealthService, HealthService>();
builder.Services.AddLeagueApi();
// LeagueTools must be registered explicitly: the MCP endpoint instantiates it
// on demand, but the agent resolves its AIFunctions from this registration.
builder.Services.AddSingleton<LeagueTools>();
builder.Services.AddAGUIServer();
builder.Services.AddHockeySeasonDataAgent(builder.Configuration);
builder.Services.AddSingleton<AIAgent>(services =>
    HockeySeasonAgentsServiceCollectionExtensions.CreateAgent(services, HockeySeasonDataAgentFactory.DefaultName));

// JWT security: triggers enforce authentication through FunctionsAuthorization
// (direct JWT validation of the Authorization header by HockeySeason.Auth).
// The ASP.NET Core JWT bearer middleware is intentionally NOT wired here: on
// the Static Web Apps managed host the middleware short-circuits failed
// authentication before the Functions coordinator handshake completes, which
// held every request for the 15s FunctionStartTimeout and forced 401s with
// "Timed out waiting for the function start call" exceptions. The Api host
// keeps the middleware + fallback policy; this host is trigger-validated only.
builder.Services.AddOptions<HockeySeason.Auth.AuthOptions>()
    .Bind(builder.Configuration.GetSection(HockeySeason.Auth.AuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<HockeySeason.Auth.IJwtValidator, HockeySeason.Auth.JwtValidator>();

builder.Build().Run();
