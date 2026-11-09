using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HockeySeason.Agents;

public static class HockeySeasonAgentsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <c>hockey-season-data.agent</c> translation agent as a
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
                var options = serviceProvider.GetRequiredService<IOptions<HockeySeasonDataAgentOptions>>().Value;
                var contentRoot = serviceProvider.GetRequiredService<IHostEnvironment>().ContentRootPath;
                var instructionsPath = Path.IsPathRooted(options.InstructionsPath)
                    ? options.InstructionsPath
                    : Path.Combine(contentRoot, options.InstructionsPath);

                var instructions = File.ReadAllText(instructionsPath);
                var chatClient = TranslationChatClientFactory.Create(options);

                return HockeySeasonDataAgentFactory.Create(chatClient, instructions, name);
            });
    }
}
