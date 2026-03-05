using Microsoft.AspNetCore.Routing;
using ZeroMCP.Metadata;

namespace ZeroMCP.Extensions;

/// <summary>
/// Extension methods for exposing minimal API endpoints as MCP tools.
/// </summary>
public static class McpToolEndpointExtensions
{
    /// <summary>
    /// Marks this endpoint as an MCP tool with the given name and optional description.
    /// The endpoint will appear in tools/list and can be invoked via tools/call.
    /// </summary>
    /// <param name="builder">The endpoint convention builder (e.g. from MapGet, MapPost).</param>
    /// <param name="name">Tool name in snake_case (e.g. "get_weather").</param>
    /// <param name="description">Optional description shown to the LLM.</param>
    /// <param name="tags">Optional tags for grouping.</param>
    /// <param name="roles">Optional role names; tool is only listed if the user is in at least one role.</param>
    /// <param name="policy">Optional authorization policy name; tool is only listed if the user satisfies the policy.</param>
    /// <param name="category">Optional primary category for this tool (e.g. \"orders\", \"customers\", \"system\").</param>
    /// <param name="examples">Optional free-form usage examples for this tool.</param>
    /// <param name="hints">Optional AI-facing hints or metadata strings.</param>
    /// <param name="version">Optional version number; tool is exposed only on /mcp/v{version}. Null means the tool appears on all version endpoints.</param>
    public static TBuilder AsMcp<TBuilder>(
        this TBuilder builder,
        string name,
        string? description = null,
        string[]? tags = null,
        string[]? roles = null,
        string? policy = null,
        string? category = null,
        string[]? examples = null,
        string[]? hints = null,
        int? version = null)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new McpToolEndpointMetadata(name, description, tags, roles, policy, category, examples, hints, version));
        });
        return builder;
    }
}
