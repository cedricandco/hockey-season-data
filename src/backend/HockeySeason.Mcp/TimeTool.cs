using System.ComponentModel;
using HockeySeason.Application;
using HockeySeason.Domain;
using ModelContextProtocol.Server;

namespace HockeySeason.Mcp;

/// <summary>
/// MCP tools for the Hockey Season data API. Tool methods are plain calls
/// into application use cases, keeping this layer a thin adapter (SRP).
/// </summary>
[McpServerToolType]
public sealed class TimeTool
{
    private readonly IHealthService _healthService;

    public TimeTool(IHealthService healthService)
    {
        _healthService = healthService;
    }

    [McpServerTool(Name = "get-server-time", Title = "Get server time")]
    [Description("Returns the current server time in UTC")]
    public ServerTime GetServerTime()
    {
        return _healthService.GetServerTime();
    }
}
