using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwaggerMcp.Attributes;
using SwaggerMcp.Options;
using SwaggerMcp.Schema;

namespace SwaggerMcp.Discovery;

/// <summary>
/// Discovers all [McpTool]-tagged controller actions at startup and builds the tool registry.
/// This runs once and the result is cached for the lifetime of the application.
/// </summary>
public sealed class McpToolDiscoveryService
{
    private readonly IApiDescriptionGroupCollectionProvider _apiDescriptionProvider;
    private readonly McpSchemaBuilder _schemaBuilder;
    private readonly SwaggerMcpOptions _options;
    private readonly ILogger<McpToolDiscoveryService> _logger;

    // Lazy-initialized registry
    private IReadOnlyDictionary<string, McpToolDescriptor>? _registry;
    private readonly Lock _lock = new();

    public McpToolDiscoveryService(
        IApiDescriptionGroupCollectionProvider apiDescriptionProvider,
        McpSchemaBuilder schemaBuilder,
        IOptions<SwaggerMcpOptions> options,
        ILogger<McpToolDiscoveryService> logger)
    {
        _apiDescriptionProvider = apiDescriptionProvider;
        _schemaBuilder = schemaBuilder;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the full registry of discovered MCP tools.
    /// Built lazily on first access, then cached.
    /// </summary>
    public IReadOnlyDictionary<string, McpToolDescriptor> GetRegistry()
    {
        if (_registry is not null) return _registry;

        lock (_lock)
        {
            if (_registry is not null) return _registry;
            _registry = BuildRegistry();
        }

        return _registry;
    }

    /// <summary>Returns all discovered tool descriptors.</summary>
    public IEnumerable<McpToolDescriptor> GetTools() => GetRegistry().Values;

    /// <summary>Looks up a tool by name.</summary>
    public McpToolDescriptor? GetTool(string name)
    {
        GetRegistry().TryGetValue(name, out var descriptor);
        return descriptor;
    }

    private Dictionary<string, McpToolDescriptor> BuildRegistry()
    {
        var registry = new Dictionary<string, McpToolDescriptor>(StringComparer.OrdinalIgnoreCase);

        var allDescriptions = _apiDescriptionProvider.ApiDescriptionGroups.Items
            .SelectMany(g => g.Items);

        foreach (var apiDescription in allDescriptions)
        {
            // Must be a controller action
            if (apiDescription.ActionDescriptor is not ControllerActionDescriptor controllerDescriptor)
                continue;

            // Must have [McpTool]
            var mcpAttr = controllerDescriptor.MethodInfo
                .GetCustomAttributes(typeof(McpToolAttribute), inherit: false)
                .FirstOrDefault() as McpToolAttribute;

            if (mcpAttr is null)
                continue;

            // Apply optional filter
            if (_options.ToolFilter is not null && !_options.ToolFilter(mcpAttr.Name))
            {
                _logger.LogDebug("Tool '{ToolName}' excluded by ToolFilter", mcpAttr.Name);
                continue;
            }

            // Detect name collisions
            if (registry.ContainsKey(mcpAttr.Name))
            {
                _logger.LogWarning(
                    "Duplicate MCP tool name '{ToolName}' on {Controller}.{Action} — skipping. " +
                    "Each [McpTool] name must be unique.",
                    mcpAttr.Name,
                    controllerDescriptor.ControllerName,
                    controllerDescriptor.ActionName);
                continue;
            }

            var descriptor = BuildDescriptor(apiDescription, controllerDescriptor, mcpAttr);
            registry[descriptor.Name] = descriptor;

            _logger.LogDebug(
                "Registered MCP tool '{ToolName}' → {HttpMethod} {RelativeUrl}",
                descriptor.Name,
                descriptor.HttpMethod,
                descriptor.RelativeUrl);
        }

        _logger.LogInformation("SwaggerMcp: discovered {Count} MCP tool(s)", registry.Count);
        return registry;
    }

    private McpToolDescriptor BuildDescriptor(
        ApiDescription apiDescription,
        ControllerActionDescriptor controllerDescriptor,
        McpToolAttribute mcpAttr)
    {
        var routeParams = new List<McpParameterDescriptor>();
        var queryParams = new List<McpParameterDescriptor>();
        McpBodyDescriptor? body = null;

        foreach (var param in apiDescription.ParameterDescriptions)
        {
            switch (param.Source.Id)
            {
                case "Path":
                    routeParams.Add(new McpParameterDescriptor
                    {
                        Name = param.Name,
                        ParameterType = param.Type ?? typeof(string),
                        IsRequired = param.IsRequired,
                        Description = param.ModelMetadata?.Description
                    });
                    break;

                case "Query":
                    queryParams.Add(new McpParameterDescriptor
                    {
                        Name = param.Name,
                        ParameterType = param.Type ?? typeof(string),
                        IsRequired = param.IsRequired,
                        Description = param.ModelMetadata?.Description
                    });
                    break;

                case "Body":
                    body = new McpBodyDescriptor
                    {
                        BodyType = param.Type ?? typeof(object),
                        ParameterName = param.Name
                    };
                    break;
            }
        }

        var descriptor = new McpToolDescriptor
        {
            Name = mcpAttr.Name,
            Description = mcpAttr.Description,
            Tags = mcpAttr.Tags,
            ApiDescription = apiDescription,
            ActionDescriptor = controllerDescriptor,
            RouteParameters = routeParams,
            QueryParameters = queryParams,
            Body = body,
            HttpMethod = apiDescription.HttpMethod ?? "GET",
            RelativeUrl = apiDescription.RelativePath ?? string.Empty
        };

        // Build and attach JSON Schema
        if (_options.IncludeInputSchemas)
        {
            descriptor.InputSchemaJson = _schemaBuilder.BuildSchema(descriptor);
        }

        return descriptor;
    }
}
