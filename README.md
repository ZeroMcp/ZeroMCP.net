# ZeroMCP.net

Enterprise-grade MCP enablement for ASP.NET Core APIs.

ZeroMCP lets teams expose existing controller and minimal API endpoints as MCP (Model Context Protocol) tools, resources, templates, and prompts, without creating a second service or duplicating logic.

## Executive Summary

- **What it solves:** Connect LLM clients to established ASP.NET Core APIs safely and quickly.
- **How it works:** Annotate endpoints (`[Mcp]`, `[McpResource]`, `[McpTemplate]`, `[McpPrompt]`) or use minimal API metadata (`.AsMcp`, `.AsResource`, `.AsTemplate`, `.AsPrompt`), then map one MCP route.
- **Why enterprises adopt it:** Keeps existing auth, policy, validation, observability, and release controls in place.

## Core Capabilities

- In-process dispatch through your real ASP.NET Core pipeline
- Streamable HTTP MCP endpoint (`GET` and `POST`)
- Optional stdio transport for local/desktop MCP clients
- Tools, resources, templates, and prompts in one framework
- Per-tool governance (roles, policies, filters)
- Observability hooks (correlation, logs, metrics sink, OpenTelemetry tags)
- Inspector endpoints for discovery and controlled testing
- Versioned MCP routes for phased client migration

## Architecture at a Glance

1. API startup discovers MCP metadata from controllers and minimal APIs.
2. ZeroMCP builds schemas and endpoint descriptors.
3. MCP clients call `/mcp` using JSON-RPC methods.
4. ZeroMCP dispatches in-process to your original endpoint.
5. Response is normalized back into MCP-compatible output.

This model preserves middleware behavior and avoids "shadow implementations."

## Quick Start

### 1) Install package

```xml
<PackageReference Include="ZeroMCP" Version="1.*" />
```

### 2) Register and map

```csharp
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "Orders Platform";
    options.ServerVersion = "1.0.0";
});

var app = builder.Build();

app.MapControllers();
app.MapZeroMCP(); // registers GET+POST /mcp

app.Run();
```

### 3) Expose endpoints as MCP

```csharp
[HttpGet("{id:int}")]
[Mcp("get_order", Description = "Retrieves an order by ID.")]
public IActionResult GetOrder(int id) => Ok(new { id });

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .AsMcp("health_check", "Returns API health status.");
```

## Enterprise Deployment Guidance

### Security baseline

- Protect MCP endpoint using existing authentication/authorization:

```csharp
app.MapZeroMCP().RequireAuthorization("McpPolicy");
```

- Enforce least privilege with `Roles` and `Policy` on tool metadata.
- Keep inspector endpoints disabled or restricted outside trusted environments.
- Forward only required security headers via `ForwardHeaders`.

### Governance

- `ToolFilter`: discovery-time exclusion by name/environment.
- `ToolVisibilityFilter`: per-request dynamic visibility based on context.
- Roles/policies are enforced at listing and invocation boundaries.
- Use versioned routes to run controlled cutovers across client populations.

### Observability and operations

- Correlation IDs via configurable header propagation.
- Structured logging around MCP request lifecycle and tool calls.
- `IMcpMetricsSink` for custom telemetry export.
- Optional OpenTelemetry enrichment for traces.
- SSE-based keep-alive behavior for long-lived MCP connections.

### Reliability recommendations

- Set endpoint auth and rate limiting policies at the ASP.NET Core layer.
- Treat `/mcp` as a production API surface with normal SLO/SLA controls.
- Keep inspector UI behind environment checks or internal access controls.
- Validate key tool flows with integration tests before client rollout.

## Configuration Example

```csharp
builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "Orders Platform";
    options.ServerVersion = "2.3.0";
    options.RoutePrefix = "/mcp";

    // Core behavior
    options.IncludeInputSchemas = true;
    options.ForwardHeaders = ["Authorization"];

    // Governance
    options.ToolFilter = name => !name.StartsWith("internal_");
    options.ToolVisibilityFilter = (name, ctx) =>
        ctx.User.IsInRole("Admin") || !name.StartsWith("admin_");

    // Observability
    options.CorrelationIdHeader = "X-Correlation-ID";
    options.EnableOpenTelemetryEnrichment = true;

    // Optional MCP capabilities
    options.EnableResources = true;
    options.EnablePrompts = true;
    options.EnableToolInspector = false;
    options.EnableToolInspectorUI = false;
});
```

## Supported MCP Surface

- `initialize`
- `tools/list`, `tools/call`
- `resources/list`, `resources/templates/list`, `resources/read`
- `resources/subscribe`, `resources/unsubscribe` (when enabled)
- `prompts/list`, `prompts/get`
- notification flows such as list-changed updates (when enabled)

## Transport Options

### Streamable HTTP (default)

- `GET /mcp` for metadata and SSE scenarios
- `POST /mcp` for JSON-RPC methods

### stdio (optional)

```csharp
if (args.Contains("--mcp-stdio"))
{
    await app.RunMcpStdioAsync();
    return;
}
```

Useful for local-first MCP clients that spawn your service process directly.

## Inspector Endpoints

- `GET /mcp/tools`: JSON inventory of tools and schemas
- `GET /mcp/ui`: browser-based invocation UI

Recommended usage: enable in development and internal test environments only.

## Versioning and Compatibility

- Semantic versioning policy is defined in `VERSIONING.md`.
- MCP protocol behavior is implemented with explicit compatibility tests.
- Versioned endpoint support allows non-breaking migration paths for clients.

## Solution Layout

- `ZeroMCP/`: core framework package (NuGet artifact source)
- `ZeroMCP.Sample/`: reference host with practical patterns
- `ZeroMCP.Tests/`: integration and schema/compatibility tests
- `examples/`: focused scenario samples (auth, enrichment, enterprise, stdio)
- `wiki/`: implementation and operations documentation
- `progress.md`: persistent engineering change log

## Build and Test

```bash
dotnet build ZeroMCP.slnx -v detailed
dotnet test ZeroMCP.Tests/ZeroMCP.Tests.csproj -v detailed
```

## Documentation Map

- Package README: `ZeroMCP/README.md`
- Configuration: `wiki/Configuration.md`
- Security model: `wiki/Security-Model.md`
- Enterprise usage: `wiki/Enterprise-Usage.md`
- Tool versioning: `wiki/Tool-Versioning.md`
- Resources and prompts: `wiki/Resources-and-Prompts.md`

## Contributing

Contributions are welcome for enterprise hardening, protocol compatibility, and new sample implementations. Please include tests and documentation updates with each functional change.
