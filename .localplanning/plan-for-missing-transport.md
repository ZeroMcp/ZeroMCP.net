# Feature Requests: Missing Transport & Input Types

This document contains individual feature requests for the transport and input-type gaps currently listed in ZeroMCP's Limitations. Each request is self-contained and can be filed as a separate GitHub issue.

---

## Table of Contents

1. [stdio Transport](#1-stdio-transport)
2. [Legacy SSE Transport (Backward Compatibility)](#2-legacy-sse-transport-backward-compatibility)
3. [Streaming Responses via IAsyncEnumerable\<T\>](#3-streaming-responses-via-iasyncenumerablet)
4. [CancellationToken / Request Cancellation](#4-cancellationtoken--request-cancellation)
5. [Multipart / File Upload Support (`[FromForm]`)](#5-multipart--file-upload-support-fromform)
6. [Minimal API Query and Body Binding Parity](#6-minimal-api-query-and-body-binding-parity)

---

## 1. stdio Transport

### Summary

Add an `stdio` transport mode so that ZeroMCP-powered APIs can be launched as local subprocesses, satisfying the MCP spec's `SHOULD support stdio` requirement and unlocking compatibility with Claude Desktop, Claude Code, VS Code extensions, and any other client that spawns MCP servers as child processes.

### Background

The MCP specification (2025-03-26) defines two standard transports: **Streamable HTTP** (what ZeroMCP currently implements) and **stdio**. The spec states that clients `SHOULD support stdio whenever possible` — meaning stdio is the expected baseline for local, developer-facing deployments.

ZeroMCP's core value proposition is **brownfield** — you already have an ASP.NET Core API, and you add `[Mcp]` to expose it. Today, that API must be a running HTTP server for MCP clients to connect. With stdio, the same application can be launched as a subprocess by a local client, which reads from stdin and writes to stdout. No HTTP server, no port, no firewall rules.

This matters because:

- **Claude Desktop** and **Claude Code** both default to stdio for local MCP servers. Users currently have to run `dotnet run` separately and configure an HTTP URL — a poor developer experience compared to every other MCP ecosystem package.
- **Developer tooling** (VS Code extensions, Copilot agents, local scripts) almost universally spawn MCP servers via stdio. Not supporting it makes ZeroMCP invisible to this entire category of client.
- **Zero-configuration local usage**: an AI-powered CLI tool written in .NET that wraps its own commands as MCP tools cannot currently use ZeroMCP — it has no web server to host.

### Proposed Design

#### Activation

Add a `UseStdioTransport()` method (or an equivalent `--mcp-stdio` CLI flag) that switches the host to stdio mode:
```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddZeroMcp(options =>
{
    options.ServerName = "My Orders API";
    options.ServerVersion = "1.0.0";
});

var app = builder.Build();
app.MapControllers();

// If launched with --mcp-stdio flag, enter stdio mode
if (args.Contains("--mcp-stdio"))
{
    await app.RunMcpStdioAsync();
    return;
}

app.Run();
```

`RunMcpStdioAsync()` is a new extension method on `WebApplication` (or `IHost`) that:

1. Starts the application host (so DI, middleware, the full ASP.NET pipeline are all initialised).
2. Opens `Console.OpenStandardInput()` and `Console.OpenStandardOutput()`.
3. Reads newline-delimited JSON-RPC messages from stdin in a loop.
4. Routes each message through the existing `McpMessageRouter`.
5. Writes the JSON-RPC response as a single line to stdout.
6. Exits cleanly when stdin closes (EOF).

#### Wire Protocol

The stdio transport uses the same JSON-RPC 2.0 messages as Streamable HTTP — `initialize`, `tools/list`, `tools/call`. Messages are newline-delimited UTF-8. The server MAY write log output to stderr; stdout is reserved exclusively for JSON-RPC responses.

#### Authentication

stdio servers run as subprocesses under the same user account as the client. There is no HTTP authentication context. `ForwardHeaders` is a no-op in stdio mode and should be silently ignored. Role- and policy-based tool visibility still works because the `User` identity can be set programmatically:
```csharp
options.StdioIdentity = new ClaimsPrincipal(
    new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "admin") }, "stdio")
);
```

#### Client Configuration

With stdio support, Claude Desktop configuration becomes:
```json
{
  "mcpServers": {
    "my-api": {
      "command": "dotnet",
      "args": ["run", "--project", "MyApi", "--", "--mcp-stdio"]
    }
  }
}
```

Or with a published binary:
```json
{
  "mcpServers": {
    "my-api": {
      "command": "C:\\MyApi\\MyApi.exe",
      "args": ["--mcp-stdio"]
    }
  }
}
```

### Implementation Notes

- The `RunMcpStdioAsync` loop should use `PipeReader` over stdin for efficient newline detection.
- stdout should be wrapped in a `StreamWriter` with `AutoFlush = true`.
- The Tool Inspector UI and `/mcp/tools` JSON endpoint should not be started in stdio mode.
- `Console.Error` should be used for all internal ZeroMCP diagnostic logging in stdio mode.
- Consider a `ZEROMCP_STDIO=1` environment variable as an alternative activation mechanism.

### Acceptance Criteria

- [ ] `RunMcpStdioAsync()` extension method on `WebApplication` / `IHost`
- [ ] `--mcp-stdio` CLI flag supported out of the box
- [ ] stdin reads newline-delimited JSON-RPC; stdout writes newline-delimited responses
- [ ] `initialize`, `tools/list`, `tools/call` all function correctly over stdio
- [ ] stderr used for all internal logging; stdout reserved for JSON-RPC only
- [ ] `StdioIdentity` option for fixed-identity stdio deployments
- [ ] `ForwardHeaders` silently ignored in stdio mode
- [ ] HTTP transport unaffected; both transports coexist in the same binary
- [ ] Claude Desktop example in Quick Start wiki updated
- [ ] Integration test: launch host in stdio mode, send JSON-RPC over pipes, assert response

---

## 2. Legacy SSE Transport (Backward Compatibility)

### Summary

Add opt-in support for the deprecated HTTP+SSE transport (MCP spec 2024-11-05) so that clients that have not yet migrated to Streamable HTTP can connect to ZeroMCP servers without requiring the client to upgrade.

### Background

The MCP spec deprecated the HTTP+SSE transport in March 2025 in favour of Streamable HTTP. ZeroMCP correctly implements Streamable HTTP. However:

- A significant portion of the MCP client ecosystem still uses the old SSE transport. Claude Desktop prior to certain versions, many community MCP clients, and enterprise tooling built during the 2024 rollout period all speak HTTP+SSE.
- Enterprise customers often cannot upgrade client tooling on a short cycle. A production ZeroMCP deployment that refuses connections from SSE clients creates a support burden.

### Proposed Design

#### Activation

SSE transport support is **opt-in** and disabled by default:
```csharp
app.MapZeroMCP()
   .WithLegacySseTransport();  // adds GET /mcp/sse and POST /mcp/messages
```

Or via options:
```csharp
builder.Services.AddZeroMcp(options =>
{
    options.EnableLegacySseTransport = true;  // default: false
});
```

#### Endpoints Registered

| Endpoint | Method | Purpose |
|---|---|---|
| `/mcp/sse` | GET | Persistent SSE connection; server sends `endpoint` event then tool call responses |
| `/mcp/messages` | POST | Client sends JSON-RPC requests |

#### Session Management

On `GET /mcp/sse`:
1. Generate a session ID (cryptographically secure UUID).
2. Send an `endpoint` SSE event: `data: /mcp/messages?sessionId={sessionId}`.
3. Hold the SSE connection open, keyed by session ID in a `ConcurrentDictionary<string, SseSession>`.

On `POST /mcp/messages?sessionId={sessionId}`:
1. Look up the session.
2. Route the JSON-RPC message through the existing `McpMessageRouter`.
3. Write the response as an SSE `message` event on the held stream.

#### Scaling Limitation

Because SSE sessions are held in process memory, this transport does not scale horizontally without sticky sessions or an out-of-process session store. **If you need horizontal scale, use Streamable HTTP.** SSE transport is for backward compatibility in single-instance or sticky-session deployments only.

### Acceptance Criteria

- [x] `WithLegacySseTransport()` extension and `EnableLegacySseTransport` option
- [x] `GET /mcp/sse` returns valid SSE stream with `endpoint` event
- [x] `POST /mcp/messages?sessionId=` routes correctly and returns response on SSE stream
- [x] Auth (`ForwardHeaders`, `RequireAuthorization`) works on SSE endpoints (same middleware chain)
- [x] Session cleanup on client disconnect
- [x] Horizontal scale limitation documented in wiki
- [x] `tools/list` and `tools/call` verified working against a real SSE client (integration tests)
- [x] Streamable HTTP transport unaffected

---

## 3. Streaming Responses via `IAsyncEnumerable<T>`

### Summary

Allow `[Mcp]`-decorated actions and minimal API endpoints that return `IAsyncEnumerable<T>` to stream results back to the MCP client as a sequence of partial tool results, rather than buffering the entire sequence before responding.

### Background

ZeroMCP currently requires that tool dispatch produces a single `IActionResult` or value. This is a hard blocker for:

- **Long-running data exports** that stream thousands of rows.
- **AI pipeline tools** that proxy a streamed upstream LLM response.
- **Real-time aggregation** that yields results as they arrive from multiple slow sources.

The MCP spec supports streaming tool results via progressive `tools/call` responses — the server emits multiple `content` chunks with `isLast: false` and a final chunk with `isLast: true`.

### Proposed Design

#### No New Attribute Required

If the return type of an `[Mcp]`-decorated action is `IAsyncEnumerable<T>`, ZeroMCP detects this at registration time and marks the tool as streaming:
```csharp
[HttpGet("reports/stream")]
[Mcp("stream_report", Description = "Streams report rows as they are generated.")]
public async IAsyncEnumerable<ReportRow> StreamReport(
    string reportId,
    [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var row in _reportService.StreamAsync(reportId, ct))
    {
        yield return row;
    }
}
```

#### Wire Protocol

When a `tools/call` request targets a streaming tool, ZeroMCP:

1. Sets `Content-Type: text/event-stream` on the response.
2. Begins enumerating the `IAsyncEnumerable<T>`.
3. For each yielded item, emits an SSE `message` event with a partial `CallToolResult` payload (`isLast: false`).
4. On completion, emits a final SSE event with `isLast: true`.
5. On cancellation or exception, emits an error event and closes the stream.

Non-streaming tools continue to return a plain HTTP response body — no change.

#### Schema Generation

The MCP tool schema represents the element type `T`, not `IAsyncEnumerable<T>`. A `"streaming": true` annotation is added to the tool descriptor.

#### Tool Inspector UI

Streaming tools are marked with a streaming badge. The Invoke panel renders results progressively as SSE events arrive.

### Acceptance Criteria

- [x] `IAsyncEnumerable<T>` return type auto-detected at registration
- [x] Tool descriptor includes `"streaming": true`
- [x] `tools/call` for streaming tools returns SSE stream with partial results
- [x] Final SSE event correctly marked (`status: "done"` with `totalChunks`)
- [x] Client disconnect cancels the enumerator via `CancellationToken`
- [x] Non-streaming tools completely unaffected
- [x] Tool Inspector UI marks streaming tools and renders progressive results
- [x] `MaxStreamingItems` safety option (default 10,000 in ZeroMcpOptions)
- [x] Integration tests: full enumeration, chunk content validation, tools/list flag, inspector flag

---

## 4. CancellationToken / Request Cancellation

### Summary

Allow `[Mcp]`-decorated actions to declare a `CancellationToken` parameter that is automatically cancelled when the MCP client sends a `notifications/cancelled` notification, or when the transport connection drops.

### Background

Long-running tools — database queries, external API calls, streaming operations — can be expensive to leave running if the client abandons them. Today ZeroMCP has no mechanism to propagate cancellation into the dispatched action. An abandoned tool call continues to consume threads, database connections, and memory until it completes naturally.

The MCP spec defines a `notifications/cancelled` notification that clients send to signal they no longer need a response.

### Proposed Design

#### Parameter Convention

If a dispatched action declares a `CancellationToken` parameter, ZeroMCP automatically binds it to a token that is cancelled when:

- The client sends `notifications/cancelled` with the matching `requestId`, **or**
- `HttpContext.RequestAborted` fires (connection dropped).
```csharp
[HttpGet("orders/search")]
[Mcp("search_orders", Description = "Searches orders with optional filters.")]
public async Task<ActionResult<List<Order>>> SearchOrders(
    string? status,
    DateTime? from,
    CancellationToken ct)  // ← ZeroMCP binds this automatically
{
    return await _db.Orders
        .Where(o => status == null || o.Status == status)
        .ToListAsync(ct);
}
```

No attribute or registration change required. If no `CancellationToken` parameter is present, behaviour is unchanged.

#### Cancellation Notification Handling

When a `notifications/cancelled` notification arrives:

1. Look up the `CancellationTokenSource` registered for that `requestId`.
2. Call `cts.Cancel()`.
3. ZeroMCP catches the resulting `OperationCanceledException` and returns JSON-RPC error code `-32800` (request cancelled), per MCP spec.

#### Schema Transparency

`CancellationToken` is excluded from the MCP input schema — it is an infrastructure parameter, not a tool argument.

### Implementation Notes

- Each dispatched tool call registers a `CancellationTokenSource` in a `ConcurrentDictionary<string, CancellationTokenSource>` keyed by JSON-RPC `id`.
- Sources are removed from the dictionary when the call completes, regardless of outcome.
- `OperationCanceledException` must be caught at the dispatch boundary and translated to JSON-RPC `-32800`, not a 500.

### Acceptance Criteria

- [x] `CancellationToken` parameter auto-detected and bound at dispatch time
- [x] `notifications/cancelled` with matching `requestId` cancels the token
- [x] Client disconnect also cancels the token
- [x] `OperationCanceledException` returns JSON-RPC error `-32800`
- [x] `CancellationToken` excluded from generated MCP input schema
- [x] In-flight request registry cleaned up on completion/cancellation
- [x] Actions without `CancellationToken` unaffected
- [x] Integration tests: normal completion, `notifications/cancelled`, connection drop

---

## 5. Multipart / File Upload Support (`[FromForm]`)

### Summary

Allow `[Mcp]`-decorated actions that accept `IFormFile` / `IFormFileCollection` to be called by MCP clients that pass file content as base64-encoded strings in the tool arguments JSON.

### Background

ZeroMCP currently documents `[FromForm]` and file uploads as unsupported. A pragmatic bridge exists: clients pass file content as a base64-encoded string, and ZeroMCP reconstructs an `IFormFile` from it before dispatching to the action.

A large proportion of brownfield APIs include document processing, image handling, or data import endpoints. Without this, those endpoints are permanently excluded from MCP exposure.

### Proposed Design

#### Schema Generation

ZeroMCP detects `IFormFile` parameters and expands each into up to three schema properties:
```json
{
  "type": "object",
  "properties": {
    "document": {
      "type": "string",
      "format": "byte",
      "description": "Base64-encoded file content"
    },
    "document_filename": {
      "type": "string",
      "description": "Original filename (optional)"
    },
    "document_content_type": {
      "type": "string",
      "description": "MIME type (optional, e.g. application/pdf)"
    }
  },
  "required": ["document"]
}
```

#### Dispatch

At dispatch time, ZeroMCP:

1. Reads the base64 value from the JSON arguments.
2. Decodes it into a `MemoryStream`.
3. Constructs a `FormFile` wrapping the stream with the provided filename and content type.
4. Populates the synthetic `HttpContext.Request.Form` with reconstructed files and remaining string values.
5. Dispatches normally — the action receives a real `IFormFile` and never knows the transport was MCP.

#### Size Limit

A `MaxFormFileSizeBytes` option (default: 10 MB) is enforced before decoding:
```csharp
options.MaxFormFileSizeBytes = 5 * 1024 * 1024;  // 5 MB
```

#### Documented Limitations

- File transfer size is constrained by JSON message limits. Not suitable for large media files or bulk data imports.
- Streaming multipart (chunked upload) is not supported. The entire file must be provided in a single tool call.

### Acceptance Criteria

- [x] `IFormFile` parameters detected and expanded to base64 schema properties
- [x] `IFormFileCollection` supported (multiple files; array of {content, filename?, content_type?})
- [x] `[FromForm]` string parameters included in schema normally alongside file params
- [x] Base64 decoded and `FormFile` constructed before dispatch
- [x] `MaxFormFileSizeBytes` option enforced pre-decode with structured error response
- [x] Filename and content type optional companion properties supported
- [x] Actions without `IFormFile` unaffected
- [x] Wiki: "File Upload Tools" section added to Parameters-and-Schemas
- [x] Integration tests: single file, oversized payload rejection, model binding verification from synthetic FormCollection

---

## 6. Minimal API Query and Body Binding Parity

### Summary

Fix the binding gaps in `.AsMcp()` for minimal APIs so that query string parameters, `[AsParameters]` record types, and `[FromBody]` objects are discovered and included in the generated MCP input schema with the same fidelity as controller actions.

### Background

For controller actions, parameter discovery uses `IApiDescriptionGroupCollectionProvider` and has full support for route params, query params, and `[FromBody]` objects. For minimal APIs, the current implementation has gaps:

- **Query parameters** not in the route template are not reliably included in the schema.
- **`[AsParameters]` record types** — the idiomatic minimal API pattern for grouping parameters — are not expanded into their constituent properties.
- **`[FromBody]`** validation attributes (`[Required]`, `[Range]`, etc.) are not always reflected as JSON Schema constraints.
- **Optional parameters with defaults** are not always correctly marked as non-required.

These gaps make `.AsMcp()` a second-class citizen relative to `[Mcp]` on controllers.

### Proposed Design

#### Query Parameter Discovery
```csharp
app.MapGet("/api/orders", (string? status, int page = 1, int pageSize = 20) => ...)
   .AsMcp("list_orders", "Lists orders with optional filtering.");
```

Generated schema:
```json
{
  "properties": {
    "status":   { "type": "string" },
    "page":     { "type": "integer", "default": 1 },
    "pageSize": { "type": "integer", "default": 20 }
  },
  "required": []
}
```

#### `[AsParameters]` Expansion
```csharp
public record OrderQuery(string? Status, DateTime? From, DateTime? To, int Page = 1);

app.MapGet("/api/orders", ([AsParameters] OrderQuery query) => ...)
   .AsMcp("list_orders", "Lists orders.");
```

`OrderQuery`'s four properties are expanded at the top level, same as if declared individually.

#### `[FromBody]` Validation Attribute Mapping

| DataAnnotations | JSON Schema |
|---|---|
| `[Required]` | Added to `required` array |
| `[Range(1, 100)]` | `minimum: 1, maximum: 100` |
| `[MinLength(3)]` / `[MaxLength(50)]` | `minLength`, `maxLength` |
| `[RegularExpression("...")]` | `pattern` |
| `[EmailAddress]` | `format: email` |
| `[Url]` | `format: uri` |

### Implementation Notes

- A new `MinimalApiParameterResolver` handles delegate reflection and `[AsParameters]` expansion, feeding into the same `McpSchemaBuilder` used by controller actions.
- Route template is parsed first to identify route-bound names; everything remaining is treated as query or body.
- Endpoint metadata (`IFromBodyMetadata`, `IFromQueryMetadata`) is consulted before falling back to reflection.
- This change only affects schema generation — dispatch is unchanged.

### Acceptance Criteria

- [ ] Query parameters on minimal API delegates included in schema
- [ ] Nullable and defaulted parameters correctly marked optional
- [ ] `[AsParameters]` record/class types expanded inline in schema
- [ ] `[FromBody]` validation attributes reflected as JSON Schema constraints
- [ ] Controller `[Mcp]` and minimal `.AsMcp()` produce equivalent schemas for equivalent signatures
- [ ] Existing minimal API endpoints with currently-working binding unaffected
- [ ] Integration tests: query params, `[AsParameters]`, `[FromBody]` with validation, mixed scenarios
- [ ] Parameters-and-Schemas wiki updated with minimal API examples

---

## Priority Recommendation

| # | Feature | Complexity | Impact | Suggested Order |
|---|---|---|---|---|
| 1 | stdio Transport | Medium | Very High — unlocks Claude Desktop, Claude Code, all local clients | **1st** |
| 4 | CancellationToken | Low | High — correctness for all long-running tools | **2nd** |
| 6 | Minimal API Binding Parity | Medium | High — fixes second-class citizen status | **3rd** |
| 3 | Streaming (`IAsyncEnumerable`) | High | Medium-High — unlocks long-running and AI pipeline tools | **4th** |
| 5 | File Upload (`[FromForm]`) | Medium | Medium — niche but frequently requested for doc processing | **5th** |
| 2 | Legacy SSE Transport | Low-Medium | Medium — backward compat only, not needed for new clients | **6th** |
