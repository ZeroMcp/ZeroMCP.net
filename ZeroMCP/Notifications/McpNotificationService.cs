using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace ZeroMCP.Notifications;

/// <summary>
/// Tracks active SSE sessions and broadcasts MCP notifications to connected clients.
/// Supports both list-changed broadcasts (all sessions) and resource-subscription
/// notifications (targeted to sessions subscribed to a specific URI).
///
/// Usage from application code:
/// <code>
/// // List changed (all sessions):
/// _toolDiscovery.InvalidateCache();
/// await _notificationService.NotifyToolsListChangedAsync();
///
/// // Resource updated (subscribed sessions only):
/// await _notificationService.NotifyResourceUpdatedAsync("catalog://products/42");
/// </code>
/// </summary>
public sealed class McpNotificationService
{
    private readonly ConcurrentDictionary<string, ChannelWriter<string>> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _subscriptions = new();
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

    // ----- Session lifecycle -----

    /// <summary>
    /// Registers a new SSE session. Returns a session ID that must be passed to
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
    /// Removes a session when the client disconnects. Also cleans up any resource
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

    /// <summary>Number of active SSE sessions currently connected.</summary>
    public int ActiveSessionCount => _sessions.Count;

    // ----- List-changed broadcasts (all sessions) -----

    /// <summary>Broadcasts <c>notifications/tools/list_changed</c> to all connected SSE clients.</summary>
    public Task NotifyToolsListChangedAsync() => BroadcastAllAsync("notifications/tools/list_changed");

    /// <summary>Broadcasts <c>notifications/resources/list_changed</c> to all connected SSE clients.</summary>
    public Task NotifyResourcesListChangedAsync() => BroadcastAllAsync("notifications/resources/list_changed");

    /// <summary>Broadcasts <c>notifications/prompts/list_changed</c> to all connected SSE clients.</summary>
    public Task NotifyPromptsListChangedAsync() => BroadcastAllAsync("notifications/prompts/list_changed");

    // ----- Resource subscriptions -----

    /// <summary>
    /// Subscribes an SSE session to notifications for the specified resource URI.
    /// </summary>
    public void SubscribeSession(string sessionId, string uri)
    {
        var subscribers = _subscriptions.GetOrAdd(uri, _ => new ConcurrentDictionary<string, byte>());
        subscribers[sessionId] = 0;
        _logger.LogDebug("Session {SessionId} subscribed to {Uri} ({Count} subscribers)",
            sessionId, uri, subscribers.Count);
    }

    /// <summary>
    /// Unsubscribes an SSE session from notifications for the specified resource URI.
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
    /// Sends <c>notifications/resources/updated</c> to all sessions subscribed to the
    /// specified URI. Sessions that are not subscribed are not notified. This is the
    /// public API the application calls when a resource's content changes.
    /// </summary>
    public Task NotifyResourceUpdatedAsync(string uri)
    {
        if (!_subscriptions.TryGetValue(uri, out var subscribers) || subscribers.IsEmpty)
            return Task.CompletedTask;

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

        _logger.LogInformation("Broadcast notifications/resources/updated for {Uri} to {Count} subscriber(s)",
            uri, subscribers.Count);
        return Task.CompletedTask;
    }

    /// <summary>Returns the number of sessions subscribed to a specific URI.</summary>
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

    private Task BroadcastAllAsync(string method)
    {
        if (_sessions.IsEmpty)
            return Task.CompletedTask;

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
        return Task.CompletedTask;
    }
}
