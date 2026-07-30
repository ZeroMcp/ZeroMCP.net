using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using ZeroMCP.Notifications;
using ZeroMCP.Protocol;

namespace ZeroMCP.Transport;

internal sealed partial class McpHttpEndpointHandler
{
    private async Task<object?> DispatchMethodAsync(string method, JsonElement @params, HttpContext context, bool modern)
    {
        return method switch
        {
            "server/discover" => HandleServerDiscover(),
            "initialize" => HandleInitialize(@params),
            "notifications/initialized" => null,
            "tools/list" => await HandleToolsListAsync(context, modern),
            "tools/call" => await HandleToolsCallAsync(@params, context, _endpointVersion, null),
            "resources/list" => WrapListOrEmpty(
                _resourceHandler?.HandleResourcesList(),
                "resources",
                modern),
            "resources/templates/list" => WrapListOrEmpty(
                _resourceHandler?.HandleResourcesTemplatesList(),
                "resourceTemplates",
                modern),
            "resources/read" => _resourceHandler is not null
                ? await WrapCacheableAsync(
                    await _resourceHandler.HandleResourcesReadAsync(@params, context, context.RequestAborted),
                    modern)
                : throw new McpMethodNotFoundException($"Method not found: {method}"),
            "resources/subscribe" => modern
                ? throw new McpMethodNotFoundException("Method not found: resources/subscribe (use subscriptions/listen in MCP 2026-07-28)")
                : HandleResourceSubscribe(@params, context),
            "resources/unsubscribe" => modern
                ? throw new McpMethodNotFoundException("Method not found: resources/unsubscribe (use subscriptions/listen in MCP 2026-07-28)")
                : HandleResourceUnsubscribe(@params, context),
            "prompts/list" => WrapListOrEmpty(
                _promptHandler?.HandlePromptsList(),
                "prompts",
                modern),
            "prompts/get" => _promptHandler is not null
                ? await _promptHandler.HandlePromptsGetAsync(@params, context, context.RequestAborted)
                : throw new McpMethodNotFoundException($"Method not found: {method}"),
            _ => throw new McpMethodNotFoundException($"Method not found: {method}")
        };
    }

    private object HandleServerDiscover()
    {
        var capabilities = BuildServerCapabilities();

        var supported = _options.EnableLegacyProtocol
            ? McpProtocolConstants.SupportedProtocolVersions
            : [McpProtocolConstants.ModernProtocolVersion];

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["supportedVersions"] = supported,
            ["capabilities"] = capabilities,
            ["serverInfo"] = new
            {
                name = _options.ServerName ?? "ZeroMCP",
                version = _options.ServerVersion
            },
            ["ttlMs"] = _options.ListResultTtlMs,
            ["cacheScope"] = _options.ListResultCacheScope,
            ["instructions"] = "ZeroMCP dual-era server. Prefer protocol version 2026-07-28 with per-request _meta; legacy initialize remains available when EnableLegacyProtocol is true."
        };
    }

    private async Task HandleSubscriptionsListenAsync(JsonElement @params, HttpContext context, object? idValue)
    {
        var notificationService = _notificationService
            ?? throw new McpMethodNotFoundException("Method not found: subscriptions/listen (notifications are not enabled)");

        var filter = McpListenFilter.Parse(@params);
        var acknowledged = filter.ToAcknowledgedObject(
            listChangedEnabled: _options.EnableListChangedNotifications,
            resourceSubscribeEnabled: _options.EnableResourceSubscriptions);

        // Honor only what was acknowledged
        var honoredFilter = new McpListenFilter
        {
            ToolsListChanged = acknowledged.ContainsKey("toolsListChanged"),
            PromptsListChanged = acknowledged.ContainsKey("promptsListChanged"),
            ResourcesListChanged = acknowledged.ContainsKey("resourcesListChanged"),
            ResourceSubscriptions = acknowledged.TryGetValue("resourceSubscriptions", out var urisObj)
                && urisObj is IReadOnlyList<string> uris
                ? uris
                : null
        };

        var subscriptionId = idValue is null
            ? Guid.NewGuid().ToString("N")
            : idValue is string s
                ? s
                : idValue.ToString() ?? Guid.NewGuid().ToString("N");

        // Strip JSON quotes if Clone() produced a raw JSON string token representation elsewhere
        subscriptionId = subscriptionId.Trim('"');

        var channel = Channel.CreateUnbounded<string>();
        notificationService.RegisterListenSubscription(subscriptionId, channel.Writer, honoredFilter);

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        await context.Response.StartAsync(context.RequestAborted);

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

        try
        {
            var ack = new
            {
                jsonrpc = "2.0",
                method = "notifications/subscriptions/acknowledged",
                @params = new Dictionary<string, object?>
                {
                    ["notifications"] = acknowledged,
                    ["_meta"] = new Dictionary<string, object?>
                    {
                        [McpProtocolConstants.MetaSubscriptionId] = ParseJsonRpcId(subscriptionId, idValue)
                    }
                }
            };
            await WriteSseEventAsync(context, "message", JsonSerializer.Serialize(ack, jsonOptions));

            while (!context.RequestAborted.IsCancellationRequested)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    if (await channel.Reader.WaitToReadAsync(timeoutCts.Token)
                        && channel.Reader.TryRead(out var msg))
                    {
                        await WriteSseEventAsync(context, "message", msg);
                    }
                }
                catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
                {
                    await context.Response.WriteAsync(": keep-alive\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // client cancelled by closing the SSE stream
        }
        finally
        {
            // Graceful closure response when the server ends the stream
            try
            {
                if (!context.RequestAborted.IsCancellationRequested)
                {
                    var complete = new
                    {
                        jsonrpc = "2.0",
                        id = idValue,
                        result = new Dictionary<string, object?>
                        {
                            ["resultType"] = "complete",
                            ["_meta"] = new Dictionary<string, object?>
                            {
                                [McpProtocolConstants.MetaSubscriptionId] = ParseJsonRpcId(subscriptionId, idValue)
                            }
                        }
                    };
                    await WriteSseEventAsync(context, "message", JsonSerializer.Serialize(complete, jsonOptions));
                }
            }
            catch
            {
                // ignore write failures during teardown
            }

            notificationService.UnregisterListenSubscription(subscriptionId);
            channel.Writer.TryComplete();
        }
    }

    private object EnsureModernResultShape(object responsePayload, string method)
    {
        Dictionary<string, object?> dict;
        if (responsePayload is Dictionary<string, object?> existing)
        {
            dict = existing;
        }
        else
        {
            // Convert anonymous / other objects via JSON round-trip into a mutable dictionary
            var json = JsonSerializer.Serialize(responsePayload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
                   ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (!dict.ContainsKey("resultType"))
            dict["resultType"] = "complete";

        if (!dict.TryGetValue("_meta", out var metaObj) || metaObj is null)
        {
            dict["_meta"] = McpModernProtocol.CreateServerInfoMeta(_options.ServerName, _options.ServerVersion);
        }
        else if (metaObj is Dictionary<string, object?> metaDict)
        {
            if (!metaDict.ContainsKey(McpProtocolConstants.MetaServerInfo))
            {
                foreach (var (k, v) in McpModernProtocol.CreateServerInfoMeta(_options.ServerName, _options.ServerVersion))
                    metaDict[k] = v;
            }
        }
        else
        {
            // merge via JSON
            var metaJson = JsonSerializer.Serialize(metaObj);
            var metaDict2 = JsonSerializer.Deserialize<Dictionary<string, object?>>(metaJson)
                            ?? new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in McpModernProtocol.CreateServerInfoMeta(_options.ServerName, _options.ServerVersion))
                metaDict2.TryAdd(k, v);
            dict["_meta"] = metaDict2;
        }

        if (IsCacheableMethod(method))
        {
            dict.TryAdd("ttlMs", _options.ListResultTtlMs);
            dict.TryAdd("cacheScope", _options.ListResultCacheScope);
        }

        return dict;
    }

    private static bool IsCacheableMethod(string method)
        => method is "server/discover" or "tools/list" or "resources/list" or "resources/templates/list"
            or "resources/read" or "prompts/list";

    private object WrapListOrEmpty(object? handlerResult, string collectionKey, bool modern)
    {
        object payload = handlerResult ?? CreateEmptyCollection(collectionKey);
        if (!modern)
            return payload;

        if (payload is Dictionary<string, object?> dict)
        {
            dict.TryAdd("ttlMs", _options.ListResultTtlMs);
            dict.TryAdd("cacheScope", _options.ListResultCacheScope);
            return dict;
        }

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var d = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
                ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        d.TryAdd("ttlMs", _options.ListResultTtlMs);
        d.TryAdd("cacheScope", _options.ListResultCacheScope);
        return d;
    }

    private Task<object> WrapCacheableAsync(object payload, bool modern)
    {
        if (!modern)
            return Task.FromResult(payload);
        return Task.FromResult(WrapListOrEmpty(payload, "contents", modern: true));
    }

    private static object CreateEmptyCollection(string key)
        => key switch
        {
            "resources" => new { resources = Array.Empty<object>() },
            "resourceTemplates" => new { resourceTemplates = Array.Empty<object>() },
            "prompts" => new { prompts = Array.Empty<object>() },
            _ => new Dictionary<string, object?> { [key] = Array.Empty<object>() }
        };

    private static string? TryGetRequestedModernVersion(JsonElement root)
    {
        if (!McpModernProtocol.TryGetParamsMeta(root, out var meta))
            return null;
        if (!meta.TryGetProperty(McpProtocolConstants.MetaProtocolVersion, out var v)
            || v.ValueKind != JsonValueKind.String)
            return null;
        return v.GetString()?.Trim();
    }

    private static object ParseJsonRpcId(string subscriptionId, object? idValue)
    {
        if (idValue is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n))
                return n;
            if (el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? subscriptionId;
        }

        if (long.TryParse(subscriptionId, out var parsed))
            return parsed;
        return subscriptionId;
    }
}
