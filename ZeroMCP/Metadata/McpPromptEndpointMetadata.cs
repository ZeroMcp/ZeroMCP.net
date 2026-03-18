namespace ZeroMCP.Metadata;

/// <summary>
/// Metadata attached to a minimal API endpoint to expose it as a reusable MCP prompt template.
/// Use <see cref="Extensions.McpToolEndpointExtensions.AsPrompt{TBuilder}"/> to attach this.
/// </summary>
public sealed class McpPromptEndpointMetadata
{
    /// <summary>Snake_case prompt name advertised in <c>prompts/list</c>.</summary>
    public string Name { get; }

    /// <summary>Optional description shown to the AI client.</summary>
    public string? Description { get; }

    public McpPromptEndpointMetadata(string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
    }
}
