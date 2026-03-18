namespace ZeroMCP.Attributes;

/// <summary>
/// Marks an ASP.NET Core controller action as a parameterised MCP resource template (RFC 6570 Level 1).
/// The action is invoked when a client calls <c>resources/read</c> with a URI that matches the template.
/// Template variables (e.g. <c>{userId}</c>) must match the route or query parameter names on the action.
/// </summary>
/// <example>
/// <code>
/// [McpTemplate("resource://myapp/users/{id}", "get_user", Description = "Returns a user by ID")]
/// [HttpGet("users/{id}")]
/// public IActionResult GetUser(int id) => Ok(_repo.GetUser(id));
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpTemplateAttribute : Attribute
{
    /// <summary>
    /// RFC 6570 Level 1 URI template (e.g. "resource://myapp/users/{id}").
    /// Variables use <c>{name}</c> syntax and must correspond to action route or query parameters.
    /// </summary>
    public string UriTemplate { get; }

    /// <summary>Human-readable name for this resource template.</summary>
    public string Name { get; }

    /// <summary>Optional description advertised in resources/templates/list.</summary>
    public string? Description { get; set; }

    /// <summary>Optional MIME type of the resource content (e.g. "application/json", "text/plain").</summary>
    public string? MimeType { get; set; }

    public McpTemplateAttribute(string uriTemplate, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uriTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        UriTemplate = uriTemplate;
        Name = name;
    }
}
