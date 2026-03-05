using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZeroMCP.Discovery;
using ZeroMCP.Options;

namespace ZeroMCP.Transport;

/// <summary>
/// Runs the MCP JSON-RPC loop over stdin/stdout for stdio transport mode.
/// Reads newline-delimited JSON-RPC from stdin, routes through the handler, writes responses to stdout.
/// </summary>
internal sealed class McpStdioHostRunner
{
    private readonly IServiceProvider _services;
    private readonly ZeroMCPOptions _options;
    private readonly ILogger<McpStdioHostRunner> _logger;

    public McpStdioHostRunner(IServiceProvider services, ZeroMCPOptions options, ILogger<McpStdioHostRunner> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Runs the stdio loop until stdin closes (EOF). Uses stderr for logging; stdout reserved for JSON-RPC only.
    /// </summary>
    public Task RunAsync(CancellationToken hostCancellation = default) =>
        RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), hostCancellation);

    /// <summary>
    /// Runs the MCP loop over the given streams. Used for testing or custom transport.
    /// </summary>
    public async Task RunAsync(Stream stdin, Stream stdout, CancellationToken hostCancellation = default)
    {
        using var reader = new StreamReader(stdin, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(stdout, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        var toolHandler = _services.GetRequiredService<McpToolHandler>();
        var discovery = _services.GetRequiredService<McpToolDiscoveryService>();
        var handlerLogger = _services.GetRequiredService<ILoggerFactory>().CreateLogger<McpHttpEndpointHandler>();

        // Use default (unversioned) endpoint for stdio
        var endpointVersion = discovery.HasVersionedTools
            ? (_options.DefaultVersion ?? (discovery.GetAvailableVersions().Count > 0 ? discovery.GetAvailableVersions()[^1] : (int?)null))
            : (int?)null;
        var availableVersions = discovery.HasVersionedTools ? discovery.GetAvailableVersions() : [];

        var mcpHandler = new McpHttpEndpointHandler(toolHandler, _options, handlerLogger, endpointVersion, availableVersions);

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(hostCancellation);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
                break; // EOF

            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var context = CreateStdioContext(doc.RootElement, _services, _options);
                var responseJson = await mcpHandler.ProcessMessageAsync(doc, context);
                if (responseJson is not null)
                {
                    await writer.WriteLineAsync(responseJson);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON-RPC line received");
                var errorResponse = SerializeError(null, -32700, "Parse error", null);
                await writer.WriteLineAsync(errorResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing stdio message");
                var errorResponse = SerializeError(null, -32603, "Internal error", ex.Message);
                await writer.WriteLineAsync(errorResponse);
            }
        }
    }

    private static HttpContext CreateStdioContext(JsonElement root, IServiceProvider services, ZeroMCPOptions options)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        features.Set<IHttpRequestLifetimeFeature>(new HttpRequestLifetimeFeature()); // stdio: per-request cancellation via notifications/cancelled
        features.Set<IHttpResponseFeature>(new HttpResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
        features.Set<IServiceProvidersFeature>(new StdioRequestServicesFeature(services));
        features.Set<IItemsFeature>(new ItemsFeature());

        var context = new DefaultHttpContext(features);
        context.RequestServices = services;
        context.User = options.StdioIdentity ?? new ClaimsPrincipal();
        context.Items[McpHttpEndpointHandler.CorrelationIdItemKey] = correlationId;
        context.TraceIdentifier = correlationId;

        return context;
    }

    private static string SerializeError(object? id, int code, string message, string? data)
    {
        var error = data is not null ? (object)new { code, message, data } : new { code, message };
        var response = new { jsonrpc = "2.0", id, error };
        return JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}

internal sealed class StdioRequestServicesFeature : IServiceProvidersFeature
{
    public StdioRequestServicesFeature(IServiceProvider requestServices) => RequestServices = requestServices;
    public IServiceProvider RequestServices { get; set; }
}
