namespace ZeroMCP.Attributes;

/// <summary>
/// Marks an ASP.NET Core controller action as an MCP resource with a fixed URI.
/// The action is invoked when a client calls <c>resources/read</c> with the matching URI.
/// The response body of the action becomes the resource content.
/// </summary>
/// <example>
/// <code>
/// [McpResource("resource://myapp/config", "app_config", Description = "Returns application configuration")]
/// [HttpGet("config")]
/// public IActionResult GetConfig() => Ok(_config);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpResourceAttribute : Attribute
{
    /// <summary>The static URI clients use to identify and read this resource (e.g. "resource://myapp/config").</summary>
    public string Uri { get; }

    /// <summary>Human-readable name for this resource.</summary>
    public string Name { get; }

    /// <summary>Optional description advertised in resources/list.</summary>
    public string? Description { get; set; }

    /// <summary>Optional MIME type of the resource content (e.g. "application/json", "text/plain").</summary>
    public string? MimeType { get; set; }

    public McpResourceAttribute(string uri, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Uri = uri;
        Name = name;
    }
}
