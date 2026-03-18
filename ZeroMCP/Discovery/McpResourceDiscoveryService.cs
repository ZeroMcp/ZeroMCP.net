using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ZeroMCP.Attributes;

namespace ZeroMCP.Discovery;

/// <summary>
/// Discovers all <c>[McpResource]</c> and <c>[McpTemplate]</c> attributed controller actions at startup
/// and builds the resource registry. Results are cached for the lifetime of the application.
/// </summary>
public sealed class McpResourceDiscoveryService
{
    private readonly IApiDescriptionGroupCollectionProvider _apiDescriptionProvider;
    private readonly EndpointDataSource _endpointDataSource;
    private readonly ILogger<McpResourceDiscoveryService> _logger;

    private IReadOnlyList<McpResourceDescriptor>? _staticResources;
    private IReadOnlyList<McpResourceDescriptor>? _templateResources;

#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    public McpResourceDiscoveryService(
        IApiDescriptionGroupCollectionProvider apiDescriptionProvider,
        EndpointDataSource endpointDataSource,
        ILogger<McpResourceDiscoveryService> logger)
    {
        _apiDescriptionProvider = apiDescriptionProvider;
        _endpointDataSource = endpointDataSource;
        _logger = logger;
    }

    private void EnsureBuilt()
    {
        if (_staticResources is not null) return;
        lock (_lock)
        {
            if (_staticResources is not null) return;
            BuildRegistry();
        }
    }

    /// <summary>All static resources (fixed URI), for resources/list.</summary>
    public IReadOnlyList<McpResourceDescriptor> GetStaticResources()
    {
        EnsureBuilt();
        return _staticResources!;
    }

    /// <summary>All URI-templated resources, for resources/templates/list.</summary>
    public IReadOnlyList<McpResourceDescriptor> GetTemplateResources()
    {
        EnsureBuilt();
        return _templateResources!;
    }

    /// <summary>
    /// Finds a resource descriptor whose URI or URI template matches the requested URI.
    /// Returns null if no match is found.
    /// </summary>
    public (McpResourceDescriptor? descriptor, IReadOnlyDictionary<string, string>? templateVars) FindForUri(string uri)
    {
        EnsureBuilt();

        // Check static resources first (exact match)
        foreach (var r in _staticResources!)
        {
            if (string.Equals(r.ResourceUri, uri, StringComparison.Ordinal))
                return (r, null);
        }

        // Check templates (pattern match)
        foreach (var t in _templateResources!)
        {
            if (t.UriPattern is null) continue;
            var match = t.UriPattern.Match(uri);
            if (!match.Success) continue;

            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var varName in t.TemplateVariables)
            {
                var group = match.Groups[varName];
                if (group.Success)
                    vars[varName] = Uri.UnescapeDataString(group.Value);
            }
            return (t, vars);
        }

        return (null, null);
    }

    private void BuildRegistry()
    {
        var staticList = new List<McpResourceDescriptor>();
        var templateList = new List<McpResourceDescriptor>();

        var allDescriptions = _apiDescriptionProvider.ApiDescriptionGroups.Items
            .SelectMany(g => g.Items);

        foreach (var apiDescription in allDescriptions)
        {
            if (apiDescription.ActionDescriptor is not ControllerActionDescriptor controllerDescriptor)
                continue;

            var resourceAttr = controllerDescriptor.MethodInfo
                .GetCustomAttributes(typeof(McpResourceAttribute), inherit: false)
                .FirstOrDefault() as McpResourceAttribute;

            var templateAttr = controllerDescriptor.MethodInfo
                .GetCustomAttributes(typeof(McpTemplateAttribute), inherit: false)
                .FirstOrDefault() as McpTemplateAttribute;

            if (resourceAttr is null && templateAttr is null)
                continue;

            var dispatchDescriptor = BuildDispatchDescriptor(apiDescription, controllerDescriptor);

            if (resourceAttr is not null)
            {
                var descriptor = new McpResourceDescriptor
                {
                    Name = resourceAttr.Name,
                    Description = resourceAttr.Description,
                    MimeType = resourceAttr.MimeType,
                    IsTemplate = false,
                    ResourceUri = resourceAttr.Uri,
                    HttpMethod = dispatchDescriptor.HttpMethod,
                    RelativeUrl = dispatchDescriptor.RelativeUrl,
                    DispatchDescriptor = dispatchDescriptor
                };
                staticList.Add(descriptor);
                _logger.LogDebug("Registered MCP resource '{Name}' → uri={Uri}", descriptor.Name, descriptor.ResourceUri);
            }

            if (templateAttr is not null)
            {
                var (pattern, variables) = CompileUriTemplate(templateAttr.UriTemplate);
                var descriptor = new McpResourceDescriptor
                {
                    Name = templateAttr.Name,
                    Description = templateAttr.Description,
                    MimeType = templateAttr.MimeType,
                    IsTemplate = true,
                    UriTemplate = templateAttr.UriTemplate,
                    UriPattern = pattern,
                    TemplateVariables = variables,
                    HttpMethod = dispatchDescriptor.HttpMethod,
                    RelativeUrl = dispatchDescriptor.RelativeUrl,
                    DispatchDescriptor = dispatchDescriptor
                };
                templateList.Add(descriptor);
                _logger.LogDebug("Registered MCP resource template '{Name}' → template={Template}", descriptor.Name, descriptor.UriTemplate);
            }
        }

        _staticResources = staticList;
        _templateResources = templateList;

        _logger.LogInformation(
            "ZeroMCP: discovered {StaticCount} MCP resource(s) and {TemplateCount} resource template(s)",
            staticList.Count, templateList.Count);
    }

    private McpToolDescriptor BuildDispatchDescriptor(
        ApiDescription apiDescription,
        ControllerActionDescriptor controllerDescriptor)
    {
        var routeParams = new List<McpParameterDescriptor>();
        var queryParams = new List<McpParameterDescriptor>();
        McpBodyDescriptor? body = null;

        foreach (var param in apiDescription.ParameterDescriptions)
        {
            if (param.Type == typeof(CancellationToken))
                continue;

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

        return new McpToolDescriptor
        {
            Name = controllerDescriptor.ActionName,
            ApiDescription = apiDescription,
            ActionDescriptor = controllerDescriptor,
            Endpoint = FindEndpointForAction(controllerDescriptor),
            RouteParameters = routeParams,
            QueryParameters = queryParams,
            Body = body,
            FormFileParameters = [],
            FormParameters = [],
            HttpMethod = apiDescription.HttpMethod ?? "GET",
            RelativeUrl = apiDescription.RelativePath ?? ""
        };
    }

    private Endpoint? FindEndpointForAction(ControllerActionDescriptor controllerDescriptor)
    {
        foreach (var endpoint in _endpointDataSource.Endpoints)
        {
            var actionMeta = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (actionMeta is null) continue;
            if (string.Equals(actionMeta.Id, controllerDescriptor.Id, StringComparison.Ordinal))
                return endpoint;
            if (string.Equals(actionMeta.ControllerName, controllerDescriptor.ControllerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(actionMeta.ActionName, controllerDescriptor.ActionName, StringComparison.OrdinalIgnoreCase))
                return endpoint;
        }
        return null;
    }

    /// <summary>
    /// Converts a Level-1 URI template (e.g. "resource://app/users/{id}") into a named-group regex
    /// and an ordered list of variable names. Literal portions are regex-escaped so characters like
    /// '.', '/', ':' are matched literally.
    /// </summary>
    private static (Regex pattern, IReadOnlyList<string> variables) CompileUriTemplate(string uriTemplate)
    {
        var variables = new List<string>();
        var sb = new System.Text.StringBuilder();
        sb.Append('^');

        var remaining = uriTemplate.AsSpan();
        while (!remaining.IsEmpty)
        {
            var open = remaining.IndexOf('{');
            if (open < 0)
            {
                sb.Append(Regex.Escape(remaining.ToString()));
                break;
            }

            // Escape the literal segment before the variable
            sb.Append(Regex.Escape(remaining[..open].ToString()));
            remaining = remaining[(open + 1)..];

            var close = remaining.IndexOf('}');
            if (close < 0)
            {
                // Malformed template — treat the rest as a literal
                sb.Append(Regex.Escape("{")).Append(Regex.Escape(remaining.ToString()));
                break;
            }

            var varName = remaining[..close].ToString();
            variables.Add(varName);
            sb.Append($"(?<{varName}>[^/?#]+)");
            remaining = remaining[(close + 1)..];
        }

        sb.Append('$');
        var pattern = new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
        return (pattern, variables);
    }
}
