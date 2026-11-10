namespace HockeySeason.Agents;

/// <summary>
/// Settings for the <c>hockey-season-data.agent</c> Hockey Assistant.
/// Bound from configuration section "Agents:HockeySeasonData".
/// </summary>
public sealed class HockeySeasonDataAgentOptions
{
    public const string SectionName = "Agents:HockeySeasonData";

    /// <summary>Logical agent name exposed over AG-UI. Must match the frontend agent id.</summary>
    public string AgentName { get; set; } = "hockey-season-data.agent";

    /// <summary>Path of the agent instructions, relative to the content root or the application directory.</summary>
    public string InstructionsPath { get; set; } = "Agents/hockey-season-data.agent.md";

    /// <summary>Ollama-compatible API base URL, e.g. https://ollama.com/api.</summary>
    public string OllamaUrl { get; set; } = "https://ollama.com/api";

    /// <summary>API key used as Bearer token (optional for local Ollama).</summary>
    public string? OllamaApiKey { get; set; }

    /// <summary>Model used by the Hockey Assistant, e.g. glm-5.3-flash:cloud.</summary>
    public string OllamaModel { get; set; } = "glm-5.3-flash:cloud";
}
