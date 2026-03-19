# ZeroMCP

[![NuGet Version](https://img.shields.io/nuget/v/ZeroMCP.svg)](https://www.nuget.org/packages/ZeroMCP/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ZeroMCP.svg)](https://www.nuget.org/packages/ZeroMCP/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/ZeroMCP/ZeroMCP.net/blob/main/LICENSE)
[![Target Frameworks](https://img.shields.io/badge/.NET-8%2F9%2F10-512BD4)](https://dotnet.microsoft.com/)

ZeroMCP turns your existing ASP.NET Core APIs into an MCP (Model Context Protocol) server without creating a second app or duplicating business logic.

## Why ZeroMCP

- Reuse existing controllers and minimal APIs
- Expose MCP tools, resources, templates, and prompts from one codebase
- Keep your normal ASP.NET Core pipeline (auth, validation, filters, DI)
- Support Streamable HTTP and stdio transports for common MCP clients

## Install

```xml
<PackageReference Include="ZeroMCP" Version="1.*" />
```

## Quick Start

```csharp
// Program.cs
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "Orders API";
    options.ServerVersion = "1.0.0";
});

var app = builder.Build();

app.MapControllers();
app.MapZeroMCP(); // GET/POST /mcp

app.Run();
```

### Expose a tool from a controller

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id:int}")]
    [Mcp("get_order", Description = "Retrieves an order by ID.")]
    public IActionResult GetOrder(int id) => Ok(new { id, status = "pending" });
}
```

### Expose a tool from a minimal API

```csharp
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .AsMcp("health_check", "Returns API health status.");
```

## Features

- **Tools**: `[Mcp]` and `.AsMcp(...)`
- **Resources**: `[McpResource]` and `.AsResource(...)`
- **Resource templates**: `[McpTemplate]` and `.AsTemplate(...)`
- **Prompts**: `[McpPrompt]` and `.AsPrompt(...)`
- **Versioned MCP routes** when tool versions are defined
- **Streaming tool results** for `IAsyncEnumerable<T>` actions
- **Inspector endpoints** for discovery and UI testing

## Configuration (common options)

| Option | Default | Description |
|---|---|---|
| `RoutePrefix` | `"/mcp"` | MCP endpoint base route |
| `ServerName` / `ServerVersion` | unset | Reported during MCP initialize |
| `IncludeInputSchemas` | `true` | Include JSON schema in tool definitions |
| `ForwardHeaders` | `["Authorization"]` | Headers copied into synthetic dispatch |
| `EnableResources` | `true` | Enable `resources/list` and `resources/read` |
| `EnablePrompts` | `true` | Enable `prompts/list` and `prompts/get` |
| `EnableToolInspector` | `true` | Enable `GET {RoutePrefix}/tools` |
| `EnableToolInspectorUI` | `true` | Enable `GET {RoutePrefix}/ui` |
| `EnableResultEnrichment` | `false` | Add result metadata to `tools/call` |
| `EnableStreamingToolResults` | `false` | Return chunked content responses |
| `EnableListChangedNotifications` | `false` | Advertise and emit listChanged notifications |
| `EnableResourceSubscriptions` | `false` | Enable `resources/subscribe` and update notifications |

## Optional stdio mode

```csharp
if (args.Contains("--mcp-stdio"))
{
    await app.RunMcpStdioAsync();
    return;
}

app.Run();
```

### Client stdio setup (Claude and similar clients)

For stdio-based MCP clients, configure the client to launch your app with the `--mcp-stdio` switch.

```json
{
  "mcpServers": {
    "orders-api": {
      "command": "dotnet",
      "args": ["run", "--project", "ZeroMCP.Sample", "--", "--mcp-stdio"]
    }
  }
}
```

This same `command` + `args` pattern is used by most stdio clients (for example Claude Desktop and other MCP-compatible desktop/CLI tools).
For HTTP/remote MCP setup and additional client notes, see `wiki/Connecting-Clients.md`.

## Security and governance

- Protect MCP endpoints with normal ASP.NET Core auth:

```csharp
app.MapZeroMCP().RequireAuthorization("McpPolicy");
```

- Restrict tool visibility with role and policy metadata:
  - `[Mcp("admin_tool", Roles = new[] { "Admin" }, Policy = "RequireAdmin")]`
  - `.AsMcp("admin_tool", "Admin only", roles: new[] { "Admin" }, policy: "RequireAdmin")`

## Operational endpoints

- `GET /mcp`: server info or SSE stream (when `Accept: text/event-stream`)
- `POST /mcp`: MCP JSON-RPC methods (`initialize`, `tools/list`, `tools/call`, and others)
- `GET /mcp/tools`: full tool inspector JSON payload (when enabled)
- `GET /mcp/ui`: browser UI for tool invocation testing (when enabled)

## Learn More

- Repository and full docs: [ZeroMCP.net](https://github.com/ZeroMCP/ZeroMCP.net)
- Configuration details: [wiki/Configuration.md](https://github.com/ZeroMCP/ZeroMCP.net/blob/main/wiki/Configuration.md)
- Enterprise guidance: [wiki/Enterprise-Usage.md](https://github.com/ZeroMCP/ZeroMCP.net/blob/main/wiki/Enterprise-Usage.md)
- Versioning policy: [VERSIONING.md](https://github.com/ZeroMCP/ZeroMCP.net/blob/main/VERSIONING.md)
