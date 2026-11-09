using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HockeySeason.Agents;

/// <summary>
/// Builds the <c>hockey-season-data.agent</c> agent with Microsoft Agent
/// Framework. The agent answers questions about hockey league data by calling
/// the league MCP tools. Its behavior is driven by the instructions loaded
/// from <c>Agents/hockey-season-data.agent.md</c> and by the tools passed at
/// construction time, so both can be changed without touching code.
/// </summary>
public static class HockeySeasonDataAgentFactory
{
    public const string DefaultName = "hockey-season-data.agent";

    public static ChatClientAgent Create(
        Microsoft.Extensions.AI.IChatClient chatClient,
        string instructions,
        string name = DefaultName,
        System.Collections.Generic.IList<Microsoft.Extensions.AI.AITool>? tools = null) =>
        new(
            chatClient,
            instructions,
            name,
            description: "Answers questions about hockey league data (leagues, standings, teams, games, suspensions).",
            tools);
}
