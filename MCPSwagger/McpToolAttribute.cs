namespace SwaggerMcp.Attributes;

/// <summary>
/// Marks an ASP.NET Core controller action as an MCP tool.
/// Only actions with this attribute will be exposed via the MCP endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpToolAttribute : Attribute
{
    /// <summary>
    /// The tool name exposed to the MCP client. Use snake_case by convention.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// A description of what this tool does. Shown to the LLM to help it decide when to use the tool.
    /// If not provided, falls back to XML doc comments on the action method.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional tags for grouping or filtering tools.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <param name="name">The tool name in snake_case (e.g. "get_order", "create_customer")</param>
    public McpToolAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
