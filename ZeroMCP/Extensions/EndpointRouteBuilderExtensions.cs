using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZeroMCP.Ui;
using ZeroMCP.Transport;
using ZeroMCP.Options;
using ZeroMCP.Discovery;

namespace ZeroMCP.Extensions;

/// <summary>
/// Extension methods for mapping the MCP endpoint in the ASP.NET Core routing pipeline.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the MCP endpoint (default: POST /mcp).
    /// When versioned tools exist, also registers /mcp/v1, /mcp/v2, etc. and /mcp resolves to the default (highest or configured) version.
    /// Place this after <c>app.UseRouting()</c> and <c>app.UseAuthorization()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// app.MapZeroMCP();
    /// // or with a custom route:
    /// app.MapZeroMCP("/api/mcp");
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapZeroMCP(
        this IEndpointRouteBuilder endpoints,
        string? routePrefix = null)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<ZeroMCPOptions>>().Value;
        var route = routePrefix ?? options.RoutePrefix;

        // Normalize — ensure single leading slash, no trailing slash
        route = "/" + route.Trim('/');
        var baseRoute = route.TrimEnd('/');

        var logger = endpoints.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("ZeroMCP");

        var toolHandler = endpoints.ServiceProvider.GetRequiredService<McpToolHandler>();
        var discovery = endpoints.ServiceProvider.GetRequiredService<McpToolDiscoveryService>();
        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var handlerLogger = loggerFactory.CreateLogger<McpHttpEndpointHandler>();

        // Trigger discovery build so we can check for versioned tools
        _ = discovery.GetRegistry();

        if (!discovery.HasVersionedTools)
        {
            logger.LogInformation("ZeroMCP MCP endpoint registered at POST {Route}", route);
            var mcpHandler = new McpHttpEndpointHandler(toolHandler, options, handlerLogger);

            if (options.EnableToolInspector)
            {
                var toolsRoute = baseRoute + "/tools";
                endpoints.MapGet(toolsRoute, (ctx) => mcpHandler.HandleToolsInspectorAsync(ctx))
                    .WithName("mcp-tools-inspector")
                    .WithDisplayName("MCP Tool Inspector (ZeroMCP)");
                if (options.EnableToolInspectorUI)
                {
                    var uiRoute = baseRoute + "/ui";
                    endpoints.MapGet(uiRoute, (ctx) =>
                    {
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        ctx.Response.StatusCode = 200;
                        return ctx.Response.WriteAsync(McpInspectorUiHtml.GetHtml(baseRoute));
                    })
                        .WithName("mcp-tools-ui")
                        .WithDisplayName("MCP Tool Inspector UI (ZeroMCP)");
                }
            }

            return endpoints.MapMethods(route, ["GET", "POST"], (ctx) => mcpHandler.HandleAsync(ctx))
                .WithName("mcp-endpoint")
                .WithDisplayName("MCP Endpoint (ZeroMCP)")
                .WithMetadata(new HttpMethodMetadata(["GET", "POST"]));
        }

        var availableVersions = discovery.GetAvailableVersions();
        var defaultVersion = options.DefaultVersion ?? (availableVersions.Count > 0 ? availableVersions[^1] : (int?)null);
        logger.LogInformation("ZeroMCP MCP endpoints registered: POST {Route} (default=v{DefaultVersion}), and versioned: {Versions}",
            route, defaultVersion ?? 0, string.Join(", ", availableVersions.Select(v => "v" + v)));

        foreach (var v in availableVersions)
        {
            var versionedHandler = new McpHttpEndpointHandler(toolHandler, options, handlerLogger, v, availableVersions);
            var versionedBase = baseRoute + "/v" + v;

            endpoints.MapMethods(versionedBase, ["GET", "POST"], (ctx) => versionedHandler.HandleAsync(ctx))
                .WithDisplayName($"MCP Endpoint v{v} (ZeroMCP)")
                .WithMetadata(new HttpMethodMetadata(["GET", "POST"]));

            if (options.EnableToolInspector)
            {
                endpoints.MapGet(versionedBase + "/tools", (ctx) => versionedHandler.HandleToolsInspectorAsync(ctx))
                    .WithDisplayName($"MCP Tool Inspector v{v} (ZeroMCP)");
                if (options.EnableToolInspectorUI)
                {
                    var mcpBasePath = versionedBase;
                    var uiHtml = McpInspectorUiHtml.GetHtml(mcpBasePath, v, availableVersions);
                    endpoints.MapGet(versionedBase + "/ui", (ctx) =>
                    {
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        ctx.Response.StatusCode = 200;
                        return ctx.Response.WriteAsync(uiHtml);
                    })
                        .WithDisplayName($"MCP Tool Inspector UI v{v} (ZeroMCP)");
                }
            }
        }

        var defaultHandler = new McpHttpEndpointHandler(toolHandler, options, handlerLogger, defaultVersion, availableVersions);

        if (options.EnableToolInspector)
        {
            var toolsRoute = baseRoute + "/tools";
            endpoints.MapGet(toolsRoute, (ctx) => defaultHandler.HandleToolsInspectorAsync(ctx))
                .WithName("mcp-tools-inspector")
                .WithDisplayName("MCP Tool Inspector (ZeroMCP)");
            if (options.EnableToolInspectorUI)
            {
                var uiRoute = baseRoute + "/ui";
                var uiHtml = McpInspectorUiHtml.GetHtml(baseRoute, defaultVersion, availableVersions);
                endpoints.MapGet(uiRoute, (ctx) =>
                {
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.StatusCode = 200;
                    return ctx.Response.WriteAsync(uiHtml);
                })
                    .WithName("mcp-tools-ui")
                    .WithDisplayName("MCP Tool Inspector UI (ZeroMCP)");
            }
        }

        return endpoints.MapMethods(route, ["GET", "POST"], (ctx) => defaultHandler.HandleAsync(ctx))
            .WithName("mcp-endpoint")
            .WithDisplayName("MCP Endpoint (ZeroMCP)")
            .WithMetadata(new HttpMethodMetadata(["GET", "POST"]));
    }
}
