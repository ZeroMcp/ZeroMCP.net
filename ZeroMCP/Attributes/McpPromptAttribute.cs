namespace ZeroMCP.Attributes;

/// <summary>
/// Marks an ASP.NET Core controller action as an MCP prompt.
/// The action's parameters become the prompt's arguments in <c>prompts/list</c>.
/// The action is invoked when a client calls <c>prompts/get</c>, and the response is
/// wrapped into MCP prompt messages returned to the client.
/// </summary>
/// <example>
/// <code>
/// [McpPrompt("summarise", Description = "Summarise a document by topic")]
/// [HttpGet("prompts/summarise")]
/// public IActionResult GetSummarisePrompt([FromQuery] string topic, [FromQuery] string? style = null)
///     => Ok($"Please summarise the document about {topic}" + (style != null ? $" in a {style} style." : "."));
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpPromptAttribute : Attribute
{
    /// <summary>Unique name for this prompt, used as the key in prompts/list and prompts/get.</summary>
    public string Name { get; }

    /// <summary>Optional description advertised in prompts/list.</summary>
    public string? Description { get; set; }

    public McpPromptAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
