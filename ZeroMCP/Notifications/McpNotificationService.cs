using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace ZeroMCP.Notifications;

/// <summary>
/// Tracks active notification streams and broadcasts MCP notifications.
/// Supports dual-era delivery:
/// <list type="bullet">
/// <item>Legacy: GET SSE sessions with optional <c>resources/subscribe</c></item>
/// <item>Modern (2026-07-28): <c>subscriptions/listen</c> streams with a notification filter</item>
/// </list>
/// </summary>
public sealed class McpNotificationService
{
    private readonly ConcurrentDictionary<string, ChannelWriter<string>> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _subscriptions = new();
    private readonly ConcurrentDictionary<string, ListenSubscription> _listenSubscriptions = new();
    private readonly ILogger<McpNotificationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public McpNotificationService(ILogger<McpNotificationService> logger)
    {
        _logger = logger;
    }

    // ----- Legacy session lifecycle -----

    /// <summary>
    /// Registers a new legacy SSE session. Returns a session ID that must be passed to
    /// <see cref="UnregisterSession"/> when the client disconnects.
    /// </summary>
    public string RegisterSession(ChannelWriter<string> writer)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        _sessions[sessionId] = writer;
        _logger.LogDebug("SSE session {SessionId} registered ({Count} active)", sessionId, _sessions.Count);
        return sessionId;
    }

    /// <summary>
    /// Removes a legacy session when the client disconnects. Also cleans up any resource
    /// subscriptions held by the session. Safe to call multiple times.
    /// </summary>
    public void UnregisterSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var writer))
        {
            writer.TryComplete();
            _logger.LogDebug("SSE session {SessionId} unregistered ({Count} active)", sessionId, _sessions.Count);
        }
        UnsubscribeAll(sessionId);
    }

    /// <summary>Number of active legacy SSE sessions currently connected.</summary>
    public int ActiveSessionCount => _sessions.Count;

    /// <summary>Number of active modern <c>subscriptions/listen</c> streams.</summary>
    public int ActiveListenSubscriptionCount => _listenSubscriptions.Count;

    // ----- Modern subscriptions/listen -----

    /// <summary>
    /// Registers a modern <c>subscriptions/listen</c> stream keyed by the JSON-RPC request id.
    /// </summary>
    public void RegisterListenSubscription(string subscriptionId, ChannelWriter<string> writer, McpListenFilter filter)
    {
        _listenSubscriptions[subscriptionId] = new ListenSubscription(writer, filter);
        _logger.LogDebug("Listen subscription {SubscriptionId} registered ({Count} active)",
            subscriptionId, _listenSubscriptions.Count);
    }

    /// <summary>Removes a modern listen subscription when the stream ends.</summary>
    public void UnregisterListenSubscription(string subscriptionId)
    {
        if (_listenSubscriptions.TryRemove(subscriptionId, out var sub))
        {
            sub.Writer.TryComplete();
            _logger.LogDebug("Listen subscription {SubscriptionId} unregistered ({Count} active)",
                subscriptionId, _listenSubscriptions.Count);
        }
    }

    // ----- List-changed broadcasts -----

    /// <summary>Broadcasts <c>notifications/tools/list_changed</c> to connected clients.</summary>
    public Task NotifyToolsListChangedAsync() => BroadcastListChangedAsync("notifications/tools/list_changed", f => f.ToolsListChanged);

    /// <summary>Broadcasts <c>notifications/resources/list_changed</c> to connected clients.</summary>
    public Task NotifyResourcesListChangedAsync() => BroadcastListChangedAsync("notifications/resources/list_changed", f => f.ResourcesListChanged);

    /// <summary>Broadcasts <c>notifications/prompts/list_changed</c> to connected clients.</summary>
    public Task NotifyPromptsListChangedAsync() => BroadcastListChangedAsync("notifications/prompts/list_changed", f => f.PromptsListChanged);

    // ----- Resource subscriptions (legacy + modern) -----

    /// <summary>
    /// Subscribes a legacy SSE session to notifications for the specified resource URI.
    /// </summary>
    public void SubscribeSession(string sessionId, string uri)
    {
        var subscribers = _subscriptions.GetOrAdd(uri, _ => new ConcurrentDictionary<string, byte>());
        subscribers[sessionId] = 0;
        _logger.LogDebug("Session {SessionId} subscribed to {Uri} ({Count} subscribers)",
            sessionId, uri, subscribers.Count);
    }

    /// <summary>
    /// Unsubscribes a legacy SSE session from notifications for the specified resource URI.
    /// </summary>
    public void UnsubscribeSession(string sessionId, string uri)
    {
        if (_subscriptions.TryGetValue(uri, out var subscribers))
        {
            subscribers.TryRemove(sessionId, out _);
            if (subscribers.IsEmpty)
                _subscriptions.TryRemove(uri, out _);
            _logger.LogDebug("Session {SessionId} unsubscribed from {Uri}", sessionId, uri);
        }
    }

    /// <summary>
    /// Sends <c>notifications/resources/updated</c> to legacy sessions and modern listen
    /// subscriptions that opted into the URI. This is the public API the application calls
    /// when a resource's content changes.
    /// </summary>
    public Task NotifyResourceUpdatedAsync(string uri)
    {
        // Legacy session subscriptions
        if (_subscriptions.TryGetValue(uri, out var subscribers) && !subscribers.IsEmpty)
        {
            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "notifications/resources/updated",
                @params = new { uri }
            }, JsonOptions);

            var deadSessions = new List<string>();
            foreach (var (sessionId, _) in subscribers)
            {
                if (_sessions.TryGetValue(sessionId, out var writer))
                {
                    if (!writer.TryWrite(payload))
                        deadSessions.Add(sessionId);
                }
                else
                {
                    deadSessions.Add(sessionId);
                }
            }

            foreach (var id in deadSessions)
            {
                subscribers.TryRemove(id, out _);
                if (_sessions.TryRemove(id, out var w))
                    w.TryComplete();
            }

            if (subscribers.IsEmpty)
                _subscriptions.TryRemove(uri, out _);

            _logger.LogInformation("Broadcast notifications/resources/updated for {Uri} to {Count} legacy subscriber(s)",
                uri, subscribers.Count);
        }

        // Modern listen subscriptions
        var deadListen = new List<string>();
        foreach (var (subId, sub) in _listenSubscriptions)
        {
            if (sub.Filter.ResourceSubscriptions is null
                || !sub.Filter.ResourceSubscriptions.Contains(uri, StringComparer.Ordinal))
                continue;

            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "notifications/resources/updated",
                @params = new Dictionary<string, object?>
                {
                    ["uri"] = uri,
                    ["_meta"] = new Dictionary<string, object?>
                    {
                        [McpProtocolConstants.MetaSubscriptionId] = ParseSubscriptionId(subId)
                    }
                }
            }, JsonOptions);

            if (!sub.Writer.TryWrite(payload))
                deadListen.Add(subId);
        }

        foreach (var id in deadListen)
            UnregisterListenSubscription(id);

        return Task.CompletedTask;
    }

    /// <summary>Returns the number of legacy sessions subscribed to a specific URI.</summary>
    public int GetSubscriberCount(string uri)
        => _subscriptions.TryGetValue(uri, out var s) ? s.Count : 0;

    // ----- Internals -----

    private void UnsubscribeAll(string sessionId)
    {
        foreach (var (uri, subscribers) in _subscriptions)
        {
            subscribers.TryRemove(sessionId, out _);
            if (subscribers.IsEmpty)
                _subscriptions.TryRemove(uri, out _);
        }
    }

    private Task BroadcastListChangedAsync(string method, Func<McpListenFilter, bool> listenPredicate)
    {
        if (!_sessions.IsEmpty)
        {
            var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", method }, JsonOptions);

            var deadSessions = new List<string>();
            foreach (var (sessionId, writer) in _sessions)
            {
                if (!writer.TryWrite(payload))
                    deadSessions.Add(sessionId);
            }

            foreach (var id in deadSessions)
            {
                if (_sessions.TryRemove(id, out var w))
                    w.TryComplete();
            }

            if (deadSessions.Count > 0)
                _logger.LogDebug("Removed {Count} dead SSE session(s) during {Method} broadcast", deadSessions.Count, method);

            _logger.LogInformation("Broadcast {Method} to {Count} SSE session(s)", method, _sessions.Count);
        }

        var deadListen = new List<string>();
        foreach (var (subId, sub) in _listenSubscriptions)
        {
            if (!listenPredicate(sub.Filter))
                continue;

            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method,
                @params = new Dictionary<string, object?>
                {
                    ["_meta"] = new Dictionary<string, object?>
                    {
                        [McpProtocolConstants.MetaSubscriptionId] = ParseSubscriptionId(subId)
                    }
                }
            }, JsonOptions);

            if (!sub.Writer.TryWrite(payload))
                deadListen.Add(subId);
        }

        foreach (var id in deadListen)
            UnregisterListenSubscription(id);

        return Task.CompletedTask;
    }

    private static object ParseSubscriptionId(string subscriptionId)
    {
        if (long.TryParse(subscriptionId, out var n))
            return n;
        return subscriptionId;
    }

    private sealed record ListenSubscription(ChannelWriter<string> Writer, McpListenFilter Filter);
}

/// <summary>
/// Notification filter for modern <c>subscriptions/listen</c> (MCP 2026-07-28).
/// </summary>
public sealed class McpListenFilter
{
    public bool ToolsListChanged { get; init; }
    public bool PromptsListChanged { get; init; }
    public bool ResourcesListChanged { get; init; }
    public IReadOnlyList<string>? ResourceSubscriptions { get; init; }

    public static McpListenFilter Parse(JsonElement paramsElement)
    {
        var tools = false;
        var prompts = false;
        var resources = false;
        List<string>? uris = null;

        if (paramsElement.ValueKind == JsonValueKind.Object
            && paramsElement.TryGetProperty("notifications", out var notifications)
            && notifications.ValueKind == JsonValueKind.Object)
        {
            if (notifications.TryGetProperty("toolsListChanged", out var t) && t.ValueKind is JsonValueKind.True or JsonValueKind.False)
                tools = t.GetBoolean();
            if (notifications.TryGetProperty("promptsListChanged", out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False)
                prompts = p.GetBoolean();
            if (notifications.TryGetProperty("resourcesListChanged", out var r) && r.ValueKind is JsonValueKind.True or JsonValueKind.False)
                resources = r.GetBoolean();
            if (notifications.TryGetProperty("resourceSubscriptions", out var rs) && rs.ValueKind == JsonValueKind.Array)
            {
                uris = [];
                foreach (var item in rs.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()))
                        uris.Add(item.GetString()!);
                }
            }
        }

        return new McpListenFilter
        {
            ToolsListChanged = tools,
            PromptsListChanged = prompts,
            ResourcesListChanged = resources,
            ResourceSubscriptions = uris
        };
    }

    /// <summary>
    /// Builds the acknowledged filter subset the server will honor.
    /// </summary>
    public Dictionary<string, object?> ToAcknowledgedObject(bool listChangedEnabled, bool resourceSubscribeEnabled)
    {
        var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (ToolsListChanged && listChangedEnabled)
            obj["toolsListChanged"] = true;
        if (PromptsListChanged && listChangedEnabled)
            obj["promptsListChanged"] = true;
        if (ResourcesListChanged && listChangedEnabled)
            obj["resourcesListChanged"] = true;
        if (ResourceSubscriptions is { Count: > 0 } && resourceSubscribeEnabled)
            obj["resourceSubscriptions"] = ResourceSubscriptions;
        return obj;
    }
}
