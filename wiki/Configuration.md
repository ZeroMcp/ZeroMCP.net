# Configuration

ZeroMcp is configured via **AddZeroMcp** options and **MapZeroMcp** (and optional route override).

---

## Options (AddZeroMcp)

```csharp
builder.Services.AddZeroMcp(options =>
{
    options.ServerName = "My API";              // shown during MCP handshake
    options.ServerVersion = "2.0.0";           // shown during MCP handshake
    options.RoutePrefix = "/mcp";              // where the endpoint is mounted
    options.IncludeInputSchemas = true;         // attach JSON Schema to tools (helps LLM)
    options.EnableXMLDocAnalysis = true;       // when true, use XML doc summary as tool description if [Mcp] Description is blank
    options.ForwardHeaders = ["Authorization"]; // copy from MCP request to tool dispatch

    // Optional: filter which tagged tools are exposed at discovery time (by name)
    options.ToolFilter = name => !name.StartsWith("admin_");

    // Optional: filter which tools appear in tools/list per request (e.g. by user, headers)
    options.ToolVisibilityFilter = (name, ctx) =>
        ctx.Request.Headers.TryGetValue("X-Show-Admin", out _) || !name.StartsWith("admin_");

    // Observability
    options.CorrelationIdHeader = "X-Correlation-ID";
    options.EnableOpenTelemetryEnrichment = true;

    // Phase 2 (optional, defaults shown)
    options.EnableResultEnrichment = false;   // include tools/call metadata + optional hints
    options.EnableSuggestedFollowUps = false; // include suggestedNextActions when provider is configured
    options.EnableStreamingToolResults = false; // split content into chunks for streaming-aware clients
    options.StreamingChunkSize = 4096;

    // Phase 3: Tool Inspector
    options.EnableToolInspector = true;         // GET {RoutePrefix}/tools returns full tool list as JSON
    options.EnableToolInspectorUI = true;       // GET {RoutePrefix}/ui serves test invocation UI (Swagger-like)
});
```

| Option | Default | Description |
|--------|---------|-------------|
| **ServerName** | — | Name shown in MCP handshake |
| **ServerVersion** | — | Version shown in handshake |
| **RoutePrefix** | `"/mcp"` | Path for GET/POST MCP endpoint |
| **IncludeInputSchemas** | `true` | Include JSON Schema in `tools/list` |
| **ForwardHeaders** | — | Header names copied to synthetic request (e.g. `Authorization`) |
| **ToolFilter** | `null` | Discovery-time filter by tool name |
| **ToolVisibilityFilter** | `null` | Per-request filter `(name, HttpContext) => bool` |
| **CorrelationIdHeader** | `"X-Correlation-ID"` | Header read/echoed; set to `null` to disable |
| **EnableOpenTelemetryEnrichment** | `false` | Tag `Activity.Current` with MCP fields |
| **EnableResultEnrichment** | `false` | Adds tools/call `metadata` and optional `hints` |
| **EnableSuggestedFollowUps** | `false` | Adds `suggestedNextActions` when provider returns values |
| **ResponseHintProvider** | `null` | Delegate for custom result hints |
| **SuggestedFollowUpsProvider** | `null` | Delegate returning follow-up tool suggestions |
| **EnableStreamingToolResults** | `false` | Returns chunked content blocks (`chunkIndex`, `isFinal`) |
| **StreamingChunkSize** | `4096` | Chunk size (characters) when streaming mode is enabled |
| **EnableToolInspector** | `true` | When true, registers GET {RoutePrefix}/tools with full tool registry (JSON). Set false to disable. |
| **EnableToolInspectorUI** | `true` | When true and inspector is enabled, registers GET {RoutePrefix}/ui with a Swagger-like test invocation UI (list tools, view schemas, invoke from browser). |
| **EnableXMLDocAnalysis** | `true` | When true, controller actions with `[Mcp]` but no explicit Description use the method's XML doc `<summary>` as the tool description. |

---

## Tool Inspector and UI

When **EnableToolInspector** is true:

- **GET {RoutePrefix}/tools** — Returns JSON with all registered tools (name, description, httpMethod, route, inputSchema, category, tags, examples, hints, requiredRoles, requiredPolicy). Use for debugging or tooling.
- **GET {RoutePrefix}/ui** — (When **EnableToolInspectorUI** is also true) Serves a test invocation page: browse tools (grouped by **category** when set), view input schemas, edit JSON arguments, and invoke `tools/call` from the browser. Link to "JSON (tools)" goes to the `/tools` endpoint.

Set **EnableToolInspector** or **EnableToolInspectorUI** to `false` to disable the JSON endpoint or the UI (e.g. in production if the list is sensitive). You can tie them to the environment: the sample app (**ZeroMCP.Sample**) enables both only when `builder.Environment.IsDevelopment()` is true.

---

## Custom route

Override the route when mapping:

```csharp
app.MapZeroMcp("/api/mcp");   // overrides options.RoutePrefix
```

---

## Using controllers and minimal APIs together

If you expose **both** controller actions with `[Mcp]` and minimal API endpoints with `.AsMcp(...)`, you must register the API explorer so controller tools are discovered:

```csharp
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();   // required for controller tool discovery
// ... AddZeroMcp(...) ...

app.MapControllers();
// minimal APIs with .AsMcp(...)
app.MapZeroMcp();
```

Without `AddEndpointsApiExplorer()`, only minimal API tools appear in `tools/list`.

---

## See also

- [Governance and Security](Governance-and-Security) — Roles, policy, visibility
- [Observability](Observability) — Logging, correlation ID, metrics
