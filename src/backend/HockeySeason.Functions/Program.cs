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
    // Local development only: reuse the Api project's user secrets (PIN,
    // signing key, Ollama key) so both hosts issue/validate identical tokens.
    builder.Configuration.AddUserSecrets<LoginFunction>(optional: true);
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

// OIDC-style JWT security (same shared implementation as the minimal API):
// login with the shared 6-digit access code issues an HS256 bearer token that
// the Function endpoints validate. The startup filter inserts
// UseAuthentication/UseAuthorization ahead of the Functions routing
// middleware because the integration does not add them itself.
builder.Services.AddHockeySeasonAuth(builder.Configuration);
builder.Services.AddTransient<IStartupFilter, UseAuthenticationStartupFilter>();

builder.Build().Run();
