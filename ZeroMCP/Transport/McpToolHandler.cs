using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZeroMCP.Observability;
using ZeroMCP;
using ZeroMCP.Transport;
using ZeroMCP.Discovery;
using ZeroMCP.Dispatch;
using ZeroMCP.Options;

namespace ZeroMCP.Transport;

/// <summary>
/// Handles MCP tool listing and invocation by bridging the MCP SDK
/// to our internal discovery and dispatch infrastructure.
/// </summary>
internal sealed class McpToolHandler
{
    private readonly McpToolDiscoveryService _discovery;
    private readonly McpToolDispatcher _dispatcher;
    private readonly ZeroMCPOptions _options;
    private readonly IMcpMetricsSink _metricsSink;
    private readonly ILogger<McpToolHandler> _logger;

    public McpToolHandler(
        McpToolDiscoveryService discovery,
        McpToolDispatcher dispatcher,
        IOptions<ZeroMCPOptions> options,
        IMcpMetricsSink metricsSink,
        ILogger<McpToolHandler> logger)
    {
        _discovery = discovery;
        _dispatcher = dispatcher;
        _options = options.Value;
        _metricsSink = metricsSink;
        _logger = logger;
    }

    /// <summary>
    /// Returns all registered MCP tools in the format the SDK expects (no per-request filtering).
    /// For role/policy/visibility filtering use <see cref="GetToolDefinitionsAsync"/> with an <see cref="HttpContext"/>.
    /// </summary>
    public IEnumerable<McpToolDefinition> GetToolDefinitions()
    {
        foreach (var descriptor in _discovery.GetTools())
        {
            yield return new McpToolDefinition
            {
                Name = descriptor.Name,
                Description = BuildDescription(descriptor),
                InputSchema = descriptor.InputSchemaJson is not null
                    ? JsonDocument.Parse(descriptor.InputSchemaJson).RootElement
                    : DefaultEmptySchema(),
                Category = descriptor.Category,
                Tags = descriptor.Tags,
                Examples = descriptor.Examples,
                Hints = descriptor.Hints,
                IsStreaming = descriptor.IsStreaming
            };
        }
    }

    /// <summary>
    /// Returns MCP tools visible to the current request (filtered by roles, policy, and ToolVisibilityFilter).
    /// When <paramref name="version"/> is set, only tools for that version endpoint are returned.
    /// </summary>
    public async Task<IReadOnlyList<McpToolDefinition>> GetToolDefinitionsAsync(HttpContext? context, CancellationToken cancellationToken = default, int? version = null)
    {
        var descriptors = version is null
            ? _discovery.GetTools()
            : _discovery.GetToolsForVersion(version.Value);
        var list = new List<McpToolDefinition>();
        foreach (var descriptor in descriptors)
        {
            if (context is not null && !await IsVisibleAsync(descriptor, context, cancellationToken).ConfigureAwait(false))
                continue;
            list.Add(new McpToolDefinition
            {
                Name = descriptor.Name,
                Description = BuildDescription(descriptor),
                InputSchema = descriptor.InputSchemaJson is not null
                    ? JsonDocument.Parse(descriptor.InputSchemaJson).RootElement
                    : DefaultEmptySchema(),
                Category = descriptor.Category,
                Tags = descriptor.Tags,
                Examples = descriptor.Examples,
                Hints = descriptor.Hints,
                IsStreaming = descriptor.IsStreaming
            });
        }
        return list;
    }

    /// <summary>
    /// Returns the full tool registry as a payload for the GET /mcp/tools inspector endpoint.
    /// When <paramref name="version"/> is set, only tools for that version are included; response includes version and availableVersions.
    /// </summary>
    internal object GetInspectorPayload(int? version = null, IReadOnlyList<int>? availableVersions = null)
    {
        var descriptors = version is null
            ? _discovery.GetTools()
            : _discovery.GetToolsForVersion(version.Value);
        var tools = new List<object>();
        foreach (var d in descriptors)
        {
            var schema = d.InputSchemaJson is not null
                ? JsonDocument.Parse(d.InputSchemaJson).RootElement
                : DefaultEmptySchema();
            var entry = new Dictionary<string, object?>
            {
                ["name"] = d.Name,
                ["description"] = d.Description ?? "",
                ["httpMethod"] = d.HttpMethod,
                ["route"] = d.RelativeUrl,
                ["category"] = d.Category,
                ["tags"] = d.Tags,
                ["examples"] = d.Examples,
                ["hints"] = d.Hints,
                ["inputSchema"] = schema,
                ["requiredRoles"] = d.RequiredRoles,
                ["requiredPolicy"] = d.RequiredPolicy,
                ["version"] = d.Version,
                ["isStreaming"] = d.IsStreaming
            };
            tools.Add(entry);
        }
        var payload = new Dictionary<string, object?>
        {
            ["serverName"] = _options.ServerName,
            ["serverVersion"] = _options.ServerVersion,
            ["protocolVersion"] = McpProtocolConstants.ProtocolVersion,
            ["toolCount"] = tools.Count,
            ["tools"] = tools
        };
        if (version is not null)
            payload["version"] = version.Value;
        if (availableVersions is { Count: > 0 })
            payload["availableVersions"] = availableVersions;
        return payload;
    }

    private async Task<bool> IsVisibleAsync(McpToolDescriptor descriptor, HttpContext context, CancellationToken cancellationToken)
    {
        if (descriptor.RequiredRoles is { Length: > 0 })
        {
            var inRole = false;
            foreach (var role in descriptor.RequiredRoles)
            {
                if (context.User.IsInRole(role))
                {
                    inRole = true;
                    break;
                }
            }
            if (!inRole)
            {
                _logger.LogDebug("Tool '{ToolName}' hidden from tools/list: user not in required roles", descriptor.Name);
                return false;
            }
        }

        if (!string.IsNullOrEmpty(descriptor.RequiredPolicy))
        {
            var authService = context.RequestServices.GetService<IAuthorizationService>();
            if (authService is null)
            {
                _logger.LogDebug("Tool '{ToolName}' hidden: RequiredPolicy set but IAuthorizationService not available", descriptor.Name);
                return false;
            }
            var result = await authService.AuthorizeAsync(context.User, null, descriptor.RequiredPolicy!).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _logger.LogDebug("Tool '{ToolName}' hidden from tools/list: policy '{Policy}' not satisfied", descriptor.Name, descriptor.RequiredPolicy);
                return false;
            }
        }

        if (_options.ToolVisibilityFilter is not null && !_options.ToolVisibilityFilter(descriptor.Name, context))
        {
            _logger.LogDebug("Tool '{ToolName}' excluded by ToolVisibilityFilter", descriptor.Name);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Handles a tools/call request from the MCP client.
    /// When <paramref name="version"/> is set, the tool is resolved from that version's endpoint set.
    /// </summary>
    /// <param name="sourceContext">Optional HTTP context of the MCP request; when set, configured headers (e.g. Authorization) are forwarded to the dispatched action.</param>
    public async Task<McpToolResult> HandleCallAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken,
        HttpContext? sourceContext = null,
        int? version = null)
    {
        var correlationId = sourceContext?.Items[McpHttpEndpointHandler.CorrelationIdItemKey] as string;
        var descriptor = _discovery.GetTool(toolName, version);

        if (descriptor is null)
        {
            _logger.LogWarning("MCP client requested unknown tool: ToolName={ToolName}, CorrelationId={CorrelationId}", toolName, correlationId ?? "");
            return McpToolResult.Error($"Unknown tool: {toolName}");
        }

        // Enforce roles/policy/visibility for tools/call so UI and MCP clients cannot invoke tools they are not allowed to see.
        if (sourceContext is not null && !await IsVisibleAsync(descriptor, sourceContext, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("MCP tools/call denied: tool not visible to caller. ToolName={ToolName}, CorrelationId={CorrelationId}", toolName, correlationId ?? "");
            return McpToolResult.Error($"Tool '{toolName}' is not available (roles or policy not satisfied).");
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await _dispatcher.DispatchAsync(descriptor, args, cancellationToken, sourceContext);
        stopwatch.Stop();

        var statusCode = result.StatusCode;
        var isError = !result.IsSuccess;
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        _metricsSink.RecordToolInvocation(toolName, statusCode, isError, durationMs, correlationId);

        _logger.Log(isError ? LogLevel.Warning : LogLevel.Debug,
            "Tool invocation: ToolName={ToolName}, StatusCode={StatusCode}, IsError={IsError}, DurationMs={DurationMs}, CorrelationId={CorrelationId}",
            toolName, statusCode, isError, durationMs, correlationId ?? "");

        if (_options.EnableOpenTelemetryEnrichment && Activity.Current is { } activity)
        {
            activity.SetTag("mcp.tool", toolName);
            activity.SetTag("mcp.status_code", statusCode);
            activity.SetTag("mcp.is_error", isError);
            activity.SetTag("mcp.duration_ms", durationMs);
            if (!string.IsNullOrEmpty(correlationId))
                activity.SetTag("mcp.correlation_id", correlationId);
        }

        IReadOnlyList<McpSuggestedAction>? suggestedActions = null;
        if (_options.EnableSuggestedFollowUps && _options.SuggestedFollowUpsProvider is not null)
        {
            var suggested = _options.SuggestedFollowUpsProvider(toolName, statusCode, result.Content, isError, sourceContext);
            suggestedActions = suggested?.Select(s => new McpSuggestedAction(s.ToolName, s.Rationale)).ToList();
        }

        IReadOnlyList<string>? hints = null;
        if (_options.EnableResultEnrichment && _options.ResponseHintProvider is not null)
            hints = _options.ResponseHintProvider(toolName, statusCode, result.Content, isError, sourceContext);

        if (_options.EnableResultEnrichment)
        {
            if (result.IsSuccess)
                return McpToolResult.SuccessWithEnrichment(result.Content, result.ContentType, statusCode, durationMs, correlationId, suggestedActions, hints);
            return McpToolResult.ErrorWithEnrichment(
                $"Tool '{toolName}' failed with HTTP {result.StatusCode}: {result.Content}",
                statusCode, durationMs, correlationId, suggestedActions, hints);
        }

        if (suggestedActions is { Count: > 0 })
        {
            if (result.IsSuccess)
                return McpToolResult.SuccessWithSuggested(result.Content, result.ContentType, suggestedActions);
            return McpToolResult.ErrorWithSuggested(
                $"Tool '{toolName}' failed with HTTP {result.StatusCode}: {result.Content}",
                suggestedActions);
        }

        if (result.IsSuccess)
            return McpToolResult.Success(result.Content, result.ContentType);

        return McpToolResult.Error(
            $"Tool '{toolName}' failed with HTTP {result.StatusCode}: {result.Content}");
    }

    /// <summary>
    /// Returns true if the named tool is a streaming tool (returns IAsyncEnumerable).
    /// </summary>
    internal bool IsStreamingTool(string toolName, int? version = null)
    {
        var descriptor = _discovery.GetTool(toolName, version);
        return descriptor?.IsStreaming == true;
    }

    /// <summary>
    /// Returns the descriptor for a tool after checking visibility, for use by the streaming path.
    /// Returns null if the tool doesn't exist or is not visible.
    /// </summary>
    internal async Task<McpToolDescriptor?> GetStreamingDescriptorAsync(string toolName, HttpContext? sourceContext, CancellationToken ct, int? version = null)
    {
        var descriptor = _discovery.GetTool(toolName, version);
        if (descriptor is null) return null;
        if (sourceContext is not null && !await IsVisibleAsync(descriptor, sourceContext, ct).ConfigureAwait(false))
            return null;
        return descriptor;
    }

    /// <summary>
    /// Returns the streaming dispatch enumerable for a streaming tool.
    /// The caller (McpHttpEndpointHandler) is responsible for writing SSE events.
    /// </summary>
    internal IAsyncEnumerable<DispatchStreamChunk> StreamToolAsync(
        McpToolDescriptor descriptor,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken,
        HttpContext? sourceContext = null)
    {
        return _dispatcher.DispatchStreamingAsync(descriptor, args, _options.MaxStreamingItems, cancellationToken, sourceContext);
    }

    private static string BuildDescription(McpToolDescriptor descriptor)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(descriptor.Description))
            sb.Append(descriptor.Description);

        // Append HTTP method and route so the LLM has additional context
        if (!string.IsNullOrWhiteSpace(descriptor.HttpMethod) || !string.IsNullOrWhiteSpace(descriptor.RelativeUrl))
        {
            sb.Append(" [");
            if (!string.IsNullOrWhiteSpace(descriptor.HttpMethod))
            {
                sb.Append(descriptor.HttpMethod.ToUpperInvariant());
                if (!string.IsNullOrWhiteSpace(descriptor.RelativeUrl))
                    sb.Append(' ');
            }
            if (!string.IsNullOrWhiteSpace(descriptor.RelativeUrl))
            {
                sb.Append('/');
                sb.Append(descriptor.RelativeUrl);
            }
            sb.Append(']');
        }

        if (!string.IsNullOrWhiteSpace(descriptor.Category))
        {
            sb.Append($" Category: {descriptor.Category}.");
        }

        if (descriptor.Tags is { Length: > 0 })
        {
            sb.Append($" Tags: {string.Join(", ", descriptor.Tags)}.");
        }

        if (descriptor.Hints is { Length: > 0 })
        {
            sb.Append($" Hints: {string.Join("; ", descriptor.Hints)}.");
        }

        return sb.ToString().Trim();
    }

    private static JsonElement DefaultEmptySchema()
    {
        return JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
    }
}

/// <summary>Tool definition passed to the MCP SDK.</summary>
public sealed class McpToolDefinition
{
    public string Name { get; init; } = default!;
    public string Description { get; init; } = default!;
    public JsonElement InputSchema { get; init; }
    public string? Category { get; init; }
    public string[]? Tags { get; init; }
    public string[]? Examples { get; init; }
    public string[]? Hints { get; init; }
    /// <summary>True when this tool returns IAsyncEnumerable and results are streamed progressively.</summary>
    public bool IsStreaming { get; init; }
}

/// <summary>Result returned from a tool invocation.</summary>
public sealed class McpToolResult
{
    public bool IsError { get; private init; }
    public string Content { get; private init; } = default!;
    public string ContentType { get; private init; } = "application/json";

    /// <summary>When result enrichment is enabled: HTTP status code of the dispatched action.</summary>
    public int? StatusCode { get; private init; }
    /// <summary>When result enrichment is enabled: invocation duration in milliseconds.</summary>
    public double? DurationMs { get; private init; }
    /// <summary>When result enrichment is enabled: correlation ID from the request.</summary>
    public string? CorrelationId { get; private init; }
    /// <summary>When suggested follow-ups are enabled: suggested next tools with rationale.</summary>
    public IReadOnlyList<McpSuggestedAction>? SuggestedNextActions { get; private init; }
    /// <summary>When result enrichment is enabled: optional client-facing hints from ResponseHintProvider.</summary>
    public IReadOnlyList<string>? Hints { get; private init; }

    public static McpToolResult Success(string content, string contentType = "application/json") =>
        new() { IsError = false, Content = content, ContentType = contentType };

    public static McpToolResult Error(string message) =>
        new() { IsError = true, Content = message, ContentType = "text/plain" };

    internal static McpToolResult SuccessWithEnrichment(
        string content,
        string contentType,
        int statusCode,
        double durationMs,
        string? correlationId,
        IReadOnlyList<McpSuggestedAction>? suggestedNextActions,
        IReadOnlyList<string>? hints) =>
        new()
        {
            IsError = false,
            Content = content,
            ContentType = contentType,
            StatusCode = statusCode,
            DurationMs = durationMs,
            CorrelationId = correlationId,
            SuggestedNextActions = suggestedNextActions,
            Hints = hints
        };

    internal static McpToolResult ErrorWithEnrichment(
        string message,
        int statusCode,
        double durationMs,
        string? correlationId,
        IReadOnlyList<McpSuggestedAction>? suggestedNextActions,
        IReadOnlyList<string>? hints) =>
        new()
        {
            IsError = true,
            Content = message,
            ContentType = "text/plain",
            StatusCode = statusCode,
            DurationMs = durationMs,
            CorrelationId = correlationId,
            SuggestedNextActions = suggestedNextActions,
            Hints = hints
        };

    internal static McpToolResult SuccessWithSuggested(string content, string contentType, IReadOnlyList<McpSuggestedAction> suggestedNextActions) =>
        new() { IsError = false, Content = content, ContentType = contentType, SuggestedNextActions = suggestedNextActions };

    internal static McpToolResult ErrorWithSuggested(string message, IReadOnlyList<McpSuggestedAction> suggestedNextActions) =>
        new() { IsError = true, Content = message, ContentType = "text/plain", SuggestedNextActions = suggestedNextActions };
}

/// <summary>Suggested follow-up tool and rationale for AI clients.</summary>
public sealed record McpSuggestedAction(string ToolName, string Rationale);
