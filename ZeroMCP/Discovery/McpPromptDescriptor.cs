namespace ZeroMCP.Discovery;

/// <summary>
/// Describes a single MCP prompt discovered from a <c>[McpPrompt]</c> attributed controller action.
/// </summary>
public sealed class McpPromptDescriptor
{
    /// <summary>Unique prompt name, used as the key in prompts/list and prompts/get.</summary>
    public string Name { get; init; } = "";

    /// <summary>Optional description advertised in prompts/list.</summary>
    public string? Description { get; init; }

    /// <summary>Arguments derived from the action's route/query parameters.</summary>
    public IReadOnlyList<McpPromptArgumentDescriptor> Arguments { get; init; } = [];

    /// <summary>HTTP method of the backing action, for inspector display.</summary>
    public string HttpMethod { get; init; } = "GET";

    /// <summary>Relative URL of the backing action, for inspector display.</summary>
    public string RelativeUrl { get; init; } = "";

    /// <summary>Internal tool descriptor used to dispatch the action via McpToolDispatcher.</summary>
    internal McpToolDescriptor DispatchDescriptor { get; init; } = null!;
}

/// <summary>
/// A single argument accepted by an MCP prompt, derived from an action parameter.
/// </summary>
public sealed class McpPromptArgumentDescriptor
{
    /// <summary>Parameter name as advertised to the MCP client.</summary>
    public string Name { get; init; } = "";

    /// <summary>Optional description from <c>[Description]</c> or XML doc.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the argument must be supplied in prompts/get.</summary>
    public bool Required { get; init; }
}
