using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZeroMCP.Observability;
using ZeroMCP.Transport;
using ZeroMCP.Schema;
using ZeroMCP.Discovery;
using ZeroMCP.Dispatch;
using ZeroMCP.Options;

namespace ZeroMCP.Extensions;

/// <summary>
/// Extension methods for registering ZeroMCP services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds ZeroMCP services. Call this before <c>builder.Build()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddZeroMCP(options =>
    /// {
    ///     options.ServerName = "My API";
    ///     options.RoutePrefix = "/mcp";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddZeroMCP(
        this IServiceCollection services,
        Action<ZeroMCPOptions>? configure = null)
    {
        // Register options
        var optionsBuilder = services.AddOptions<ZeroMCPOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        // Resolve server name from entry assembly if not set
        services.PostConfigure<ZeroMCPOptions>(options =>
        {
            options.ServerName ??= Assembly.GetEntryAssembly()?.GetName().Name ?? "ZeroMCP Server";
        });

        // Core infrastructure — all singletons since they cache at startup
        services.AddSingleton<McpSchemaBuilder>();
        services.AddSingleton<McpToolDiscoveryService>();

        // Dispatch infrastructure
        services.AddSingleton<SyntheticHttpContextFactory>();
        services.AddSingleton<McpToolDispatcher>();

        // Observability: metrics sink (register your own after AddZeroMCP to replace the no-op)
        services.AddSingleton<IMcpMetricsSink, NoOpMcpMetricsSink>();

        // Transport
        services.AddSingleton<McpToolHandler>();

        // Legacy SSE transport (opt-in via WithLegacySseTransport or EnableLegacySseTransport)
        services.AddSingleton<McpLegacySseEndpointHandler>();

        // IHttpContextFactory is needed for synthetic context creation
        services.AddSingleton<IHttpContextFactory, DefaultHttpContextFactory>();

        // Register the streaming capture formatter so IAsyncEnumerable results can be intercepted during dispatch
        services.Configure<MvcOptions>(mvc =>
        {
            mvc.OutputFormatters.Insert(0, new McpStreamingCaptureFormatter());
        });

        return services;
    }
}
