using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ZeroMCP.Discovery;
using ZeroMCP.Dispatch;

namespace ZeroMCP.Transport;

/// <summary>
/// Handles MCP prompt listing and retrieval by bridging the JSON-RPC calls
/// to the prompt discovery and dispatch infrastructure.
/// </summary>
internal sealed class McpPromptHandler
{
    private readonly McpPromptDiscoveryService _discovery;
    private readonly McpToolDispatcher _dispatcher;
    private readonly ILogger<McpPromptHandler> _logger;

    public McpPromptHandler(
        McpPromptDiscoveryService discovery,
        McpToolDispatcher dispatcher,
        ILogger<McpPromptHandler> logger)
    {
        _discovery = discovery;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>
    /// Handles the <c>prompts/list</c> JSON-RPC method.
    /// Returns all prompts registered with <c>[McpPrompt]</c>.
    /// </summary>
    public object HandlePromptsList()
    {
        var prompts = _discovery.GetPrompts().Select(p =>
        {
            var obj = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = p.Name
            };
            if (!string.IsNullOrEmpty(p.Description)) obj["description"] = p.Description;
            if (p.Arguments.Count > 0)
            {
                obj["arguments"] = p.Arguments.Select(a =>
                {
                    var argObj = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["name"] = a.Name,
                        ["required"] = a.Required
                    };
                    if (!string.IsNullOrEmpty(a.Description)) argObj["description"] = a.Description;
                    return (object)argObj;
                }).ToList();
            }
            return (object)obj;
        }).ToList();

        return new { prompts };
    }

    /// <summary>
    /// Handles the <c>prompts/get</c> JSON-RPC method.
    /// Dispatches the backing controller action and wraps the response as MCP prompt messages.
    /// </summary>
    public async Task<object> HandlePromptsGetAsync(
        JsonElement @params,
        HttpContext? sourceContext,
        CancellationToken cancellationToken)
    {
        if (@params.ValueKind == JsonValueKind.Undefined || !@params.TryGetProperty("name", out var nameEl))
            throw new McpInvalidParamsException("prompts/get requires params.name");

        var promptName = nameEl.GetString();
        if (string.IsNullOrEmpty(promptName))
            throw new McpInvalidParamsException("prompts/get params.name must be a non-empty string");

        var descriptor = _discovery.GetPrompt(promptName);
        if (descriptor is null)
        {
            _logger.LogWarning("MCP prompts/get: no prompt found for name={Name}", promptName);
            throw new McpInvalidParamsException($"No prompt found with name: {promptName}");
        }

        // Extract arguments from the params.arguments object
        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (@params.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in arguments.EnumerateObject())
                args[prop.Name] = prop.Value;
        }

        _logger.LogDebug("MCP prompts/get dispatching name={Name} → {Method} {Url}", promptName, descriptor.HttpMethod, descriptor.RelativeUrl);

        var result = await _dispatcher.DispatchAsync(descriptor.DispatchDescriptor, args, cancellationToken, sourceContext);

        // Wrap the action's response as a single user message.
        // The action may return a plain string, JSON, or any serialisable content.
        var messages = new[]
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["role"] = "user",
                ["content"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "text",
                    ["text"] = result.Content
                }
            }
        };

        var response = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["messages"] = messages
        };
        if (!string.IsNullOrEmpty(descriptor.Description))
            response["description"] = descriptor.Description;

        if (!result.IsSuccess)
        {
            _logger.LogWarning("MCP prompts/get action failed: name={Name}, status={Status}", promptName, result.StatusCode);
        }

        return response;
    }
}
