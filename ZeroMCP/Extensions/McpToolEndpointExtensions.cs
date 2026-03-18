using Microsoft.AspNetCore.Routing;
using ZeroMCP.Metadata;

namespace ZeroMCP.Extensions;

/// <summary>
/// Extension methods for exposing minimal API endpoints as MCP tools, resources, and prompts.
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

    /// <summary>
    /// Marks this minimal API endpoint as a static MCP resource with a fixed, well-known URI.
    /// The endpoint will appear in <c>resources/list</c> and can be fetched via <c>resources/read</c>.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="uri">The fixed resource URI (e.g. <c>catalog://info</c>).</param>
    /// <param name="name">Snake_case resource name (e.g. <c>catalog_info</c>).</param>
    /// <param name="description">Optional description shown to the AI client.</param>
    /// <param name="mimeType">Optional MIME type of the response (e.g. <c>application/json</c>).</param>
    public static TBuilder AsResource<TBuilder>(
        this TBuilder builder,
        string uri,
        string name,
        string? description = null,
        string? mimeType = null)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new McpResourceEndpointMetadata(uri, name, description, mimeType));
        });
        return builder;
    }

    /// <summary>
    /// Marks this minimal API endpoint as an MCP resource template (RFC 6570 level-1 URI template).
    /// The endpoint will appear in <c>resources/templates/list</c> and can be fetched via
    /// <c>resources/read</c> with a matching URI — template variables are extracted and bound
    /// to route parameters by name.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="uriTemplate">RFC 6570 template (e.g. <c>catalog://products/{id}</c>).</param>
    /// <param name="name">Snake_case resource name.</param>
    /// <param name="description">Optional description shown to the AI client.</param>
    /// <param name="mimeType">Optional MIME type of the response.</param>
    public static TBuilder AsTemplate<TBuilder>(
        this TBuilder builder,
        string uriTemplate,
        string name,
        string? description = null,
        string? mimeType = null)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new McpTemplateEndpointMetadata(uriTemplate, name, description, mimeType));
        });
        return builder;
    }

    /// <summary>
    /// Marks this minimal API endpoint as a reusable MCP prompt template.
    /// The endpoint will appear in <c>prompts/list</c> and can be invoked via <c>prompts/get</c>.
    /// The endpoint should return a plain text string that becomes the prompt body.
    /// Route and query parameters are surfaced as prompt arguments.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="name">Snake_case prompt name (e.g. <c>summarise_order_prompt</c>).</param>
    /// <param name="description">Optional description shown to the AI client.</param>
    public static TBuilder AsPrompt<TBuilder>(
        this TBuilder builder,
        string name,
        string? description = null)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new McpPromptEndpointMetadata(name, description));
        });
        return builder;
    }
}
