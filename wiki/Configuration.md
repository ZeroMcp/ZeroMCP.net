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
