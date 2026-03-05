using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZeroMCP.Attributes;
using ZeroMCP.Options;
using ZeroMCP.Schema;
using ZeroMCP.Discovery;
using ZeroMCP.Metadata;

namespace ZeroMCP.Discovery;

/// <summary>
/// Discovers all [McpTool]-tagged controller actions at startup and builds the tool registry.
/// This runs once and the result is cached for the lifetime of the application.
/// Supports versioned endpoints: tools with Version set appear only on /mcp/v{Version}; unversioned tools appear on all endpoints.
/// </summary>
public sealed class McpToolDiscoveryService
{
    private readonly IApiDescriptionGroupCollectionProvider _apiDescriptionProvider;
    private readonly EndpointDataSource _endpointDataSource;
    private readonly McpSchemaBuilder _schemaBuilder;
    private readonly ZeroMCPOptions _options;
    private readonly ILogger<McpToolDiscoveryService> _logger;

    private sealed class ToolRegistry
    {
        public IReadOnlyList<McpToolDescriptor> AllTools { get; init; } = [];
        public IReadOnlyList<int> AvailableVersions { get; init; } = [];
        public bool HasVersionedTools { get; init; }
        public IReadOnlyDictionary<int, IReadOnlyList<McpToolDescriptor>> VersionBuckets { get; init; } = new Dictionary<int, IReadOnlyList<McpToolDescriptor>>();
        public IReadOnlyList<McpToolDescriptor> UnversionedTools { get; init; } = [];
        public IReadOnlyDictionary<string, McpToolDescriptor> NameToDescriptor { get; init; } = new Dictionary<string, McpToolDescriptor>(StringComparer.OrdinalIgnoreCase);
    }

    private ToolRegistry? _toolRegistry;
    private IReadOnlyDictionary<string, McpToolDescriptor>? _registryCache;
#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    public McpToolDiscoveryService(
        IApiDescriptionGroupCollectionProvider apiDescriptionProvider,
        EndpointDataSource endpointDataSource,
        McpSchemaBuilder schemaBuilder,
        IOptions<ZeroMCPOptions> options,
        ILogger<McpToolDiscoveryService> logger)
    {
        _apiDescriptionProvider = apiDescriptionProvider;
        _endpointDataSource = endpointDataSource;
        _schemaBuilder = schemaBuilder;
        _options = options.Value;
        _logger = logger;
    }

    private void EnsureBuilt()
    {
        if (_toolRegistry is not null) return;
        lock (_lock)
        {
            if (_toolRegistry is not null) return;
            var (registry, nameToDescriptor) = BuildRegistry();
            _toolRegistry = registry;
            _registryCache = nameToDescriptor;
        }
    }

    /// <summary>
    /// Returns the full registry of discovered MCP tools (name to descriptor). When multiple versions define the same tool name, one is chosen for this view.
    /// Built lazily on first access, then cached.
    /// </summary>
    public IReadOnlyDictionary<string, McpToolDescriptor> GetRegistry()
    {
        EnsureBuilt();
        return _registryCache!;
    }

    /// <summary>Returns all discovered tool descriptors.</summary>
    public IEnumerable<McpToolDescriptor> GetTools()
    {
        EnsureBuilt();
        return _toolRegistry!.AllTools;
    }

    /// <summary>Looks up a tool by name (backward compat; when multiple versions exist, returns the one from the registry view).</summary>
    public McpToolDescriptor? GetTool(string name)
    {
        GetRegistry().TryGetValue(name, out var descriptor);
        return descriptor;
    }

    /// <summary>True if any tool has a Version set, meaning versioned endpoints (/mcp/v1, etc.) will be registered.</summary>
    public bool HasVersionedTools
    {
        get { EnsureBuilt(); return _toolRegistry!.HasVersionedTools; }
    }

    /// <summary>Distinct version numbers from versioned tools, sorted ascending.</summary>
    public IReadOnlyList<int> GetAvailableVersions()
    {
        EnsureBuilt();
        return _toolRegistry!.AvailableVersions;
    }

    /// <summary>Returns tools for the given version endpoint: unversioned tools plus tools with this Version.</summary>
    public IReadOnlyList<McpToolDescriptor> GetToolsForVersion(int version)
    {
        EnsureBuilt();
        return _toolRegistry!.VersionBuckets.TryGetValue(version, out var list) ? list : _toolRegistry.UnversionedTools;
    }

    /// <summary>Returns tools for the default /mcp endpoint (configured default or highest version).</summary>
    public IReadOnlyList<McpToolDescriptor> GetToolsForDefaultEndpoint(int? configuredDefault)
    {
        EnsureBuilt();
        if (!_toolRegistry!.HasVersionedTools)
            return _toolRegistry.AllTools;
        var version = configuredDefault ?? (_toolRegistry.AvailableVersions.Count > 0 ? _toolRegistry.AvailableVersions[^1] : (int?)null);
        if (version is null) return _toolRegistry.UnversionedTools;
        return GetToolsForVersion(version.Value);
    }

    /// <summary>Looks up a tool by name scoped to a version endpoint. Returns null if the tool is not in that version's set.</summary>
    public McpToolDescriptor? GetTool(string name, int? version)
    {
        EnsureBuilt();
        if (version is null || !_toolRegistry!.HasVersionedTools)
            return GetTool(name);
        var tools = GetToolsForVersion(version.Value);
        return tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private (ToolRegistry registry, IReadOnlyDictionary<string, McpToolDescriptor> nameToDescriptor) BuildRegistry()
    {
        var allTools = new List<McpToolDescriptor>();

        var allDescriptions = _apiDescriptionProvider.ApiDescriptionGroups.Items
            .SelectMany(g => g.Items);

        foreach (var apiDescription in allDescriptions)
        {
            if (apiDescription.ActionDescriptor is not ControllerActionDescriptor controllerDescriptor)
                continue;

            var mcpAttr = controllerDescriptor.MethodInfo
                .GetCustomAttributes(typeof(McpAttribute), inherit: false)
                .FirstOrDefault() as McpAttribute;

            if (mcpAttr is null)
                continue;

            if (_options.ToolFilter is not null && !_options.ToolFilter(mcpAttr.Name))
            {
                _logger.LogDebug("Tool '{ToolName}' excluded by ToolFilter", mcpAttr.Name);
                continue;
            }

            var descriptor = BuildDescriptor(apiDescription, controllerDescriptor, mcpAttr);
            if (descriptor.Endpoint is null)
                _logger.LogWarning("No matching endpoint found for {Controller}.{Action}; CreatedAtAction/link generation may fail.",
                    controllerDescriptor.ControllerName, controllerDescriptor.ActionName);
            allTools.Add(descriptor);

            _logger.LogDebug(
                "Registered MCP tool '{ToolName}' → {HttpMethod} {RelativeUrl}",
                descriptor.Name,
                descriptor.HttpMethod,
                descriptor.RelativeUrl);
        }

        foreach (var endpoint in _endpointDataSource.Endpoints)
        {
            var mcpMeta = endpoint.Metadata.GetMetadata<McpToolEndpointMetadata>();
            if (mcpMeta is null) continue;

            if (_options.ToolFilter is not null && !_options.ToolFilter(mcpMeta.Name))
            {
                _logger.LogDebug("Tool '{ToolName}' excluded by ToolFilter", mcpMeta.Name);
                continue;
            }

            var minDescriptor = BuildMinimalApiDescriptor(endpoint, mcpMeta);
            allTools.Add(minDescriptor);

            _logger.LogDebug(
                "Registered MCP tool '{ToolName}' (minimal) → {HttpMethod} {RelativeUrl}",
                minDescriptor.Name,
                minDescriptor.HttpMethod,
                minDescriptor.RelativeUrl);
        }

        var unversioned = allTools.Where(d => d.Version is null).ToList();
        var versioned = allTools.Where(d => d.Version is not null).ToList();
        var availableVersions = versioned.Select(d => d.Version!.Value).Distinct().OrderBy(x => x).ToList();
        var hasVersionedTools = availableVersions.Count > 0;

        var versionBuckets = new Dictionary<int, IReadOnlyList<McpToolDescriptor>>();
        foreach (var v in availableVersions)
        {
            var versionSpecific = versioned.Where(d => d.Version == v).ToList();
            var merged = new List<McpToolDescriptor>(unversioned.Count + versionSpecific.Count);
            merged.AddRange(unversioned);
            merged.AddRange(versionSpecific);
            var deduped = DedupeByName(merged, v);
            versionBuckets[v] = deduped;
        }

        var nameToDescriptor = new Dictionary<string, McpToolDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in allTools)
            nameToDescriptor[d.Name] = d;

        _logger.LogInformation("ZeroMCP: discovered {Count} MCP tool(s)", allTools.Count);
        if (hasVersionedTools)
            _logger.LogInformation("ZeroMCP: versioned endpoints enabled for versions {Versions}", string.Join(", ", availableVersions));

        var registry = new ToolRegistry
        {
            AllTools = allTools,
            AvailableVersions = availableVersions,
            HasVersionedTools = hasVersionedTools,
            VersionBuckets = versionBuckets,
            UnversionedTools = unversioned,
            NameToDescriptor = nameToDescriptor
        };
        return (registry, nameToDescriptor);
    }

    private IReadOnlyList<McpToolDescriptor> DedupeByName(List<McpToolDescriptor> merged, int version)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<McpToolDescriptor>();
        foreach (var d in merged)
        {
            if (seen.Add(d.Name))
                result.Add(d);
            else
                _logger.LogWarning(
                    "Duplicate MCP tool name '{ToolName}' within version {Version} — skipping. Each tool name must be unique per version endpoint.",
                    d.Name, version);
        }
        return result;
    }

    private McpToolDescriptor BuildMinimalApiDescriptor(Endpoint endpoint, McpToolEndpointMetadata meta)
    {
        var routeParams = new List<McpParameterDescriptor>();
        var queryParams = new List<McpParameterDescriptor>();
        McpBodyDescriptor? body = null;
        var httpMethod = "GET";
        var relativeUrl = "";

        if (endpoint is RouteEndpoint routeEndpoint)
        {
            var pattern = routeEndpoint.RoutePattern;
            relativeUrl = pattern.RawText?.TrimStart('/') ?? "";
            foreach (var param in pattern.Parameters)
            {
                routeParams.Add(new McpParameterDescriptor
                {
                    Name = param.Name ?? "",
                    ParameterType = typeof(string),
                    IsRequired = !param.IsOptional,
                    Description = null
                });
            }
        }

        var methodMeta = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        if (methodMeta?.HttpMethods is { Count: > 0 })
            httpMethod = methodMeta.HttpMethods.First();

        // Match minimal API endpoint to ApiDescription for query/body params (AddEndpointsApiExplorer populates these)
        var apiDesc = FindApiDescriptionForMinimalEndpoint(relativeUrl, httpMethod);
        if (apiDesc is not null)
        {
            foreach (var param in apiDesc.ParameterDescriptions)
            {
                if (param.Type == typeof(CancellationToken))
                    continue;
                switch (param.Source.Id)
                {
                    case "Path":
                        // Already have from RoutePattern; skip to avoid duplicates
                        break;
                    case "Query":
                        queryParams.Add(new McpParameterDescriptor
                        {
                            Name = param.Name,
                            ParameterType = param.Type ?? typeof(string),
                            IsRequired = param.IsRequired,
                            Description = param.ModelMetadata?.Description,
                            DefaultValue = param.DefaultValue
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
        }

        var descriptor = new McpToolDescriptor
        {
            Name = meta.Name,
            Description = meta.Description,
            Tags = meta.Tags,
            Category = meta.Category,
            Examples = meta.Examples,
            Hints = meta.Hints,
            RequiredRoles = meta.Roles,
            RequiredPolicy = meta.Policy,
            Version = meta.Version, // int? in metadata; 0 or null = unversioned
            ApiDescription = null,
            ActionDescriptor = null,
            Endpoint = endpoint,
            RouteParameters = routeParams,
            QueryParameters = queryParams,
            Body = body,
            HttpMethod = httpMethod,
            RelativeUrl = relativeUrl
        };

        if (_options.IncludeInputSchemas)
            descriptor.InputSchemaJson = _schemaBuilder.BuildSchema(descriptor);

        return descriptor;
    }

    /// <summary>
    /// Finds the ApiDescription for a minimal API endpoint by matching RelativePath and HttpMethod.
    /// AddEndpointsApiExplorer populates ApiDescription for minimal APIs with ParameterDescriptions including Query and Body.
    /// </summary>
    private ApiDescription? FindApiDescriptionForMinimalEndpoint(string relativeUrl, string httpMethod)
    {
        var normalized = relativeUrl.TrimStart('/');
        foreach (var group in _apiDescriptionProvider.ApiDescriptionGroups.Items)
        {
            foreach (var desc in group.Items)
            {
                // Skip controller actions — they are processed separately
                if (desc.ActionDescriptor is ControllerActionDescriptor)
                    continue;
                var descPath = (desc.RelativePath ?? "").TrimStart('/');
                if (string.Equals(descPath, normalized, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(desc.HttpMethod ?? "", httpMethod, StringComparison.OrdinalIgnoreCase))
                    return desc;
            }
        }
        return null;
    }

    private McpToolDescriptor BuildDescriptor(
        ApiDescription apiDescription,
        ControllerActionDescriptor controllerDescriptor,
        McpAttribute mcpAttr)
    {
        var routeParams = new List<McpParameterDescriptor>();
        var queryParams = new List<McpParameterDescriptor>();
        McpBodyDescriptor? body = null;

        foreach (var param in apiDescription.ParameterDescriptions)
        {
            if (param.Type == typeof(CancellationToken))
                continue; // Infrastructure parameter; excluded from MCP schema
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
        // Use Description from the MCP Attribute, or, if EnableXMLDocAnalysis is set to true, read from XMLDoc
            Description = !string.IsNullOrWhiteSpace(mcpAttr.Description)
                ? mcpAttr.Description
                : _options.EnableXMLDocAnalysis?XmlDocHelper.GetMethodSummary(controllerDescriptor.MethodInfo):"",
            Tags = mcpAttr.Tags,
            Category = mcpAttr.Category,
            Examples = mcpAttr.Examples,
            Hints = mcpAttr.Hints,
            RequiredRoles = mcpAttr.Roles,
            RequiredPolicy = mcpAttr.Policy,
            Version = mcpAttr.Version <= 0 ? null : mcpAttr.Version,
            ApiDescription = apiDescription,
            ActionDescriptor = controllerDescriptor,
            Endpoint = FindEndpointForAction(controllerDescriptor),
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

    /// <summary>
    /// Finds the RouteEndpoint that corresponds to the given controller action so we can set it on the synthetic request.
    /// This avoids 500s when the action (e.g. CreatedAtAction) uses endpoint-aware services like LinkGenerator.
    /// </summary>
    private Endpoint? FindEndpointForAction(ControllerActionDescriptor controllerDescriptor)
    {
        foreach (var endpoint in _endpointDataSource.Endpoints)
        {
            var actionMeta = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (actionMeta is null) continue;
            // Prefer Id match; fallback to controller+action in case descriptor instances differ
            if (string.Equals(actionMeta.Id, controllerDescriptor.Id, StringComparison.Ordinal))
                return endpoint;
            if (string.Equals(actionMeta.ControllerName, controllerDescriptor.ControllerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(actionMeta.ActionName, controllerDescriptor.ActionName, StringComparison.OrdinalIgnoreCase))
                return endpoint;
        }
        return null;
    }
}
