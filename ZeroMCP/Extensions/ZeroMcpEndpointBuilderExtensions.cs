using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZeroMCP.Options;
using ZeroMCP.Transport;

namespace ZeroMCP.Extensions;

/// <summary>
/// Extension methods for <see cref="ZeroMcpEndpointBuilder"/>.
/// </summary>
public static class ZeroMcpEndpointBuilderExtensions
{
    /// <summary>
    /// Registers the deprecated MCP HTTP+SSE transport (spec 2024-11-05) for backward compatibility.
    /// Adds GET {RoutePrefix}/sse and POST {RoutePrefix}/messages.
    /// </summary>
    /// <remarks>
    /// SSE sessions are held in process memory; does not scale horizontally without sticky sessions.
    /// Use Streamable HTTP for horizontal scale.
    /// </remarks>
    public static ZeroMcpEndpointBuilder WithLegacySseTransport(this ZeroMcpEndpointBuilder builder)
    {
        var sseHandler = builder.Endpoints.ServiceProvider.GetRequiredService<McpLegacySseEndpointHandler>();
        var sseRoute = builder.BaseRoute + "/sse";
        var messagesRoute = builder.BaseRoute + "/messages";

        builder.Endpoints.MapGet(sseRoute, (ctx) => sseHandler.HandleSseConnectionAsync(ctx))
            .WithDisplayName("MCP Legacy SSE (ZeroMCP)");
        builder.Endpoints.MapPost(messagesRoute, (ctx) => sseHandler.HandleMessagesAsync(ctx))
            .WithDisplayName("MCP Legacy SSE Messages (ZeroMCP)");

        var logger = builder.Endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("ZeroMCP");
        logger.LogInformation("ZeroMCP Legacy SSE transport registered: GET {SseRoute}, POST {MessagesRoute}", sseRoute, messagesRoute);

        return builder;
    }
}
