using HockeySeason.Application;
using HockeySeason.Infrastructure;
using HockeySeason.LeagueApi.Extensions;
using HockeySeason.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// The agent resolves its MCP tools once, from the root provider: every
/// transitive dependency of <see cref="LeagueTools"/> must therefore be
/// resolvable as a singleton. This guards the composition root (a scoped
/// dependency here would fail with "cannot resolve scoped service from root
/// provider" at the first agent run).
/// </summary>
public sealed class AgentToolResolutionTests
{
    [Fact]
    public void LeagueTools_AndItsAgentFunctions_ResolveFromTheRootProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LeagueApi:ApiKey"] = "test",
                ["ConnectionStrings:Postgres"] = "Host=test",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddLeagueApi();
        services.AddHockeySeasonData(configuration);
        services.AddSingleton<LeagueTools>();
        services.AddSingleton<CallerIdentity>();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var tools = provider.GetRequiredService<LeagueTools>();
        var functions = LeagueAgentToolFactory.CreateFrom(tools);

        // One AIFunction per [McpServerTool] method: the six DB-backed
        // reference/detail tools and the three live Spordle-backed ones.
        Assert.NotEmpty(functions);
        Assert.Contains(functions, tool => tool.Name.Contains("GetAssociations", StringComparison.Ordinal));
        Assert.Contains(functions, tool => tool.Name.Contains("GetLeagues", StringComparison.Ordinal));
        Assert.Contains(functions, tool => tool.Name.Contains("GetLeagueStanding", StringComparison.Ordinal));
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "HockeySeason.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
