# Missing Transport & Input Types Implementation Plan

**Source:** [.localplanning/plan-for-missing-transport.md](.localplanning/plan-for-missing-transport.md)  
**Focus:** Transport gaps (stdio, Legacy SSE), input-type gaps (streaming, cancellation, file upload, minimal API binding)  
**Outcome:** MCP spec compliance, Claude Desktop/Code compatibility, full parameter and binding parity

This document turns the feature requests in plan-for-missing-transport.md into a concrete implementation plan with tasks, dependencies, and acceptance criteria.

---

## 1. stdio Transport

### Goals

- Add `stdio` transport so ZeroMCP APIs can run as local subprocesses.
- Satisfy MCP spec `SHOULD support stdio` requirement.
- Unlock compatibility with Claude Desktop, Claude Code, VS Code extensions, and other clients that spawn MCP servers as child processes.

### 1.1 Activation and Entry Point

| Task | Description | Notes |
|------|-------------|-------|
| **Extension method** | Add `RunMcpStdioAsync()` on `WebApplication` / `IHost` that starts the host, opens stdin/stdout, and runs the JSON-RPC loop. | New file: `McpStdioExtensions.cs` or similar. |
| **CLI flag** | Support `--mcp-stdio` so apps can branch: `if (args.Contains("--mcp-stdio")) { await app.RunMcpStdioAsync(); return; }`. | Document in Program.cs examples. |
| **Env var (optional)** | Consider `ZEROMCP_STDIO=1` as alternative activation. | Low priority. |

### 1.2 Wire Protocol

| Task | Description | Notes |
|------|-------------|-------|
| **Input** | Read newline-delimited JSON-RPC from stdin using `PipeReader` for efficient newline detection. | Same JSON-RPC messages as Streamable HTTP: `initialize`, `tools/list`, `tools/call`. |
| **Output** | Write JSON-RPC responses as single newline-delimited lines to stdout. Use `StreamWriter` with `AutoFlush = true`. | stdout reserved exclusively for JSON-RPC; stderr for logs. |
| **Routing** | Route each message through existing `McpMessageRouter`. | Reuse HTTP handler logic where possible. |

### 1.3 Identity and Auth

| Task | Description | Notes |
|------|-------------|-------|
| **StdioIdentity** | Add `options.StdioIdentity` (ClaimsPrincipal?) for fixed-identity stdio deployments. | When set, use this as `HttpContext.User` (or equivalent) for tool dispatch. |
| **ForwardHeaders** | Silently ignore `ForwardHeaders` in stdio mode. | No HTTP context; document as no-op. |

### 1.4 Behaviour in stdio Mode

| Task | Description | Notes |
|------|-------------|-------|
| **Tool Inspector** | Do not start Tool Inspector UI or `/mcp/tools` JSON endpoint in stdio mode. | Avoid binding to HTTP when in stdio. |
| **Logging** | Use `Console.Error` for all internal ZeroMCP diagnostic logging in stdio mode. | Keep stdout clean for JSON-RPC only. |
| **Exit** | Exit cleanly when stdin closes (EOF). | |

### 1.5 Acceptance Criteria

- [x] `RunMcpStdioAsync()` extension method on `WebApplication` / `IHost`
- [x] `--mcp-stdio` CLI flag supported out of the box
- [x] stdin reads newline-delimited JSON-RPC; stdout writes newline-delimited responses
- [x] `initialize`, `tools/list`, `tools/call` all function correctly over stdio
- [x] stderr used for all internal logging; stdout reserved for JSON-RPC only
- [x] `StdioIdentity` option for fixed-identity stdio deployments
- [x] `ForwardHeaders` silently ignored in stdio mode
- [x] HTTP transport unaffected; both transports coexist in the same binary
- [x] Claude Desktop example in Quick Start wiki updated
- [x] Integration test: launch host in stdio mode, send JSON-RPC over pipes, assert response

---

## 2. CancellationToken / Request Cancellation

### Goals

- Allow `[Mcp]`-decorated actions to declare a `CancellationToken` that is cancelled when the client sends `notifications/cancelled` or when the transport connection drops.
- Propagate cancellation into dispatched actions to avoid wasted work on abandoned tool calls.

### 2.1 Parameter Binding

| Task | Description | Notes |
|------|-------------|-------|
| **Auto-detection** | If an action declares a `CancellationToken` parameter, ZeroMCP binds it automatically at dispatch time. | No attribute or registration change required. |
| **Schema** | Exclude `CancellationToken` from the MCP input schema — it is infrastructure, not a tool argument. | |

### 2.2 Cancellation Sources

| Task | Description | Notes |
|------|-------------|-------|
| **notifications/cancelled** | When client sends `notifications/cancelled` with matching `requestId`, look up the `CancellationTokenSource` and call `cts.Cancel()`. | Need in-flight request registry keyed by JSON-RPC `id`. |
| **Connection drop** | When `HttpContext.RequestAborted` fires, cancel the token. | For stdio, EOF or pipe break should also trigger. |

### 2.3 Error Handling

| Task | Description | Notes |
|------|-------------|-------|
| **OperationCanceledException** | Catch at dispatch boundary and return JSON-RPC error code `-32800` (request cancelled), per MCP spec. | Do not return 500. |
| **Cleanup** | Remove `CancellationTokenSource` from registry when call completes (success, cancellation, or exception). | Use `ConcurrentDictionary<string, CancellationTokenSource>`. |

### 2.4 Acceptance Criteria

- [x] `CancellationToken` parameter auto-detected and bound at dispatch time
- [x] `notifications/cancelled` with matching `requestId` cancels the token
- [x] Client disconnect also cancels the token
- [x] `OperationCanceledException` returns JSON-RPC error `-32800`
- [x] `CancellationToken` excluded from generated MCP input schema
- [x] In-flight request registry cleaned up on completion/cancellation
- [x] Actions without `CancellationToken` unaffected
- [x] Integration tests: normal completion, `notifications/cancelled`, connection drop

---

## 3. Minimal API Query and Body Binding Parity

### Goals

- Fix binding gaps in `.AsMcp()` so query parameters, `[AsParameters]` record types, and `[FromBody]` objects are discovered and included in the MCP input schema with the same fidelity as controller actions.
- Make minimal APIs first-class citizens alongside `[Mcp]` on controllers.

### 3.1 Query Parameter Discovery

| Task | Description | Notes |
|------|-------------|-------|
| **Delegate reflection** | Inspect minimal API delegate parameters; include query params not in route template in schema. | New `MinimalApiParameterResolver` or extend existing discovery. |
| **Nullable and defaults** | Nullable and defaulted parameters correctly marked optional in schema. | e.g. `page = 1` → `default: 1`, not required. |

### 3.2 [AsParameters] Expansion

| Task | Description | Notes |
|------|-------------|-------|
| **Expansion** | When delegate has `[AsParameters] OrderQuery query`, expand `OrderQuery`'s properties at top level in schema. | Same as if declared individually. |
| **Metadata** | Consult `IFromBodyMetadata`, `IFromQueryMetadata` before falling back to reflection. | |

### 3.3 [FromBody] Validation Mapping

| Task | Description | Notes |
|------|-------------|-------|
| **DataAnnotations → JSON Schema** | Map `[Required]` → `required` array; `[Range]` → `minimum`/`maximum`; `[MinLength]`/`[MaxLength]` → `minLength`/`maxLength`; `[RegularExpression]` → `pattern`; `[EmailAddress]` → `format: email`; `[Url]` → `format: uri`. | Feed into same `McpSchemaBuilder` used by controller actions. |

### 3.4 Acceptance Criteria

- [x] Query parameters on minimal API delegates included in schema (via ApiDescription matching)
- [x] Nullable and defaulted parameters correctly marked optional
- [ ] `[AsParameters]` record/class types expanded inline in schema (follow-up if ApiDescription does not expand)
- [x] `[FromBody]` validation attributes reflected as JSON Schema constraints (via existing McpSchemaBuilder)
- [x] Controller `[Mcp]` and minimal `.AsMcp()` produce equivalent schemas for equivalent signatures
- [x] Existing minimal API endpoints with currently-working binding unaffected
- [x] Integration tests: query params, `[FromBody]` with validation (schema unit test + conditional integration)
- [x] Parameters-and-Schemas wiki updated with minimal API examples

---

## 4. Streaming Responses via IAsyncEnumerable\<T\>

### Goals

- Allow `[Mcp]`-decorated actions that return `IAsyncEnumerable<T>` to stream results as partial tool results instead of buffering.
- Support long-running exports, AI pipeline tools, and real-time aggregation.

### 4.1 Detection and Schema

| Task | Description | Notes |
|------|-------------|-------|
| **Auto-detection** | At registration, if return type is `IAsyncEnumerable<T>`, mark tool as streaming. | No new attribute. |
| **Schema** | Tool descriptor includes `"streaming": true`; schema represents element type `T`, not `IAsyncEnumerable<T>`. | |

### 4.2 Wire Protocol

| Task | Description | Notes |
|------|-------------|-------|
| **Content-Type** | For streaming tools, set `Content-Type: text/event-stream` on response. | |
| **SSE events** | For each yielded item, emit SSE `message` event with partial `CallToolResult` (`isLast: false`). On completion, emit final event with `isLast: true`. | MCP spec supports progressive `tools/call` responses. |
| **Cancellation** | Client disconnect cancels the enumerator via `CancellationToken`. | |
| **Error** | On exception, emit error event and close stream. | |

### 4.3 Tool Inspector

| Task | Description | Notes |
|------|-------------|-------|
| **Badge** | Mark streaming tools with a streaming badge. | |
| **Progressive render** | Invoke panel renders results progressively as SSE events arrive. | |

### 4.4 Safety and Docs

| Task | Description | Notes |
|------|-------------|-------|
| **MaxStreamingItems** | Document optional `MaxStreamingItems` safety option. | Limit unbounded streams. |

### 4.5 Acceptance Criteria

- [ ] `IAsyncEnumerable<T>` return type auto-detected at registration
- [ ] Tool descriptor includes `"streaming": true`
- [ ] `tools/call` for streaming tools returns SSE stream with partial results
- [ ] Final SSE event correctly marked `isLast: true`
- [ ] Client disconnect cancels the enumerator via `CancellationToken`
- [ ] Non-streaming tools completely unaffected
- [ ] Tool Inspector UI marks streaming tools and renders progressive results
- [ ] `MaxStreamingItems` safety option documented
- [ ] Integration tests: full enumeration, mid-stream cancellation, enumerator exception handling

---

## 5. Multipart / File Upload Support ([FromForm])

### Goals

- Allow `[Mcp]`-decorated actions that accept `IFormFile` / `IFormFileCollection` to be called by MCP clients passing file content as base64-encoded strings in tool arguments.
- Enable document processing, image handling, and data import endpoints to be exposed as MCP tools.

### 5.1 Schema Generation

| Task | Description | Notes |
|------|-------------|-------|
| **IFormFile expansion** | For each `IFormFile` param, expand to up to three schema properties: `{name}` (base64, required), `{name}_filename` (optional), `{name}_content_type` (optional). | `format: byte` for base64. |
| **IFormFileCollection** | Support multiple files with same pattern. | |
| **[FromForm] strings** | Include `[FromForm]` string parameters in schema normally alongside file params. | |

### 5.2 Dispatch

| Task | Description | Notes |
|------|-------------|-------|
| **Decode** | Read base64 from JSON arguments, decode into `MemoryStream`. | |
| **FormFile** | Construct `FormFile` with provided filename and content type. | |
| **Form** | Populate synthetic `HttpContext.Request.Form` with reconstructed files and remaining values. | |
| **Dispatch** | Action receives real `IFormFile`; transport is transparent. | |

### 5.3 Size Limit

| Task | Description | Notes |
|------|-------------|-------|
| **MaxFormFileSizeBytes** | Add option (default: 10 MB). Enforce before decoding; return structured error if exceeded. | |

### 5.4 Docs and Limitations

| Task | Description | Notes |
|------|-------------|-------|
| **Wiki** | Add "File Upload Tools" section to Parameters-and-Schemas. | |
| **Limitations** | Document: file size constrained by JSON message limits; no chunked upload. | |

### 5.5 Acceptance Criteria

- [ ] `IFormFile` parameters detected and expanded to base64 schema properties
- [ ] `IFormFileCollection` supported (multiple files)
- [ ] `[FromForm]` string parameters included in schema normally alongside file params
- [ ] Base64 decoded and `FormFile` constructed before dispatch
- [ ] `MaxFormFileSizeBytes` option enforced pre-decode with structured error response
- [ ] Filename and content type optional companion properties supported
- [ ] Actions without `IFormFile` unaffected
- [ ] Wiki: "File Upload Tools" section added to Parameters-and-Schemas
- [ ] Integration tests: single file, multiple files, oversized payload rejection

---

## 6. Legacy SSE Transport (Backward Compatibility)

### Goals

- Add opt-in support for deprecated HTTP+SSE transport (MCP spec 2024-11-05) so clients that have not migrated to Streamable HTTP can connect.
- Support enterprise customers who cannot upgrade client tooling on a short cycle.

### 6.1 Activation

| Task | Description | Notes |
|------|-------------|-------|
| **Extension** | `app.MapZeroMCP().WithLegacySseTransport()` adds GET `/mcp/sse` and POST `/mcp/messages`. | |
| **Option** | `options.EnableLegacySseTransport = true` (default: false). | |

### 6.2 Endpoints and Session Management

| Task | Description | Notes |
|------|-------------|-------|
| **GET /mcp/sse** | Generate session ID (UUID), send `endpoint` SSE event with `data: /mcp/messages?sessionId={id}`, hold connection open keyed by session ID. | Store in `ConcurrentDictionary<string, SseSession>`. |
| **POST /mcp/messages?sessionId=** | Look up session, route JSON-RPC through `McpMessageRouter`, write response as SSE `message` event on held stream. | |
| **Cleanup** | Session cleanup on client disconnect. | |

### 6.3 Auth and Scale

| Task | Description | Notes |
|------|-------------|-------|
| **Auth** | `ForwardHeaders`, `RequireAuthorization` work on SSE endpoints. | |
| **Scale** | Document: SSE sessions in process memory; does not scale horizontally without sticky sessions. Use Streamable HTTP for scale. | |

### 6.4 Acceptance Criteria

- [ ] `WithLegacySseTransport()` extension and `EnableLegacySseTransport` option
- [ ] `GET /mcp/sse` returns valid SSE stream with `endpoint` event
- [ ] `POST /mcp/messages?sessionId=` routes correctly and returns response on SSE stream
- [ ] Auth (`ForwardHeaders`, `RequireAuthorization`) works on SSE endpoints
- [ ] Session cleanup on client disconnect
- [ ] Horizontal scale limitation documented in wiki
- [ ] `tools/list` and `tools/call` verified working against a real SSE client
- [ ] Streamable HTTP transport unaffected

---

## 7. Implementation Order and Dependencies

| Order | Feature | Complexity | Impact | Tasks |
|-------|---------|------------|--------|-------|
| **1** | stdio Transport | Medium | Very High | Activation, wire protocol, identity, logging, tests, docs |
| **2** | CancellationToken | Low | High | Binding, notification handling, registry, error mapping, tests |
| **3** | Minimal API Binding Parity | Medium | High | Parameter resolver, query discovery, [AsParameters], validation mapping, tests, docs |
| **4** | Streaming (IAsyncEnumerable) | High | Medium-High | Detection, SSE protocol, schema, UI, cancellation, tests |
| **5** | File Upload ([FromForm]) | Medium | Medium | Schema expansion, dispatch, size limit, tests, docs |
| **6** | Legacy SSE Transport | Low-Medium | Medium | Endpoints, session management, auth, cleanup, tests, docs |

**Dependencies:**

- stdio: None (standalone).
- CancellationToken: None; can run in parallel with stdio.
- Minimal API: None; schema generation only.
- Streaming: Benefits from CancellationToken for client disconnect.
- File Upload: None.
- Legacy SSE: None; opt-in, isolated.

---

## 8. Acceptance Criteria (Summary)

| Feature | Key Criteria |
|---------|--------------|
| **stdio** | `RunMcpStdioAsync()`, `--mcp-stdio`, newline-delimited JSON-RPC, `StdioIdentity`, Claude Desktop example, integration test |
| **CancellationToken** | Auto-binding, `notifications/cancelled`, `-32800` on cancel, schema exclusion, registry cleanup, integration tests |
| **Minimal API** | Query params, `[AsParameters]`, validation mapping, parity with controllers, integration tests, wiki examples |
| **Streaming** | `IAsyncEnumerable<T>` detection, `streaming: true`, SSE partial results, `isLast`, UI badge, integration tests |
| **File Upload** | Base64 schema, `FormFile` construction, `MaxFormFileSizeBytes`, `IFormFileCollection`, wiki section, integration tests |
| **Legacy SSE** | `WithLegacySseTransport`, GET `/mcp/sse`, POST `/mcp/messages`, session management, auth, scale docs, integration tests |

---

## 9. Files to Touch (Estimated)

| Area | Files |
|------|-------|
| **stdio** | New: `McpStdioExtensions.cs`, `McpStdioHostRunner.cs` (or similar); `ZeroMcpOptions.cs` (StdioIdentity); `Program.cs` examples; `wiki/Quick-Start.md` |
| **CancellationToken** | `McpToolHandler.cs` (binding, registry); `McpHttpEndpointHandler.cs` (notifications/cancelled); `ZeroMcpOptions.cs` (if needed); new `McpCancellationRegistry.cs` |
| **Minimal API** | New: `MinimalApiParameterResolver.cs`; `McpSchemaBuilder.cs` (or equivalent); discovery service; `wiki/Parameters-and-Schemas.md` |
| **Streaming** | `McpToolDescriptor.cs` (streaming flag); `McpToolHandler.cs` (IAsyncEnumerable dispatch); `McpHttpEndpointHandler.cs` (SSE response); Tool Inspector UI |
| **File Upload** | `McpSchemaBuilder.cs` (IFormFile expansion); `McpToolHandler.cs` (base64 decode, FormFile); `ZeroMcpOptions.cs` (MaxFormFileSizeBytes); `wiki/Parameters-and-Schemas.md` |
| **Legacy SSE** | New: `McpLegacySseEndpointHandler.cs`, `McpSseSessionStore.cs`; `MapZeroMCP` extensions; `ZeroMcpOptions.cs`; `wiki/Limitations.md` or new doc |

---

## 10. References

- [.localplanning/plan-for-missing-transport.md](.localplanning/plan-for-missing-transport.md) — Source feature requests
- [plan.md](plan.md) — Master growth plan (Phase 5 mentions prompts, form fields, file uploads)
- [plan-phase4.md](plan-phase4.md) — Phase 4 implementation plan format reference
- MCP Specification (2025-03-26) — stdio, Streamable HTTP, `notifications/cancelled`, streaming tool results
