using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZeroMCP.Discovery;
using ZeroMCP.Options;

namespace ZeroMCP.Transport;

/// <summary>
/// Handles the deprecated MCP HTTP+SSE transport (spec 2024-11-05).
/// GET /sse establishes an SSE connection and sends an endpoint event.
/// POST /messages?sessionId= receives JSON-RPC and writes responses as SSE message events.
/// </summary>
internal sealed class McpLegacySseEndpointHandler
{
    private readonly ZeroMCPOptions _options;
    private readonly ILogger<McpLegacySseEndpointHandler> _logger;
    private readonly McpHttpEndpointHandler _mcpHandler;
    private static readonly ConcurrentDictionary<string, SseSession> Sessions = new();

    public McpLegacySseEndpointHandler(
        McpToolHandler toolHandler,
        McpToolDiscoveryService discovery,
        IOptions<ZeroMCPOptions> options,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<McpLegacySseEndpointHandler>();

        var endpointVersion = discovery.HasVersionedTools
            ? (_options.DefaultVersion ?? (discovery.GetAvailableVersions().Count > 0 ? discovery.GetAvailableVersions()[^1] : (int?)null))
            : (int?)null;
        var availableVersions = discovery.HasVersionedTools ? discovery.GetAvailableVersions() : [];
        var handlerLogger = loggerFactory.CreateLogger<McpHttpEndpointHandler>();
        _mcpHandler = new McpHttpEndpointHandler(toolHandler, _options, handlerLogger, endpointVersion, availableVersions);
    }

    /// <summary>
    /// Handles GET /sse — establishes SSE connection, sends endpoint event, holds connection for message responses.
    /// </summary>
    public async Task HandleSseConnectionAsync(HttpContext context)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var basePath = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "";
        var messagesPath = $"{basePath}{_options.RoutePrefix.TrimEnd('/')}/messages?sessionId={sessionId}";

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        var channel = Channel.CreateUnbounded<string>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

        var session = new SseSession(channel, cts);
        Sessions[sessionId] = session;

        try
        {
            context.RequestAborted.Register(() =>
            {
                Sessions.TryRemove(sessionId, out _);
                try { cts.Cancel(); } catch { /* ignore */ }
            });

            // Send endpoint event per MCP spec 2024-11-05
            await WriteSseEventAsync(context.Response, "endpoint", messagesPath);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            // Loop: read from channel and write message events until client disconnects
            await foreach (var msg in channel.Reader.ReadAllAsync(cts.Token))
            {
                await WriteSseEventAsync(context.Response, "message", msg);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal
        }
        finally
        {
            channel.Writer.Complete();
            Sessions.TryRemove(sessionId, out _);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Handles POST /messages?sessionId= — routes JSON-RPC through MCP handler, writes response as SSE message event.
    /// </summary>
    public async Task HandleMessagesAsync(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("sessionId", out var sessionIdValues) || sessionIdValues.Count == 0)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Missing sessionId query parameter");
            return;
        }

        var sessionId = sessionIdValues.ToString();
        if (!Sessions.TryGetValue(sessionId, out var session))
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Session not found or expired");
            return;
        }

        if (!context.Request.HasJsonContentType())
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
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Invalid JSON");
            return;
        }

        using (requestDoc)
        {
            var responseJson = await _mcpHandler.ProcessMessageAsync(requestDoc, context);
            if (responseJson is not null)
            {
                if (!session.Channel.Writer.TryWrite(responseJson))
                {
                    _logger.LogWarning("Session {SessionId} channel closed; client may have disconnected", sessionId);
                }
            }
        }

        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{}");
    }

    private static Task WriteSseEventAsync(HttpResponse response, string eventType, string data)
    {
        var payload = $"event: {eventType}\ndata: {data}\n\n";
        return response.WriteAsync(payload);
    }

    private sealed class SseSession
    {
        public System.Threading.Channels.Channel<string> Channel { get; }
        public CancellationTokenSource Cts { get; }

        public SseSession(Channel<string> channel, CancellationTokenSource cts)
        {
            Channel = channel;
            Cts = cts;
        }
    }
}
