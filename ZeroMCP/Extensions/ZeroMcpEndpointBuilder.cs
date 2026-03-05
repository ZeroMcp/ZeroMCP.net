using Microsoft.AspNetCore.Routing;
using ZeroMCP.Options;
using ZeroMCP.Transport;

namespace ZeroMCP.Extensions;

/// <summary>
/// Builder returned by <see cref="EndpointRouteBuilderExtensions.MapZeroMCP"/> to allow chaining
/// extensions such as <see cref="ZeroMcpEndpointBuilderExtensions.WithLegacySseTransport"/>.
/// </summary>
public sealed class ZeroMcpEndpointBuilder : IEndpointConventionBuilder
{
    private readonly IEndpointConventionBuilder _inner;

    internal IEndpointRouteBuilder Endpoints { get; }
    internal string BaseRoute { get; }
    internal ZeroMCPOptions Options { get; }

    internal ZeroMcpEndpointBuilder(IEndpointConventionBuilder inner, IEndpointRouteBuilder endpoints, string baseRoute, ZeroMCPOptions options)
    {
        _inner = inner;
        Endpoints = endpoints;
        BaseRoute = baseRoute;
        Options = options;
    }

    /// <inheritdoc />
    public void Add(Action<EndpointBuilder> convention) => _inner.Add(convention);
}
