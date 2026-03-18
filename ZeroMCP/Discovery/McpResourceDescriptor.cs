using System.Text.RegularExpressions;

namespace ZeroMCP.Discovery;

/// <summary>
/// Describes a single MCP resource (static URI) or resource template (URI template) discovered
/// from a <c>[McpResource]</c> or <c>[McpTemplate]</c> attributed controller action.
/// </summary>
public sealed class McpResourceDescriptor
{
    /// <summary>Human-readable display name advertised in resources/list and resources/templates/list.</summary>
    public string Name { get; init; } = "";

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Optional MIME type of the returned content.</summary>
    public string? MimeType { get; init; }

    /// <summary>True when this is a URI template; false for a static resource.</summary>
    public bool IsTemplate { get; init; }

    /// <summary>The static resource URI. Only set when <see cref="IsTemplate"/> is false.</summary>
    public string ResourceUri { get; init; } = "";

    /// <summary>The RFC 6570 URI template string. Only set when <see cref="IsTemplate"/> is true.</summary>
    public string? UriTemplate { get; init; }

    /// <summary>HTTP method of the backing action.</summary>
    public string HttpMethod { get; init; } = "GET";

    /// <summary>Relative URL of the backing action, for inspector display.</summary>
    public string RelativeUrl { get; init; } = "";

    /// <summary>
    /// Pre-compiled regex built from <see cref="UriTemplate"/> for matching incoming URIs during resources/read.
    /// Null for static resources.
    /// </summary>
    internal Regex? UriPattern { get; init; }

    /// <summary>Ordered list of variable names extracted from the URI template (e.g. ["id", "version"]).</summary>
    internal IReadOnlyList<string> TemplateVariables { get; init; } = [];

    /// <summary>Internal tool descriptor used to dispatch the action via McpToolDispatcher.</summary>
    internal McpToolDescriptor DispatchDescriptor { get; init; } = null!;
}
