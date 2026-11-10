using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace HockeySeason.Agents;

/// <summary>
/// Creates the <see cref="IChatClient"/> used by the Hockey Assistant
/// (an Ollama-compatible chat API such as Ollama Cloud or a local Ollama).
/// </summary>
public static class HockeyAssistantChatClientFactory
{
    public static IChatClient Create(HockeySeasonDataAgentOptions options)
    {
        var http = new HttpClient { BaseAddress = new Uri(options.OllamaUrl) };
        if (!string.IsNullOrWhiteSpace(options.OllamaApiKey))
        {
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.OllamaApiKey);
        }

        // OllamaApiClient implements Microsoft.Extensions.AI.IChatClient.
        return new OllamaApiClient(http, options.OllamaModel);
    }
}
