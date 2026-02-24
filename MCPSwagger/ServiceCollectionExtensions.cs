using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SwaggerMcp.Dispatch;
using SwaggerMcp.Discovery;
using SwaggerMcp.Options;
using SwaggerMcp.Schema;
using SwaggerMcp.Transport;

namespace SwaggerMcp.Extensions;

/// <summary>
/// Extension methods for registering SwaggerMcp services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SwaggerMcp services. Call this before <c>builder.Build()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddSwaggerMcp(options =>
    /// {
    ///     options.ServerName = "My API";
    ///     options.RoutePrefix = "/mcp";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddSwaggerMcp(
        this IServiceCollection services,
        Action<SwaggerMcpOptions>? configure = null)
    {
        // Register options
        var optionsBuilder = services.AddOptions<SwaggerMcpOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        // Resolve server name from entry assembly if not set
        services.PostConfigure<SwaggerMcpOptions>(options =>
        {
            options.ServerName ??= Assembly.GetEntryAssembly()?.GetName().Name ?? "SwaggerMcp Server";
        });

        // Core infrastructure — all singletons since they cache at startup
        services.AddSingleton<McpSchemaBuilder>();
        services.AddSingleton<McpToolDiscoveryService>();

        // Dispatch infrastructure
        services.AddSingleton<SyntheticHttpContextFactory>();
        services.AddSingleton<McpToolDispatcher>();

        // Transport
        services.AddSingleton<McpSwaggerToolHandler>();

        // IHttpContextFactory is needed for synthetic context creation
        services.AddSingleton<IHttpContextFactory, DefaultHttpContextFactory>();

        return services;
    }
}
