using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZeroMCP.Options;
using ZeroMCP;
using ZeroMCP.Transport;

namespace ZeroMCP.Transport;

/// <summary>
/// Handles the streamable HTTP MCP transport protocol.
/// Implements the JSON-RPC 2.0 envelope that MCP uses over HTTP.
/// 
/// Supported methods:
///   initialize        — handshake, returns server capabilities
///   tools/list        — returns all registered tools
///   tools/call        — invokes a tool and returns its result
/// </summary>
internal sealed class McpHttpEndpointHandler
{
    internal const string CorrelationIdItemKey = "McpCorrelationId";

    private readonly McpToolHandler _toolHandler;
    private readonly McpResourceHandler? _resourceHandler;
    private readonly McpPromptHandler? _promptHandler;
    private readonly ZeroMCPOptions _options;
    private readonly ILogger<McpHttpEndpointHandler> _logger;
    private readonly int? _endpointVersion;
    private readonly IReadOnlyList<int> _availableVersions;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationRegistry = new();

    public McpHttpEndpointHandler(
        McpToolHandler toolHandler,
        ZeroMCPOptions options,
        ILogger<McpHttpEndpointHandler> logger,
        int? endpointVersion = null,
        IReadOnlyList<int>? availableVersions = null,
        McpResourceHandler? resourceHandler = null,
        McpPromptHandler? promptHandler = null)
    {
        _toolHandler = toolHandler;
        _resourceHandler = resourceHandler;
        _promptHandler = promptHandler;
        _options = options;
        _logger = logger;
        _endpointVersion = endpointVersion;
        _availableVersions = availableVersions ?? [];
    }

    public async Task HandleAsync(HttpContext context)
    {
        // GET: return a short description so the URL isn't blank in a browser
        if (context.Request.Method == "GET")
        {
            context.Response.ContentType = "application/json";
            var methods = new List<string> { "initialize", "tools/list", "tools/call" };
            if (_resourceHandler is not null) methods.AddRange(["resources/list", "resources/templates/list", "resources/read"]);
            if (_promptHandler is not null) methods.AddRange(["prompts/list", "prompts/get"]);
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                protocol = "MCP",
                transport = "streamable HTTP",
                message = $"Send POST requests with JSON-RPC 2.0 body. Methods: {string.Join(", ", methods)}.",
                server = _options.ServerName,
                version = _options.ServerVersion,
                example = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new { protocolVersion = McpProtocolConstants.ProtocolVersion, clientInfo = new { name = "client", version = "1.0" } }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
            return;
        }

        if (!context.Request.HasJsonContentType() && context.Request.Method == "POST")
        {
            context.Response.StatusCode = 415;
            await context.Response.WriteAsync("Content-Type must be application/json");
            return;
        }

        JsonDocument? requestDoc = null;

        try
        {
            requestDoc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        }
        catch
        {
            await WriteErrorAsync(context, null, -32700, "Parse error", null);
            return;
        }

        var root = requestDoc.RootElement;

        if (!root.TryGetProperty("jsonrpc", out var jsonrpc) || jsonrpc.GetString() != "2.0")
        {
            await WriteErrorAsync(context, null, -32600, "Invalid Request: missing jsonrpc 2.0", null);
            return;
        }

        root.TryGetProperty("id", out var id);
        var idValue = id.ValueKind == JsonValueKind.Undefined ? (object?)null : id.GetRawText();

        if (!root.TryGetProperty("method", out var methodEl))
        {
            await WriteErrorAsync(context, idValue, -32600, "Invalid Request: missing method", null);
            return;
        }

        var method = methodEl.GetString() ?? "";

        root.TryGetProperty("params", out var @params);

        // Correlation ID: from header or generate; set on context and response
        var correlationId = GetOrCreateCorrelationId(context);
        if (!string.IsNullOrEmpty(correlationId))
        {
            context.Items[CorrelationIdItemKey] = correlationId;
            var headerName = _options.CorrelationIdHeader;
            if (!string.IsNullOrEmpty(headerName))
                context.Response.OnStarting(() => { context.Response.Headers[headerName] = correlationId; return Task.CompletedTask; });
        }

        using (_logger.BeginScope("CorrelationId={CorrelationId}, JsonRpcId={JsonRpcId}, Method={Method}", correlationId ?? "", idValue?.ToString() ?? "", method))
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                // Streaming tool detection: if tools/call targets a streaming tool, use SSE output
                if (method == "tools/call" && await TryHandleStreamingToolCallAsync(@params, context, idValue, stopwatch))
                    return;

                object? responsePayload;
                if (method == "tools/call" && idValue is not null)
                {
                    var idStr = idValue is string s ? s : idValue.ToString() ?? "";
                    responsePayload = await HandleToolsCallWithCancellationAsync(@params, context, idStr);
                }
                else
                {
                    if (method == "notifications/cancelled")
                    {
                        await HandleCancelledAsync(@params);
                        responsePayload = null;
                    }
                    else
                    {
                        responsePayload = method switch
                        {
                            "initialize" => HandleInitialize(@params),
                            "notifications/initialized" => null, // fire and forget, no response
                            "tools/list" => await HandleToolsListAsync(context),
                            "tools/call" => await HandleToolsCallAsync(@params, context, _endpointVersion, null),
                            "resources/list" => _resourceHandler is not null
                                ? _resourceHandler.HandleResourcesList()
                                : throw new McpMethodNotFoundException($"Method not found: {method}"),
                            "resources/templates/list" => _resourceHandler is not null
                                ? _resourceHandler.HandleResourcesTemplatesList()
                                : throw new McpMethodNotFoundException($"Method not found: {method}"),
                            "resources/read" => _resourceHandler is not null
                                ? await _resourceHandler.HandleResourcesReadAsync(@params, context, context.RequestAborted)
                                : throw new McpMethodNotFoundException($"Method not found: {method}"),
                            "prompts/list" => _promptHandler is not null
                                ? _promptHandler.HandlePromptsList()
                                : throw new McpMethodNotFoundException($"Method not found: {method}"),
                            "prompts/get" => _promptHandler is not null
                                ? await _promptHandler.HandlePromptsGetAsync(@params, context, context.RequestAborted)
                                : throw new McpMethodNotFoundException($"Method not found: {method}"),
                            _ => throw new McpMethodNotFoundException($"Method not found: {method}")
                        };
                    }
                }

                if (responsePayload is null)
                {
                    context.Response.StatusCode = 204;
                    _logger.LogDebug("MCP request completed: Method={Method}, DurationMs={DurationMs}", method, stopwatch.ElapsedMilliseconds);
                    return;
                }

                await WriteResultAsync(context, idValue, responsePayload);
                _logger.LogDebug("MCP request completed: Method={Method}, DurationMs={DurationMs}", method, stopwatch.ElapsedMilliseconds);
            }
            catch (McpMethodNotFoundException ex)
            {
                _logger.LogWarning("MCP method not found: Method={Method}, DurationMs={DurationMs}", method, stopwatch.ElapsedMilliseconds);
                await WriteErrorAsync(context, idValue, -32601, ex.Message, null);
            }
            catch (McpInvalidParamsException ex)
            {
                _logger.LogWarning("MCP invalid params: Method={Method}, Message={Message}, DurationMs={DurationMs}", method, ex.Message, stopwatch.ElapsedMilliseconds);
                await WriteErrorAsync(context, idValue, -32602, ex.Message, null);
            }
            catch (OperationCanceledException)
            {
                await WriteErrorAsync(context, idValue, -32800, "Request cancelled", null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing MCP method: Method={Method}, DurationMs={DurationMs}", method, stopwatch.ElapsedMilliseconds);
                await WriteErrorAsync(context, idValue, -32603, "Internal error", ex.Message);
            }
        }
    }

    /// <summary>
    /// Checks whether a tools/call targets a streaming tool. Used by stdio to decide between single and multi-line output.
    /// </summary>
    internal bool IsStreamingToolCall(JsonDocument requestDoc)
    {
        var root = requestDoc.RootElement;
        if (!root.TryGetProperty("method", out var methodEl) || methodEl.GetString() != "tools/call")
            return false;
        if (!root.TryGetProperty("params", out var @params) || @params.ValueKind != JsonValueKind.Object)
            return false;
        if (!@params.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return false;
        return _toolHandler.IsStreamingTool(nameEl.GetString()!, _endpointVersion);
    }

    /// <summary>
    /// Processes a streaming tools/call and yields JSON-RPC lines for each chunk.
    /// Used by the stdio transport for streaming tools.
    /// </summary>
    internal async IAsyncEnumerable<string> ProcessStreamingMessageAsync(JsonDocument requestDoc, HttpContext context)
    {
        var root = requestDoc.RootElement;
        root.TryGetProperty("id", out var id);
        var idValue = id.ValueKind == JsonValueKind.Undefined ? (object?)null : id.GetRawText();
        root.TryGetProperty("params", out var @params);

        var toolName = @params.GetProperty("name").GetString()!;
        var descriptor = await _toolHandler.GetStreamingDescriptorAsync(toolName, context, context.RequestAborted, _endpointVersion);
        if (descriptor is null)
        {
            yield return SerializeErrorResponse(idValue, -32602, $"Tool '{toolName}' not found or not visible", null);
            yield break;
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (@params.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in arguments.EnumerateObject())
                args[prop.Name] = prop.Value;
        }

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
        var chunkIndex = 0;

        await foreach (var chunk in _toolHandler.StreamToolAsync(descriptor, args, context.RequestAborted, context))
        {
            if (chunk.IsLast && string.IsNullOrEmpty(chunk.Content) && !chunk.IsError)
            {
                var donePayload = new
                {
                    jsonrpc = "2.0",
                    id = idValue,
                    result = new
                    {
                        content = Array.Empty<object>(),
                        isError = false,
                        _meta = new { streaming = true, status = "done", totalChunks = chunkIndex }
                    }
                };
                yield return JsonSerializer.Serialize(donePayload, jsonOptions);
                yield break;
            }

            if (chunk.IsError)
            {
                var errorPayload = new
                {
                    jsonrpc = "2.0",
                    id = idValue,
                    result = new
                    {
                        content = new[] { new { type = "text", text = chunk.Content } },
                        isError = true,
                        _meta = new { streaming = true, status = "error", chunkIndex }
                    }
                };
                yield return JsonSerializer.Serialize(errorPayload, jsonOptions);
                yield break;
            }

            var chunkPayload = new
            {
                jsonrpc = "2.0",
                id = idValue,
                result = new
                {
                    content = new[] { new { type = "text", text = chunk.Content } },
                    isError = false,
                    _meta = new { streaming = true, status = "streaming", chunkIndex }
                }
            };
            yield return JsonSerializer.Serialize(chunkPayload, jsonOptions);
            chunkIndex++;
        }
    }

    /// <summary>
    /// Processes a single JSON-RPC message and returns the response as a JSON string.
    /// Used by both HTTP and stdio transports. Returns null for notifications (no response).
    /// </summary>
    internal async Task<string?> ProcessMessageAsync(JsonDocument requestDoc, HttpContext context)
    {
        var root = requestDoc.RootElement;

        if (!root.TryGetProperty("jsonrpc", out var jsonrpc) || jsonrpc.GetString() != "2.0")
            return SerializeErrorResponse(null, -32600, "Invalid Request: missing jsonrpc 2.0", null);

        root.TryGetProperty("id", out var id);
        var idValue = id.ValueKind == JsonValueKind.Undefined ? (object?)null : id.GetRawText();

        if (!root.TryGetProperty("method", out var methodEl))
            return SerializeErrorResponse(idValue, -32600, "Invalid Request: missing method", null);

        var method = methodEl.GetString() ?? "";
        root.TryGetProperty("params", out var @params);

        var correlationId = GetOrCreateCorrelationId(context);
        if (!string.IsNullOrEmpty(correlationId))
            context.Items[CorrelationIdItemKey] = correlationId;

        try
        {
            object? responsePayload;
            if (method == "tools/call" && idValue is not null)
            {
                var idStr = idValue is string s ? s : idValue.ToString() ?? "";
                responsePayload = await HandleToolsCallWithCancellationAsync(@params, context, idStr);
            }
            else
            {
                if (method == "notifications/cancelled")
                {
                    await HandleCancelledAsync(@params);
                    responsePayload = null;
                }
                else
                {
                    responsePayload = method switch
                    {
                        "initialize" => HandleInitialize(@params),
                        "notifications/initialized" => null,
                        "tools/list" => await HandleToolsListAsync(context),
                        "tools/call" => await HandleToolsCallAsync(@params, context, _endpointVersion, null),
                        "resources/list" => _resourceHandler is not null
                            ? _resourceHandler.HandleResourcesList()
                            : throw new McpMethodNotFoundException($"Method not found: {method}"),
                        "resources/templates/list" => _resourceHandler is not null
                            ? _resourceHandler.HandleResourcesTemplatesList()
                            : throw new McpMethodNotFoundException($"Method not found: {method}"),
                        "resources/read" => _resourceHandler is not null
                            ? await _resourceHandler.HandleResourcesReadAsync(@params, context, context.RequestAborted)
                            : throw new McpMethodNotFoundException($"Method not found: {method}"),
                        "prompts/list" => _promptHandler is not null
                            ? _promptHandler.HandlePromptsList()
                            : throw new McpMethodNotFoundException($"Method not found: {method}"),
                        "prompts/get" => _promptHandler is not null
                            ? await _promptHandler.HandlePromptsGetAsync(@params, context, context.RequestAborted)
                            : throw new McpMethodNotFoundException($"Method not found: {method}"),
                        _ => throw new McpMethodNotFoundException($"Method not found: {method}")
                    };
                }
            }

            if (responsePayload is null)
                return null;

            return SerializeResultResponse(idValue, responsePayload);
        }
        catch (McpMethodNotFoundException ex)
        {
            return SerializeErrorResponse(idValue, -32601, ex.Message, null);
        }
        catch (McpInvalidParamsException ex)
        {
            return SerializeErrorResponse(idValue, -32602, ex.Message, null);
        }
        catch (OperationCanceledException)
        {
            return SerializeErrorResponse(idValue, -32800, "Request cancelled", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing MCP method: Method={Method}", method);
            return SerializeErrorResponse(idValue, -32603, "Internal error", ex.Message);
        }
    }

    /// <summary>
    /// Handles GET {RoutePrefix}/tools — returns the full tool registry as JSON for developer inspection.
    /// </summary>
    public async Task HandleToolsInspectorAsync(HttpContext context)
    {
        var payload = _toolHandler.GetInspectorPayload(_endpointVersion, _availableVersions.Count > 0 ? _availableVersions : null);
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }

    private string? GetOrCreateCorrelationId(HttpContext context)
    {
        var headerName = _options.CorrelationIdHeader;
        if (string.IsNullOrEmpty(headerName)) return null;
        if (context.Request.Headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.ToString().Trim();
        return Guid.NewGuid().ToString("N");
    }

    private Task HandleCancelledAsync(JsonElement @params)
    {
        if (@params.ValueKind == JsonValueKind.Undefined) return Task.CompletedTask;
        if (!@params.TryGetProperty("requestId", out var idEl)) return Task.CompletedTask;
        var requestId = idEl.GetRawText().Trim('"');
        if (_cancellationRegistry.TryRemove(requestId, out var cts))
        {
            try { cts.Cancel(); } catch { /* ignore */ }
            cts.Dispose();
        }
        return Task.CompletedTask;
    }

    private static string SerializeResultResponse(object? id, object result)
    {
        var response = new { jsonrpc = "2.0", id, result };
        return JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false });
    }

    private static string SerializeErrorResponse(object? id, int code, string message, string? data)
    {
        var error = data is not null ? (object)new { code, message, data } : new { code, message };
        var response = new { jsonrpc = "2.0", id, error };
        return JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private object HandleInitialize(JsonElement @params)
    {
        var capabilities = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["tools"] = new { listChanged = false }
        };

        if (_resourceHandler is not null)
            capabilities["resources"] = new { listChanged = false, subscribe = false };

        if (_promptHandler is not null)
            capabilities["prompts"] = new { listChanged = false };

        return new
        {
            protocolVersion = McpProtocolConstants.ProtocolVersion,
            serverInfo = new { name = _options.ServerName, version = _options.ServerVersion },
            capabilities
        };
    }

    private async Task<object> HandleToolsListAsync(HttpContext context)
    {
        var list = await _toolHandler.GetToolDefinitionsAsync(context, context.RequestAborted, _endpointVersion);
        var tools = list.Select(t =>
        {
            // MCP standard: name, description, inputSchema. Phase 2: optional category, tags, examples, hints
            var obj = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = t.InputSchema
            };
            if (!string.IsNullOrEmpty(t.Category)) obj["category"] = t.Category;
            if (t.Tags is { Length: > 0 }) obj["tags"] = t.Tags;
            if (t.Examples is { Length: > 0 }) obj["examples"] = t.Examples;
            if (t.Hints is { Length: > 0 }) obj["hints"] = t.Hints;
            if (t.IsStreaming) obj["streaming"] = true;
            return (object)obj;
        }).ToList();
        return new { tools };
    }

    /// <summary>
    /// If the tools/call target is a streaming tool, writes SSE events and returns true.
    /// Returns false if the tool is non-streaming (caller should use the normal path).
    /// </summary>
    private async Task<bool> TryHandleStreamingToolCallAsync(JsonElement @params, HttpContext httpContext, object? idValue, Stopwatch stopwatch)
    {
        if (@params.ValueKind == JsonValueKind.Undefined) return false;
        if (!@params.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) return false;

        var toolName = nameEl.GetString()!;
        if (!_toolHandler.IsStreamingTool(toolName, _endpointVersion))
            return false;

        var descriptor = await _toolHandler.GetStreamingDescriptorAsync(toolName, httpContext, httpContext.RequestAborted, _endpointVersion);
        if (descriptor is null)
        {
            await WriteErrorAsync(httpContext, idValue, -32602, $"Tool '{toolName}' not found or not visible", null);
            return true;
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (@params.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in arguments.EnumerateObject())
                args[prop.Name] = prop.Value;
        }

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";
        httpContext.Response.StatusCode = 200;
        await httpContext.Response.StartAsync(httpContext.RequestAborted);

        var chunkIndex = 0;
        var hasError = false;

        await foreach (var chunk in _toolHandler.StreamToolAsync(descriptor, args, httpContext.RequestAborted, httpContext))
        {
            if (chunk.IsLast && string.IsNullOrEmpty(chunk.Content) && !chunk.IsError)
            {
                // Final empty sentinel: write the done event
                var donePayload = new
                {
                    jsonrpc = "2.0",
                    id = idValue,
                    result = new
                    {
                        content = Array.Empty<object>(),
                        isError = false,
                        _meta = new { streaming = true, status = "done", totalChunks = chunkIndex }
                    }
                };
                await WriteSseEventAsync(httpContext, "done", JsonSerializer.Serialize(donePayload, jsonOptions));
                break;
            }

            if (chunk.IsError)
            {
                hasError = true;
                var errorPayload = new
                {
                    jsonrpc = "2.0",
                    id = idValue,
                    result = new
                    {
                        content = new[] { new { type = "text", text = chunk.Content } },
                        isError = true,
                        _meta = new { streaming = true, status = "error", chunkIndex }
                    }
                };
                await WriteSseEventAsync(httpContext, "error", JsonSerializer.Serialize(errorPayload, jsonOptions));
                break;
            }

            var chunkPayload = new
            {
                jsonrpc = "2.0",
                id = idValue,
                result = new
                {
                    content = new[] { new { type = "text", text = chunk.Content } },
                    isError = false,
                    _meta = new { streaming = true, status = "streaming", chunkIndex }
                }
            };
            await WriteSseEventAsync(httpContext, "chunk", JsonSerializer.Serialize(chunkPayload, jsonOptions));
            chunkIndex++;
        }

        _logger.LogDebug("MCP streaming request completed: Tool={ToolName}, Chunks={ChunkCount}, HasError={HasError}, DurationMs={DurationMs}",
            toolName, chunkIndex, hasError, stopwatch.ElapsedMilliseconds);

        return true;
    }

    private static async Task WriteSseEventAsync(HttpContext context, string eventType, string data)
    {
        var sb = new StringBuilder();
        sb.Append("event: ").AppendLine(eventType);
        sb.Append("data: ").AppendLine(data);
        sb.AppendLine();
        await context.Response.WriteAsync(sb.ToString(), context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    private async Task<object> HandleToolsCallWithCancellationAsync(JsonElement @params, HttpContext httpContext, string requestId)
    {
        using var cts = new CancellationTokenSource();
        _cancellationRegistry[requestId] = cts;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted, cts.Token);
        try
        {
            return await HandleToolsCallAsync(@params, httpContext, _endpointVersion, linked.Token);
        }
        finally
        {
            _cancellationRegistry.TryRemove(requestId, out _);
        }
    }

    private async Task<object> HandleToolsCallAsync(JsonElement @params, HttpContext httpContext, int? endpointVersion, CancellationToken? cancellationToken)
    {
        if (@params.ValueKind == JsonValueKind.Undefined)
            throw new McpInvalidParamsException("tools/call requires params");

        if (!@params.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            throw new McpInvalidParamsException("tools/call requires params.name");

        var toolName = nameEl.GetString()!;

        // Extract arguments as a flat dictionary
        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (@params.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in arguments.EnumerateObject())
                args[prop.Name] = prop.Value;
        }

        var ct = cancellationToken ?? httpContext.RequestAborted;
        var result = await _toolHandler.HandleCallAsync(toolName, args, ct, httpContext, endpointVersion);

        // MCP tool result format (content + isError always; optional metadata/suggestedNextActions/hints when enrichment enabled)
        // When streaming enabled, content is split into chunks with chunkIndex and isFinal for partial-response clients
        object contentArray;
        if (_options.EnableStreamingToolResults && !string.IsNullOrEmpty(result.Content))
        {
            var chunkSize = _options.StreamingChunkSize > 0 ? _options.StreamingChunkSize : 4096;
            var chunks = new List<object>();
            for (var i = 0; i < result.Content.Length; i += chunkSize)
            {
                var chunk = result.Content.Length - i <= chunkSize
                    ? result.Content.Substring(i)
                    : result.Content.Substring(i, chunkSize);
                chunks.Add(new
                {
                    type = "text",
                    text = chunk,
                    chunkIndex = chunks.Count,
                    isFinal = i + chunk.Length >= result.Content.Length
                });
            }
            if (chunks.Count == 0)
                chunks.Add(new { type = "text", text = "", chunkIndex = 0, isFinal = true });
            contentArray = chunks;
        }
        else
        {
            contentArray = new[]
            {
                new
                {
                    type = result.ContentType.StartsWith("application/json") ? "text" : "text",
                    text = result.Content
                }
            };
        }

        if (result.StatusCode.HasValue || result.SuggestedNextActions is { Count: > 0 } || result.Hints is { Count: > 0 })
        {
            var payload = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["content"] = contentArray,
                ["isError"] = result.IsError
            };
            if (result.StatusCode.HasValue)
            {
                payload["metadata"] = new
                {
                    statusCode = result.StatusCode.Value,
                    contentType = result.ContentType,
                    correlationId = result.CorrelationId ?? (object?)null,
                    durationMs = result.DurationMs
                };
            }
            if (result.SuggestedNextActions is { Count: > 0 })
                payload["suggestedNextActions"] = result.SuggestedNextActions.Select(a => new { toolName = a.ToolName, rationale = a.Rationale }).ToList();
            if (result.Hints is { Count: > 0 })
                payload["hints"] = result.Hints;
            return payload;
        }

        return new
        {
            content = contentArray,
            isError = result.IsError
        };
    }

    private static async Task WriteResultAsync(HttpContext context, object? id, object result)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id,
            result
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        }));
    }

    private static async Task WriteErrorAsync(HttpContext context, object? id, int code, string message, string? data)
    {
        var error = data is not null
            ? (object)new { code, message, data }
            : new { code, message };

        var response = new { jsonrpc = "2.0", id, error };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200; // JSON-RPC errors still return HTTP 200
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}

internal sealed class McpMethodNotFoundException : Exception
{
    public McpMethodNotFoundException(string message) : base(message)
    {
    }
}

internal sealed class McpInvalidParamsException : Exception
{
    public McpInvalidParamsException(string message) : base(message)
    {
    }
}
