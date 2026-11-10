using HockeySeason.Agents;
using HockeySeason.Application;
using HockeySeason.Auth;
using HockeySeason.Domain;
using HockeySeason.Functions;
using HockeySeason.Infrastructure;
using HockeySeason.LeagueApi.Extensions;
using HockeySeason.Mcp;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

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

// JWT security backed by Auth0 (OIDC), same shared implementation as the
// minimal API: RS256 access tokens validated against the authority metadata.
// The startup filter inserts UseAuthentication/UseAuthorization ahead of the
// Functions routing middleware because the integration does not add them
// itself.
builder.Services.AddHockeySeasonAuth(builder.Configuration);
builder.Services.AddTransient<IStartupFilter, UseAuthenticationStartupFilter>();

builder.Build().Run();
