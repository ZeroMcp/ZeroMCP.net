namespace ZeroMCP.Metadata;

/// <summary>
/// Metadata attached to a minimal API endpoint to expose it as an MCP resource template
/// (RFC 6570 level-1 URI template with <c>{variables}</c>). Use
/// <see cref="Extensions.McpToolEndpointExtensions.AsTemplate{TBuilder}"/> to attach this.
/// </summary>
public sealed class McpTemplateEndpointMetadata
{
    /// <summary>RFC 6570 level-1 URI template (e.g. <c>catalog://products/{id}</c>).</summary>
    public string UriTemplate { get; }

    /// <summary>Snake_case display name advertised in <c>resources/templates/list</c>.</summary>
    public string Name { get; }

    /// <summary>Optional description shown to the AI client.</summary>
    public string? Description { get; }

    /// <summary>Optional MIME type of the response content (e.g. <c>application/json</c>).</summary>
    public string? MimeType { get; }

    public McpTemplateEndpointMetadata(string uriTemplate, string name, string? description = null, string? mimeType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uriTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        UriTemplate = uriTemplate;
        Name = name;
        Description = description;
        MimeType = mimeType;
    }
}
