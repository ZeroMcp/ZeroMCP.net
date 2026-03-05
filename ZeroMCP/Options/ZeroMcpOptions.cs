using Microsoft.AspNetCore.Http;

namespace ZeroMCP.Options;

/// <summary>
/// Configuration options for the ZeroMCP middleware.
/// </summary>
public sealed class ZeroMCPOptions
{
    /// <summary>
    /// The route prefix for the MCP endpoint. Default is "/mcp".
    /// </summary>
    public string RoutePrefix { get; set; } = "/mcp";

    /// <summary>
    /// The server name advertised during MCP handshake. Defaults to the entry assembly name.
    /// </summary>
    public string? ServerName { get; set; }

    /// <summary>
    /// The server version advertised during MCP handshake. Defaults to "1.0.0".
    /// </summary>
    public string ServerVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Whether to include JSON Schema definitions in tool input descriptions.
    /// Enables the LLM to understand parameter types and constraints. Default is true.
    /// </summary>
    public bool IncludeInputSchemas { get; set; } = true;

    /// <summary>
    /// Optional predicate to further filter which [McpTool]-tagged actions are exposed at discovery time (by name only).
    /// Useful for environment-specific exclusions (e.g. exclude admin tools in non-production).
    /// </summary>
    public Func<string, bool>? ToolFilter { get; set; }

    /// <summary>
    /// Optional predicate to filter which tools are returned in tools/list per request.
    /// Receives the tool name and the current HTTP context (e.g. to check user, headers, or environment).
    /// Return true to include the tool, false to hide it. When null, no per-request filter is applied
    /// (role/policy filters on descriptors still apply).
    /// </summary>
    public Func<string, HttpContext, bool>? ToolVisibilityFilter { get; set; }

    /// <summary>
    /// Header names to forward from the incoming MCP request into the synthetic HttpContext
    /// when dispatching tool calls. Enables the dispatched action to see the same auth (e.g. Bearer token).
    /// Default is ["Authorization"]. Set to empty or null to disable forwarding.
    /// </summary>
    public IReadOnlyList<string>? ForwardHeaders { get; set; } = ["Authorization"];

    /// <summary>
    /// Request header name used to read and propagate a correlation ID. If present, the same value is echoed in the response and in logs.
    /// If absent, a new GUID is generated. Default is "X-Correlation-ID". Set to null or empty to disable correlation ID handling.
    /// </summary>
    public string? CorrelationIdHeader { get; set; } = "X-Correlation-ID";

    /// <summary>
    /// When true, tags the current <see cref="System.Diagnostics.Activity"/> (if any) with MCP tool invocation details
    /// (mcp.tool, mcp.status_code, mcp.is_error, mcp.duration_ms). Use with OpenTelemetry or similar. Default is false.
    /// </summary>
    public bool EnableOpenTelemetryEnrichment { get; set; }

    // --- Phase 2: Result enrichment ---

    /// <summary>
    /// When true, tools/call results include a metadata object (statusCode, contentType, correlationId, durationMs)
    /// and optional suggestedNextActions and hints for AI clients. Default is false.
    /// </summary>
    public bool EnableResultEnrichment { get; set; }

    /// <summary>
    /// When true and <see cref="SuggestedFollowUpsProvider"/> is set, tools/call results include suggested next tool calls with rationale. Default is false.
    /// </summary>
    public bool EnableSuggestedFollowUps { get; set; }

    /// <summary>
    /// Optional. When <see cref="EnableResultEnrichment"/> is true, invoked after each tool call to supply client-facing hints (e.g. "consider retry", "rate limited").
    /// Parameters: tool name, HTTP status code, response content, isError, HTTP context. Return null or empty to omit hints.
    /// </summary>
    public Func<string, int, string, bool, HttpContext?, IReadOnlyList<string>?>? ResponseHintProvider { get; set; }

    /// <summary>
    /// Optional. When <see cref="EnableSuggestedFollowUps"/> is true, invoked after each tool call to suggest follow-up tools.
    /// Parameters: tool name, HTTP status code, response content, isError, HTTP context. Return null or empty to omit suggestions.
    /// </summary>
    public Func<string, int, string, bool, HttpContext?, IReadOnlyList<ZeroMCPOptionsSuggestedAction>?>? SuggestedFollowUpsProvider { get; set; }

    // --- Phase 2: Streaming / partial responses ---

    /// <summary>
    /// When true, tools/call may return content as a sequence of chunks (chunkIndex, isFinal, text) for long responses.
    /// Default is false (single content block). When true, response content is split by <see cref="StreamingChunkSize"/> for compatibility with streaming-aware clients.
    /// </summary>
    public bool EnableStreamingToolResults { get; set; }

    /// <summary>
    /// When <see cref="EnableStreamingToolResults"/> is true, response content is split into chunks of this size (characters). Default is 4096.
    /// </summary>
    public int StreamingChunkSize { get; set; } = 4096;

    // --- Phase 3: Tool Inspector ---

    /// <summary>
    /// When true, registers a GET {RoutePrefix}/tools endpoint that returns the full tool registry as JSON for developer inspection.
    /// Does not apply per-request visibility (shows all registered tools). Default is true.
    /// </summary>
    public bool EnableToolInspector { get; set; } = true;

    /// <summary>
    /// When true will check XMLDoc descriptions if MCP Description is left blank. Default is true.
    /// </summary>
    public bool EnableXMLDocAnalysis { get; set; } = true;

    /// <summary>
    /// When true and <see cref="EnableToolInspector"/> is true, registers GET {RoutePrefix}/ui with a Swagger-like test invocation UI.
    /// The UI lists tools, shows input schemas, and lets you invoke tools/call from the browser. Default is true.
    /// </summary>
    public bool EnableToolInspectorUI { get; set; } = true;

    /// <summary>
    /// When set, the unversioned /mcp endpoint resolves to this version instead of the highest registered version.
    /// Default: null (auto = highest registered version). Useful during migration to control when clients get bumped.
    /// </summary>
    public int? DefaultVersion { get; set; }
}

/// <summary>Suggested follow-up tool and rationale for AI clients.</summary>
public sealed record ZeroMCPOptionsSuggestedAction(string ToolName, string Rationale);
