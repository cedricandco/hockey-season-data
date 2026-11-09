using Microsoft.Agents.AI;

namespace HockeySeason.Agents;

/// <summary>
/// Builds the <c>hockey-season-data.agent</c> translation agent with
/// Microsoft Agent Framework. The agent has a single responsibility: translate
/// the user's input message into English. Its behavior is driven entirely by
/// the instructions loaded from <c>Agents/hockey-season-data.agent.md</c>, so
/// the prompt can be edited without touching code.
/// </summary>
public static class HockeySeasonDataAgentFactory
{
    public const string DefaultName = "hockey-season-data.agent";

    public static ChatClientAgent Create(
        Microsoft.Extensions.AI.IChatClient chatClient,
        string instructions,
        string name = DefaultName) =>
        new(
            chatClient,
            instructions,
            name,
            description: "Translates the user's input message into English.");
}
