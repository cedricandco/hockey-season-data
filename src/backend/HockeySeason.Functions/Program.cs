using HockeySeason.Agents;
using HockeySeason.Application;
using HockeySeason.Domain;
using HockeySeason.Infrastructure;
using HockeySeason.LeagueApi.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton<ITimeProvider, UtcTimeProvider>();
builder.Services.AddSingleton<IHealthService, HealthService>();
builder.Services.AddLeagueApi();
builder.Services.AddAGUIServer();
builder.Services.AddHockeySeasonDataAgent(builder.Configuration);
builder.Services.AddSingleton<AIAgent>(services =>
    HockeySeasonAgentsServiceCollectionExtensions.CreateAgent(services, HockeySeasonDataAgentFactory.DefaultName));

builder.Build().Run();
