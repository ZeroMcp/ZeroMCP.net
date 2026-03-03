# Limitations

Known limitations and workarounds.

---

## Transport

- **Streamable HTTP only** — stdio and SSE transports are not supported. Only the MCP Streamable HTTP transport (GET/POST `/mcp`) is implemented.

---

## Minimal APIs

- **Supported** via **.AsMcp(...)**; route parameters are bound.
- **Query and body binding** — Limited to what the route pattern and runtime expose; complex query/body binding may not match controller behavior.

---

## Body and uploads

- **[FromForm] and file uploads** — Not supported; JSON-only body binding for tool calls.
- **Streaming responses** — **IAsyncEnumerable&lt;T&gt;** and SSE action results are not captured correctly; the client receives the final response shape, not a stream.

---

## Link generation

- If **CreatedAtAction** or **LinkGenerator** ever fails in your environment (e.g. missing route values), use an explicit URL:

  ```csharp
  return Created(Url.Action(nameof(GetOrder), new { id = order.Id })!, order);
  // or
  return Created($"/api/orders/{order.Id}", order);
  ```

---

## See also

- [Dispatch and Pipeline](Dispatch-and-Pipeline) — How dispatch works
- [Parameters and Schemas](Parameters-and-Schemas) — Input binding
