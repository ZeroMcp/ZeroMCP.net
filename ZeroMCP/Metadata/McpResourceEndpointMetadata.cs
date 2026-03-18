namespace ZeroMCP.Metadata;

/// <summary>
/// Metadata attached to a minimal API endpoint to expose it as a static MCP resource
/// (fixed, well-known URI). Use <see cref="Extensions.McpToolEndpointExtensions.AsResource{TBuilder}"/>
/// to attach this to an endpoint.
/// </summary>
public sealed class McpResourceEndpointMetadata
{
    /// <summary>The fixed resource URI (e.g. <c>catalog://info</c>).</summary>
    public string Uri { get; }

    /// <summary>Snake_case display name advertised in <c>resources/list</c>.</summary>
    public string Name { get; }

    /// <summary>Optional description shown to the AI client.</summary>
    public string? Description { get; }

    /// <summary>Optional MIME type of the response content (e.g. <c>application/json</c>).</summary>
    public string? MimeType { get; }

    public McpResourceEndpointMetadata(string uri, string name, string? description = null, string? mimeType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Uri = uri;
        Name = name;
        Description = description;
        MimeType = mimeType;
    }
}
