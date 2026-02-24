using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwaggerMcp.Options;
using SwaggerMcp.Transport;

namespace SwaggerMcp.Extensions;

/// <summary>
/// Extension methods for mapping the MCP endpoint in the ASP.NET Core routing pipeline.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the MCP endpoint (default: POST /mcp).
    /// Place this after <c>app.UseRouting()</c> and <c>app.UseAuthorization()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// app.MapSwaggerMcp();
    /// // or with a custom route:
    /// app.MapSwaggerMcp("/api/mcp");
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapSwaggerMcp(
        this IEndpointRouteBuilder endpoints,
        string? routePrefix = null)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<SwaggerMcpOptions>>().Value;
        var route = routePrefix ?? options.RoutePrefix;

        // Normalize — ensure single leading slash, no trailing slash
        route = "/" + route.Trim('/');

        var logger = endpoints.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("SwaggerMcp");

        logger.LogInformation("SwaggerMcp MCP endpoint registered at POST {Route}", route);

        // Pre-build the handler once — it's expensive to construct per-request
        var toolHandler = endpoints.ServiceProvider.GetRequiredService<McpSwaggerToolHandler>();
        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var handlerLogger = loggerFactory.CreateLogger<McpHttpEndpointHandler>();

        var mcpHandler = new McpHttpEndpointHandler(
            toolHandler,
            options.ServerName!,
            options.ServerVersion,
            handlerLogger);

        // The MCP streamable HTTP transport: GET returns endpoint info, POST handles JSON-RPC
        return endpoints.MapMethods(route, ["GET", "POST"], (HttpContext ctx) => mcpHandler.HandleAsync(ctx))
            .WithName("mcp-endpoint")
            .WithDisplayName("MCP Endpoint (SwaggerMcp)")
            .WithMetadata(new HttpMethodMetadata(["GET", "POST"]));
    }
}
