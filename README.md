# ZeroMCP

Expose your existing ASP.NET Core API as an MCP (Model Context Protocol) server with a single attribute and two lines of setup. No separate process. No code duplication.

## How It Works

Tag controller actions with `[Mcp]` or minimal APIs with `.AsMcp(...)`. ZeroMCP will:

1. **Discover** tools at startup from controller API descriptions (same source as Swagger) and from minimal API endpoints that use `AsMcp`
2. **Generate** a JSON Schema for each tool's inputs (route, query, and body merged)
3. **Expose** a single endpoint (GET and POST `/mcp`) that speaks the MCP Streamable HTTP transport
4. **Dispatch** tool calls in-process through your real action or endpoint pipeline — filters, validation, and authorization run normally

```
MCP Client (Claude Desktop, Claude.ai, etc.)
    │
    │  GET /mcp (info)  or  POST /mcp (JSON-RPC 2.0)
    ▼
ZeroMCP Endpoint
    │
    │  in-process dispatch (controller or minimal endpoint)
    ▼
Your Action / Endpoint  ← [Mcp] or .AsMcp(...)
    │
    │  real response
    ▼
MCP Client gets structured result
```

---

## Quick Start

### 1. Install

```xml
<PackageReference Include="ZeroMCP" Version="1.*" />
```

### 2. Register services

```csharp
// Program.cs
builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "My Orders API";
    options.ServerVersion = "1.0.0";
});
```

### 3. Map the endpoint

```csharp
app.MapZeroMCP(); // registers GET and POST /mcp
```

### 4. Tag your actions

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id}")]
    [Mcp("get_order", Description = "Retrieves a single order by ID.")]
    public ActionResult<Order> GetOrder(int id) { ... }

    [HttpPost]
    [Mcp("create_order", Description = "Creates a new order. Returns the created order.")]
    public ActionResult<Order> CreateOrder([FromBody] CreateOrderRequest request) { ... }

    [HttpDelete("{id}")]
    // No [Mcp] — invisible to MCP clients
    public IActionResult Delete(int id) { ... }
}
```

Point any MCP client at your app's `/mcp` URL; it will see your tagged controller actions and minimal endpoints as tools.

ZeroMCP supports **HTTP** and **stdio** transports. For Claude Desktop and Claude Code (which default to stdio), add a stdio branch before `app.Run()`:

```csharp
if (args.Contains("--mcp-stdio"))
{
    await app.RunMcpStdioAsync();
    return;
}
app.Run();
```

Then configure Claude Desktop with `"command": "dotnet", "args": ["run", "--project", "MyApi", "--", "--mcp-stdio"]`. See [wiki/Connecting-Clients](wiki/Connecting-Clients.md).

- **GET /mcp** — Server info and example JSON-RPC payload.
- **GET /mcp/tools** — (Phase 3) JSON list of all registered tools and their schemas (when **EnableToolInspector** is true). Use for debugging or tooling.
- **GET /mcp/ui** — (Phase 3) Swagger-like test invocation UI: list tools, view schemas, invoke tools from the browser (when **EnableToolInspectorUI** is true).
- **POST /mcp** — JSON-RPC (`initialize`, `tools/list`, `tools/call`).
- **Legacy SSE** — Opt-in: `app.MapZeroMCP().WithLegacySseTransport()` adds GET `/mcp/sse` and POST `/mcp/messages` for MCP spec 2024-11-05 clients.

For **versioning and breaking-change policy**, see [VERSIONING.md](VERSIONING.md).

---

## Configuration

```csharp
builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "My API";         // shown during MCP handshake
    options.ServerVersion = "2.0.0";       // shown during MCP handshake
    options.RoutePrefix = "/mcp";          // where the endpoint is mounted
    options.IncludeInputSchemas = true;    // attach JSON Schema to tools (helps LLM)
    options.ForwardHeaders = ["Authorization"];  // copy these from MCP request to tool dispatch

    // Optional: filter which tagged tools are exposed at discovery time (by name)
    options.ToolFilter = name => !name.StartsWith("admin_");

    // Optional: filter which tools appear in tools/list per request (e.g. by user, headers)
    options.ToolVisibilityFilter = (name, ctx) => ctx.Request.Headers.TryGetValue("X-Show-Admin", out _) || !name.StartsWith("admin_");

    // Observability (Phase 1)
    options.CorrelationIdHeader = "X-Correlation-ID";  // read from request, echo in response and logs; default
    options.EnableOpenTelemetryEnrichment = true;     // tag Activity.Current with mcp.tool, mcp.duration_ms, etc.

    // Phase 2: result enrichment and streaming (all optional, default off)
    options.EnableResultEnrichment = true;            // tools/call result includes metadata (statusCode, durationMs, correlationId) and optional hints
    options.EnableSuggestedFollowUps = true;          // when SuggestedFollowUpsProvider is set, result includes suggested next tools
    options.EnableStreamingToolResults = false;       // when true, content is returned as chunks (chunkIndex, isFinal, text)
    options.StreamingChunkSize = 4096;

    // Phase 3: XML Doc and Inspector (defaults)
    options.EnableXMLDocAnalysis = true;   // when true, use XML doc <summary> as tool description if [Mcp] Description is blank
    options.EnableToolInspector = true;   // GET {RoutePrefix}/tools returns full tool list as JSON
    options.EnableToolInspectorUI = true; // GET {RoutePrefix}/ui serves Swagger-like test invocation UI
});
```

### Observability (Phase 1)

- **Structured logging** — Each MCP request is logged with a scope containing `CorrelationId`, `JsonRpcId`, and `Method`. Tool invocations log `ToolName`, `StatusCode`, `IsError`, `DurationMs`, and `CorrelationId`.
- **Execution timing** — Request duration and per-tool duration are recorded and included in log messages.
- **Correlation ID** — Send `X-Correlation-ID` (or the header name in `CorrelationIdHeader`) on the request; the same value is echoed in the response and propagated to the synthetic request (`TraceIdentifier` and `HttpContext.Items`). If omitted, a new GUID is generated.
- **Metrics sink** — Implement `IMcpMetricsSink` and register it after `AddZeroMCP()` to record tool invocations (tool name, status code, success/failure, duration). The default is a no-op.
- **OpenTelemetry** — Set `EnableOpenTelemetryEnrichment = true` to tag the current `Activity` with `mcp.tool`, `mcp.status_code`, `mcp.is_error`, `mcp.duration_ms`, and `mcp.correlation_id` when present.

### Governance & tool control (Phase 1)

You can control which tools appear in `tools/list` per request:

- **Role-based exposure** — On `[Mcp]` set `Roles = new[] { "Admin" }`. The tool is only listed if the current user is in at least one of the roles. Requires `AddAuthentication()` and `AddAuthorization()`.
- **Policy-based exposure** — Set `Policy = "RequireEditor"` (or any policy name). The tool is only listed if `IAuthorizationService.AuthorizeAsync(user, null, policy)` succeeds.
- **Environment / custom filter** — Use **`ToolFilter`** for discovery-time filtering by name (e.g. exclude `admin_*` in non-production). Use **`ToolVisibilityFilter`** for per-request filtering: `(toolName, httpContext) => bool` (e.g. hide tools based on user, headers, or feature flags).

Minimal APIs support the same via `.AsMcp("name", "description", tags: null, roles: new[] { "Admin" }, policy: "RequireEditor")`.

Tools that are hidden from `tools/list` are also not callable: a direct `tools/call` for that tool name will still be rejected (unknown tool). Authorization on the underlying action/endpoint is still enforced when the tool is invoked.

### Custom route

```csharp
app.MapZeroMCP("/api/mcp");  // overrides options.RoutePrefix
```

### Using controllers and minimal APIs together

If you expose **both** controller actions (with `[Mcp]`) and minimal API endpoints (with `.AsMcp(...)`), you must register the API explorer so controller actions are discovered:

```csharp
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();   // required for controller tool discovery
// ... AddZeroMCP(...) ...

app.MapControllers();
// minimal APIs with .AsMcp(...)
app.MapZeroMCP();
```

Without `AddEndpointsApiExplorer()`, only minimal API tools will appear in `tools/list`; controller actions will be missing because they are discovered from the same API description source as Swagger.

---

## Tool Inspector (Phase 3)

When **EnableToolInspector** is true (default), **GET {RoutePrefix}/tools** returns a JSON payload with `serverName`, `serverVersion`, `protocolVersion`, `toolCount`, and a `tools` array. Each tool entry includes `name`, `description`, `httpMethod`, `route`, `inputSchema`, and optional `category`, `tags`, `examples`, `hints`, `requiredRoles`, `requiredPolicy`. Use it for debugging or to build tooling.

When **EnableToolInspectorUI** is also true (default), **GET {RoutePrefix}/ui** serves a Swagger-like test invocation UI: you can browse tools, view input schemas, and invoke `tools/call` from the browser with editable JSON arguments.

Set **EnableToolInspector** or **EnableToolInspectorUI** to `false` to disable the JSON endpoint or the UI (e.g. in production if sensitive). The sample app (**ZeroMCP.Sample**) enables them only when `builder.Environment.IsDevelopment()` is true. See [wiki/Configuration](wiki/Configuration.md) and [wiki/Enterprise-Usage](wiki/Enterprise-Usage.md).

---

## Tool Versioning

You can expose tools on versioned endpoints so clients can target a specific API version. Use **Version** on **`[Mcp]`** or **`.AsMcp(..., version: n)`** for tools that differ by version; leave **Version** unset for tools that appear on all versions.

- **Without versioning** — Only **/mcp**, **/mcp/tools**, and **/mcp/ui** are registered (unchanged behaviour).
- **With versioning** — For each version (e.g. 1, 2) you get **/mcp/v1**, **/mcp/v2**, plus **/mcp/v1/tools**, **/mcp/v2/ui**, etc. The unversioned **/mcp** resolves to the highest version (or **DefaultVersion** in options).
- **Inspector** — The UI shows a version selector and version badges on tools when multiple versions exist.

See [wiki/Tool-Versioning](wiki/Tool-Versioning.md).

---

## Examples

The **examples/** folder contains five standalone projects:

| Example | Description |
|--------|-------------|
| **Minimal** | Bare-minimum: one controller action, one minimal API, no auth |
| **WithAuth** | API-key auth, role-based tool visibility, `[Authorize]` |
| **WithEnrichment** | Phase 2 result enrichment, suggested follow-ups, streaming options |
| **WithRateLimiting** | Phase 4 (Option A): ASP.NET Core rate limiting on the MCP endpoint, 429 + JSON-RPC error |
| **Enterprise** | Auth, enrichment, observability, ToolFilter, ToolVisibilityFilter |

Run any example with `dotnet run` from its folder. See each project's **README.md** for details.

---

## The `[Mcp]` Attribute

```csharp
[Mcp(
    name: "create_order",               // Required. Snake_case tool name for the LLM.
    Description = "Creates an order.",  // Shown to the LLM. Be descriptive.
    Tags = ["write", "orders"],         // Optional. For grouping/filtering.
    Category = "orders",                // Optional (Phase 2). Primary category for tools/list.
    Examples = ["Create order for Alice, 2 Widgets"], // Optional (Phase 2). Usage examples.
    Hints = ["idempotent", "cost=low"], // Optional (Phase 2). AI-facing hints.
    Roles = ["Editor", "Admin"],        // Optional. Tool only in tools/list if user in one of these roles.
    Policy = "RequireEditor"            // Optional. Tool only in tools/list if user satisfies this policy.
)]
```

### Placement rules

- **Per-action only** — `[Mcp]` goes on individual action methods, not controllers
- **One name per version** — duplicate names within the same version are logged and skipped; the same name in different versions (e.g. `get_order` in v1 and v2) is allowed. Without versioning, one name per application.
- **Any HTTP method** — GET, POST, PATCH, DELETE all work
- **Description** — If you omit `Description`, ZeroMCP uses the method's XML doc `<summary>` when available.

---

## How Parameters Are Mapped

ZeroMCP merges all parameter sources into a single flat JSON Schema object that the LLM fills in:

| Parameter source | MCP mapping |
|---|---|
| Route params (`{id}`) | Always required properties |
| Query params (`?status=`) | Optional (or required if `[Required]`) |
| `[FromBody]` object | Properties expanded inline from JSON Schema |

**Example:**

```csharp
[HttpPatch("{id}/status")]
[Mcp("update_order_status", Description = "Updates an order's status.")]
public IActionResult UpdateStatus(int id, [FromBody] UpdateStatusRequest req) { ... }

public class UpdateStatusRequest
{
    [Required] public string Status { get; set; }
    public string? Reason { get; set; }
}
```

Produces this MCP input schema:

```json
{
  "type": "object",
  "properties": {
    "id":     { "type": "integer" },
    "status": { "type": "string" },
    "reason": { "type": "string" }
  },
  "required": ["id", "status"]
}
```

---

## In-Process Dispatch

When the MCP client calls a tool, ZeroMCP:

1. Creates a fresh **DI scope** (same as a real request)
2. Builds a **synthetic `HttpContext`** with route values (including ambient `controller`/`action` for link generation), query string, and body from the JSON arguments
3. Sets the matched **endpoint** on the context so `CreatedAtAction` and `LinkGenerator` work
4. Invokes the controller action via `IActionInvokerFactory` or the minimal endpoint's `RequestDelegate`
5. Captures the response body and forwards it as the MCP result

This means:
- `[Authorize]` works — set up auth on the MCP endpoint and your action filters enforce it
- **Auth forwarding** — Headers in `ForwardHeaders` (e.g. `Authorization`) are copied from the MCP request to the synthetic request
- **CreatedAtAction** works — synthetic request has endpoint and controller/action route values so link generation succeeds
- `[ValidateModel]` / `ModelState` works — validation errors return as MCP error results
- Exception filters work — unhandled exceptions are caught and returned gracefully
- Your existing DI services, repositories, and business logic are called as-is

---

## Minimal API endpoints

You can expose minimal API endpoints as MCP tools by calling `.AsMcp(...)` when mapping:

```csharp
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .AsMcp("health_check", "Returns API health status.", tags: new[] { "system" });
```

- **Name** (required) — snake_case tool name for the LLM
- **Description** (optional) — shown to the LLM
- **Tags** (optional) — for grouping/filtering

Discovery includes both controller actions (from API descriptions) and minimal endpoints (from `EndpointDataSource`). Route parameters on minimal APIs are supported; query/body binding is limited to what the route pattern exposes.

---

## Connecting MCP Clients

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "my-api": {
      "type": "http",
      "url": "http://localhost:5000/mcp"
    }
  }
}
```

### Claude.ai (remote MCP)

Point at your deployed API's `/mcp` endpoint. For production, add authentication — ZeroMCP doesn't impose any auth on the `/mcp` route itself, so you can apply standard ASP.NET Core auth middleware or `.RequireAuthorization()` as needed:

```csharp
app.MapZeroMCP().RequireAuthorization("McpPolicy");
```

---

## Two READMEs

| File | Purpose |
|------|--------|
| **README.md** (this file) | Repository / GitLab: full docs, build, tests, contributing, project layout. |
| **ZeroMCP/README.md** | NuGet package: install, quick start, config summary. Shipped inside the package; keep it consumer-focused. |

When you add features or options, update both: details and examples here, short summary and link in `ZeroMCP/README.md`.

---

## Project Structure

```
mcpAPI/
├── ZeroMCP/                       ← Library (NuGet package ZeroMCP)
│   ├── README.md                  ← Package README (NuGet)
│   ├── Attributes/                ← [Mcp]
│   ├── Discovery/                 ← Controller + minimal API tool discovery
│   ├── Schema/                    ← JSON Schema for tool inputs (NJsonSchema)
│   ├── Dispatch/                  ← Synthetic HttpContext, controller/minimal invoke
│   ├── Metadata/                  ← McpToolEndpointMetadata for minimal APIs
│   ├── Extensions/                ← AddZeroMCP, MapZeroMCP, AsMcp
│   ├── Options/                   ← ZeroMCPOptions
│   └── ZeroMCP.csproj            (PackageId: ZeroMCP, Version: 1.0.2)
├── ZeroMCP.Sample/                ← Sample (Orders, Customer, Product APIs; nested route Customer/{id}/orders; health minimal endpoint, optional auth)
├── examples/                     ← Minimal, WithAuth, WithEnrichment, WithRateLimiting, Enterprise
├── ZeroMCP.Tests/                 ← Integration + schema tests
├── wiki/                          ← Wiki documentation (linked Markdown pages)
├── nupkgs/                        ← dotnet pack -o nupkgs
├── progress.md
└── README.md
```

**Wiki:** Detailed documentation can be found on [Our Wiki pages](https://github.com/ZeroMCP/ZeroMCP.net/wiki). 

---

## Known Limitations

- **Transports** — Streamable HTTP (primary), stdio via `--mcp-stdio`, Legacy SSE opt-in via `WithLegacySseTransport()`. See [wiki/Limitations](wiki/Limitations.md).
- **Minimal APIs** — supported via `AsMcp`; route params are bound; query/body binding is limited
- **[FromForm] and file uploads** — Supported for `IFormFile`/`IFormFileCollection` via base64; see [Parameters-and-Schemas](wiki/Parameters-and-Schemas.md)
- **Streaming responses** — `IAsyncEnumerable<T>` and SSE action results are not captured correctly
- If **CreatedAtAction** or link generation ever fails in your environment, use `return Created(Url.Action(nameof(OtherAction), new { id = entity.Id })!, entity);` as a fallback

---

## Build

- **Targets:** .NET 9.0 and .NET 10.0 (library); sample and tests may target a single framework.
- **Library:** `dotnet build ZeroMCP\ZeroMCP.csproj`
- **Sample:** `dotnet build ZeroMCP.Sample\ZeroMCP.Sample.csproj`
- **Tests:** `dotnet build ZeroMCP.Tests\ZeroMCP.Tests.csproj` then `dotnet test ZeroMCP.Tests\ZeroMCP.Tests.csproj`
- **TestService:** `dotnet build TestService\TestService.csproj`

### Test coverage

Integration and schema tests cover JSON-RPC validation and errors, model binding failures, wrong/empty arguments, unauthorized `[Authorize]` tool calls, `tools/list` schema shape, and schema edge cases (nested objects, arrays, enums, route+body merging).

---



## Contributing

PRs welcome. The most impactful next additions would be:

1. Richer minimal API parameter binding (query/body from route delegate)
