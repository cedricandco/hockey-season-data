using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HockeySeason.Agents;

public static class HockeySeasonAgentsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <c>hockey-season-data.agent</c> Hockey Assistant as a
    /// keyed hosted agent so it can be exposed over AG-UI with
    /// <c>MapAGUIServer</c>. Instructions are loaded from the markdown file
    /// referenced by <see cref="HockeySeasonDataAgentOptions.InstructionsPath"/>.
    /// </summary>
    public static IHostedAgentBuilder AddHockeySeasonDataAgent(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<HockeySeasonDataAgentOptions>()
            .Bind(configuration.GetSection(HockeySeasonDataAgentOptions.SectionName))
            .ValidateDataAnnotations();

        return services.AddAIAgent(
            HockeySeasonDataAgentFactory.DefaultName,
            (serviceProvider, name) =>
            {
                return CreateAgent(serviceProvider, name);
            });
    }

    public static Microsoft.Agents.AI.ChatClientAgent CreateAgent(IServiceProvider serviceProvider, string name)
    {
        var options = serviceProvider.GetRequiredService<IOptions<HockeySeasonDataAgentOptions>>().Value;
        var contentRoot = serviceProvider.GetRequiredService<IHostEnvironment>().ContentRootPath;
        var instructionsPath = Path.IsPathRooted(options.InstructionsPath)
            ? options.InstructionsPath
            : Path.Combine(contentRoot, options.InstructionsPath);
        if (!Path.IsPathRooted(options.InstructionsPath) && !File.Exists(instructionsPath))
        {
            instructionsPath = Path.Combine(AppContext.BaseDirectory, options.InstructionsPath);
        }

        var instructions = File.ReadAllText(instructionsPath);
        var chatClient = HockeyAssistantChatClientFactory.Create(options);
        var tools = ResolveAgentTools(serviceProvider);
        return HockeySeasonDataAgentFactory.Create(chatClient, instructions, name, tools);
    }

    /// <summary>
    /// Resolves the MCP tools the agent may call. Registered as
    /// <see cref="AITool"/> functions from <see cref="HockeySeason.Mcp.LeagueTools"/>,
    /// resolved lazily so the agent still builds when the league API is not
    /// registered (for example, in tests).
    /// </summary>
    private static System.Collections.Generic.IList<Microsoft.Extensions.AI.AITool>? ResolveAgentTools(IServiceProvider serviceProvider)
    {
        var tools = serviceProvider.GetService<HockeySeason.Mcp.LeagueTools>();
        if (tools is null)
        {
            return null;
        }

        var functions = HockeySeason.Mcp.LeagueAgentToolFactory.CreateFrom(tools);
        return functions.Count == 0 ? null : functions;
    }
}
