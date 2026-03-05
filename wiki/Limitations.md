# Limitations

Known limitations and workarounds.

---

## Transport

- **Streamable HTTP** — Primary transport: GET/POST `/mcp`. stdio is supported via `RunMcpStdioAsync()` and `--mcp-stdio`.
- **Legacy SSE (opt-in)** — When `WithLegacySseTransport()` or `EnableLegacySseTransport` is enabled, GET `/mcp/sse` and POST `/mcp/messages` provide backward compatibility with MCP spec 2024-11-05 clients. **Horizontal scale limitation:** SSE sessions are held in process memory; use sticky sessions or Streamable HTTP for multi-instance deployments.

---

## Minimal APIs

- **Supported** via **.AsMcp(...)**; route parameters are bound.
- **Query and body binding** — Limited to what the route pattern and runtime expose; complex query/body binding may not match controller behavior.

---

## Body and uploads

- **[FromForm] and file uploads** — Supported for `IFormFile` and `IFormFileCollection`: clients pass base64-encoded content; ZeroMCP reconstructs `FormFile` before dispatch. See [Parameters and Schemas](Parameters-and-Schemas) (File Upload Tools). Size limit: `MaxFormFileSizeBytes` (default 10 MB).
- **Streaming responses** — **IAsyncEnumerable&lt;T&gt;** and SSE action results from the action are not captured as a live stream. When **EnableStreamingToolResults** is true, the (buffered) response body is split into chunks (chunkIndex, isFinal, text) in a single JSON response for streaming-aware clients.

---

## Link generation

- If **CreatedAtAction** or **LinkGenerator** ever fails in your environment (e.g. missing route values), use an explicit URL:

  ```csharp
  return Created(Url.Action(nameof(GetOrder), new { id = order.Id })!, order);
  // or
  return Created($"/api/orders/{order.Id}", order);
  ```

---

## Tool versioning

- **Integer versions only** — Only integer version numbers (e.g. 1, 2) are supported. Date-based or string versions may be added in a future release. See [Tool Versioning](Tool-Versioning).

---

## Tool inspector

- **GET /mcp/tools** returns all *registered* tools (subject to **ToolFilter** only). It does not apply per-request visibility (roles, policy, **ToolVisibilityFilter**). In production, disable **EnableToolInspector** or protect the route if the list is sensitive. See [Enterprise Usage](Enterprise-Usage) and [Security Model](Security-Model).

---

## See also

- [Dispatch and Pipeline](Dispatch-and-Pipeline) — How dispatch works
- [Parameters and Schemas](Parameters-and-Schemas) — Input binding
