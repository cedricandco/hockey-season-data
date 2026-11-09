using System.Reflection;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace HockeySeason.Mcp;

/// <summary>
/// Bridges the MCP tool definitions of <see cref="LeagueTools"/> into
/// <see cref="AIFunction"/> instances that an <c>AI agent</c> can call. The MCP
/// attributes stay the single source of truth for names and descriptions: the
/// same methods serve the streamable-HTTP MCP endpoint and the agent tool list.
/// </summary>
public static class LeagueAgentToolFactory
{
    /// <summary>
    /// Creates one <see cref="AIFunction"/> per method carrying
    /// <see cref="McpServerToolAttribute"/> on <paramref name="tools"/>.
    /// </summary>
    public static System.Collections.Generic.IList<AITool> CreateFrom(LeagueTools tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var functions = new List<AITool>();
        foreach (var method in typeof(LeagueTools).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attribute is null)
            {
                continue;
            }

            var description = method
                .GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()
                ?.Description
                ?? method.Name;

            functions.Add(AIFunctionFactory.Create(method, tools, attribute.Name, description));
        }

        return functions;
    }
}
