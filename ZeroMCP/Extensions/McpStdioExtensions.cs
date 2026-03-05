using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZeroMCP.Options;
using ZeroMCP.Transport;

namespace ZeroMCP.Extensions;

/// <summary>
/// Extension methods for running ZeroMCP in stdio transport mode.
/// Use when the application is launched as a subprocess (e.g. by Claude Desktop, Claude Code).
/// </summary>
public static class McpStdioExtensions
{
    /// <summary>
    /// Runs the MCP JSON-RPC loop over stdin/stdout. Does not start the HTTP server.
    /// Call this instead of <c>app.Run()</c> when using stdio transport.
    /// Typically used when <c>args.Contains("--mcp-stdio")</c> or <c>Environment.GetEnvironmentVariable("ZEROMCP_STDIO") == "1"</c>.
    /// </summary>
    /// <remarks>
    /// Uses stderr for internal ZeroMCP logging when possible; stdout is reserved for JSON-RPC only.
    /// Tool Inspector and /mcp/tools are not available in stdio mode.
    /// </remarks>
    public static Task RunMcpStdioAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ZeroMCPOptions>>().Value;
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<McpStdioHostRunner>();

        var runner = new McpStdioHostRunner(app.Services, options, logger);
        return runner.RunAsync(cancellationToken);
    }
}
