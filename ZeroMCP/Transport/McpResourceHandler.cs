using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ZeroMCP.Discovery;
using ZeroMCP.Dispatch;

namespace ZeroMCP.Transport;

/// <summary>
/// Handles MCP resource listing and reading by bridging the JSON-RPC calls
/// to the resource discovery and dispatch infrastructure.
/// </summary>
internal sealed class McpResourceHandler
{
    private readonly McpResourceDiscoveryService _discovery;
    private readonly McpToolDispatcher _dispatcher;
    private readonly ILogger<McpResourceHandler> _logger;

    public McpResourceHandler(
        McpResourceDiscoveryService discovery,
        McpToolDispatcher dispatcher,
        ILogger<McpResourceHandler> logger)
    {
        _discovery = discovery;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>
    /// Handles the <c>resources/list</c> JSON-RPC method.
    /// Returns all static resources registered with <c>[McpResource]</c>.
    /// </summary>
    public object HandleResourcesList()
    {
        var resources = _discovery.GetStaticResources().Select(r =>
        {
            var obj = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["uri"] = r.ResourceUri,
                ["name"] = r.Name
            };
            if (!string.IsNullOrEmpty(r.Description)) obj["description"] = r.Description;
            if (!string.IsNullOrEmpty(r.MimeType)) obj["mimeType"] = r.MimeType;
            return (object)obj;
        }).ToList();

        return new { resources };
    }

    /// <summary>
    /// Handles the <c>resources/templates/list</c> JSON-RPC method.
    /// Returns all URI-templated resources registered with <c>[McpTemplate]</c>.
    /// </summary>
    public object HandleResourcesTemplatesList()
    {
        var resourceTemplates = _discovery.GetTemplateResources().Select(t =>
        {
            var obj = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["uriTemplate"] = t.UriTemplate!,
                ["name"] = t.Name
            };
            if (!string.IsNullOrEmpty(t.Description)) obj["description"] = t.Description;
            if (!string.IsNullOrEmpty(t.MimeType)) obj["mimeType"] = t.MimeType;
            return (object)obj;
        }).ToList();

        return new { resourceTemplates };
    }

    /// <summary>
    /// Handles the <c>resources/read</c> JSON-RPC method.
    /// Dispatches the backing controller action and wraps the response as MCP resource contents.
    /// </summary>
    public async Task<object> HandleResourcesReadAsync(
        JsonElement @params,
        HttpContext? sourceContext,
        CancellationToken cancellationToken)
    {
        if (@params.ValueKind == JsonValueKind.Undefined || !@params.TryGetProperty("uri", out var uriEl))
            throw new McpInvalidParamsException("resources/read requires params.uri");

        var uri = uriEl.GetString();
        if (string.IsNullOrEmpty(uri))
            throw new McpInvalidParamsException("resources/read params.uri must be a non-empty string");

        var (descriptor, templateVars) = _discovery.FindForUri(uri);
        if (descriptor is null)
        {
            _logger.LogWarning("MCP resources/read: no resource found for uri={Uri}", uri);
            throw new McpInvalidParamsException($"No resource found for URI: {uri}");
        }

        // Build args: for templates, extracted URI variables; for static, empty
        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (templateVars is not null)
        {
            foreach (var (k, v) in templateVars)
                args[k] = JsonDocument.Parse(JsonSerializer.Serialize(v)).RootElement;
        }

        _logger.LogDebug("MCP resources/read dispatching uri={Uri} → {Method} {Url}", uri, descriptor.HttpMethod, descriptor.RelativeUrl);

        var result = await _dispatcher.DispatchAsync(descriptor.DispatchDescriptor, args, cancellationToken, sourceContext);

        var contentEntry = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["uri"] = uri,
            ["text"] = result.Content
        };
        if (!string.IsNullOrEmpty(descriptor.MimeType))
            contentEntry["mimeType"] = descriptor.MimeType;
        else if (!string.IsNullOrEmpty(result.ContentType))
            contentEntry["mimeType"] = result.ContentType;

        if (!result.IsSuccess)
        {
            _logger.LogWarning("MCP resources/read action failed: uri={Uri}, status={Status}", uri, result.StatusCode);
        }

        return new { contents = new[] { contentEntry } };
    }
}
