# ZeroMCP

[![NuGet Version](https://img.shields.io/nuget/v/ZeroMCP.svg)](https://www.nuget.org/packages/ZeroMCP/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ZeroMCP.svg)](https://www.nuget.org/packages/ZeroMCP/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/ZeroMCP/ZeroMCP.net/blob/main/LICENSE)
[![Target Frameworks](https://img.shields.io/badge/.NET-8%2F9%2F10-512BD4)](https://dotnet.microsoft.com/)

Expose your ASP.NET Core API as an **MCP (Model Context Protocol)** server.

## Why ZeroMCP

- Reuse existing controller and minimal API endpoints
- Keep your current ASP.NET Core pipeline (auth, validation, filters, DI)
- Expose tools, resources, templates, and prompts from one service
- Support Streamable HTTP and stdio transports

**Full documentation** (configuration, governance, observability, minimal APIs, limitations): [repository README](https://github.com/ZeroMCP/ZeroMCP.net).

---

## Install

```xml
<PackageReference Include="ZeroMCP" Version="1.*" />
```

---

## Quick Start

**1. Register and map**

```csharp
// Program.cs
builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "My API";
    options.ServerVersion = "1.0.0";
});

// After UseRouting(), UseAuthorization()
app.MapZeroMCP();  // GET and POST /mcp; GET /mcp/tools and GET /mcp/ui when inspector/UI are enabled
```

**2. Tag controller actions**

```csharp
[HttpGet("{id}")]
[Mcp("get_order", Description = "Retrieves a single order by ID.")]
public ActionResult<Order> GetOrder(int id) { ... }

[HttpPost]
[Mcp("create_order", Description = "Creates a new order.")]
public ActionResult<Order> CreateOrder([FromBody] CreateOrderRequest request) { ... }
```

Common `[Mcp]` parameters:

```csharp
[Mcp(
    "tool_name",
    Description = "...",
    Tags = new[] { "tag" },
    Category = "orders",
    Examples = new[] { "Example call text" },
    Hints = new[] { "idempotent" },
    Roles = new[] { "Admin" },
    Policy = "PolicyName"
)]
```

**3. Optional: minimal APIs**

```csharp
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .AsMcp("health_check", "Returns API health status.");
```

**4. Optional: resources, templates, and prompts**

```csharp
// Controller attributes
[McpResource("system://status", "system_status", Description = "System status.")]
[McpTemplate("catalog://products/{id}", "product_resource", Description = "Product by ID.")]
[McpPrompt("search_products", Description = "Search products by keyword.")]

// Minimal API equivalents
app.MapGet("/api/status", () => ...).AsResource("system://status", "system_status", "System status.");
app.MapGet("/api/products/{id}", (int id) => ...).AsTemplate("catalog://products/{id}", "product_resource", "Product by ID.");
app.MapGet("/api/prompts/search", (string keyword) => ...).AsPrompt("search_products", "Search products.");
```

If you use **both** controllers and minimal APIs, add `builder.Services.AddEndpointsApiExplorer();` and `app.MapControllers();` so controller items are discovered.

Point any MCP client (e.g. Claude Desktop) at your app's `/mcp` URL.

### Optional stdio mode

For stdio-based clients, add a stdio branch before `app.Run()`:

```csharp
if (args.Contains("--mcp-stdio"))
{
    await app.RunMcpStdioAsync();
    return;
}
app.Run();
```

Claude Desktop / stdio client example:

```json
{
  "mcpServers": {
    "my-api": {
      "command": "dotnet",
      "args": ["run", "--project", "MyApiProject", "--", "--mcp-stdio"]
    }
  }
}
```

See `wiki/Connecting-Clients.md` for full client setup options.

---

## Configuration (summary)

| Option | Default | Description |
|--------|---------|-------------|
| `RoutePrefix` | `"/mcp"` | Endpoint path |
| `ServerName` / `ServerVersion` | — | Shown in MCP handshake |
| `IncludeInputSchemas` | `true` | Include JSON Schema in tools/list |
| `EnableXMLDocAnalysis` | `true` | Use XML doc summary as tool description when [Mcp] Description is blank |
| `ForwardHeaders` | `["Authorization"]` | Headers copied to tool dispatch |
| `ToolFilter` | `null` | Discovery-time filter by tool name |
| `ToolVisibilityFilter` | `null` | Per-request filter `(name, ctx) => bool` |
| `CorrelationIdHeader` | `"X-Correlation-ID"` | Request/response correlation ID |
| `EnableOpenTelemetryEnrichment` | `false` | Tag `Activity.Current` with MCP tool details |
| `EnableResultEnrichment` | `false` | tools/call result includes metadata, optional hints |
| `EnableSuggestedFollowUps` | `false` | Include suggestedNextActions when provider is set |
| `EnableStreamingToolResults` | `false` | Return content as chunks (chunkIndex, isFinal) |
| `StreamingChunkSize` | `4096` | Chunk size when streaming enabled |
| `EnableToolInspector` | `true` | GET {RoutePrefix}/tools returns full tool list as JSON |
| `EnableToolInspectorUI` | `true` | GET {RoutePrefix}/ui serves Swagger-like test invocation UI |
| `EnableResources` | `true` | Enable `resources/list`, `resources/read`, `resources/templates/list` |
| `EnablePrompts` | `true` | Enable `prompts/list`, `prompts/get` |
| `EnableLegacySseTransport` | `false` | Add GET /mcp/sse and POST /mcp/messages for MCP spec 2024-11-05 clients |
| `MaxFormFileSizeBytes` | `10485760` (10 MB) | Max size for base64-decoded form files; enforced before decode |
| `EnableListChangedNotifications` | `false` | Advertise `listChanged: true` and enable SSE push for list changes |
| `EnableResourceSubscriptions` | `false` | Advertise `subscribe: true` in resources; handle `resources/subscribe` / `resources/unsubscribe` |

Set `EnableToolInspector` or `EnableToolInspectorUI` to `false` to disable the JSON endpoint or the UI (e.g. in production if the list is sensitive). The sample app uses `builder.Environment.IsDevelopment()` to enable them only in Development.

**Governance:** Use `[Mcp(..., Roles = new[] { "Admin" }, Policy = "RequireEditor")]` or `.AsMcp(..., roles: ..., policy: ...)` to restrict which tools appear in `tools/list` per user.

**Metrics:** Implement `IMcpMetricsSink` and register it after `AddZeroMCP()` to record tool invocations (name, status code, duration, success/failure).

---

## Versioning

We follow [Semantic Versioning](https://semver.org/). Breaking changes are documented in the repository (e.g. `VERSIONING.md`).
