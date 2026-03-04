# Quick Start

Get ZeroMCP running in a few steps.

---

## 1. Install

Add the package to your ASP.NET Core project:

```xml
<PackageReference Include="ZeroMCP" Version="1.*" />
```

---

## 2. Register services

In `Program.cs`:

```csharp
builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "My Orders API";
    options.ServerVersion = "1.0.0";

    // Phase 2 (optional)
    options.EnableResultEnrichment = true;
    options.EnableStreamingToolResults = true;
    options.StreamingChunkSize = 4096;
});
```

---

## 3. Map the endpoint

```csharp
app.MapZeroMCP();   // registers GET and POST /mcp
```

---

## 4. Tag your actions

Add the **`[Mcp]`** attribute to controller actions you want exposed as MCP tools:

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

Use **snake_case** for the tool name (e.g. `get_order`, `create_order`). Omit `[Mcp]` on actions you do not want exposed.

---

## 5. Run and connect

Point any MCP client at your app's `/mcp` URL (e.g. `http://localhost:5000/mcp`). The client will see your tagged actions as tools.

- **GET /mcp** — Server info and example JSON-RPC payload.
- **GET /mcp/tools** — (Phase 3) JSON list of all registered tools and their schemas (when **EnableToolInspector** is true). Useful for debugging and tooling.
- **GET /mcp/ui** — (Phase 3) Swagger-like test invocation UI: list tools, view schemas, and invoke tools from the browser (when **EnableToolInspectorUI** is true).
- **POST /mcp** — JSON-RPC (`initialize`, `tools/list`, `tools/call`).

See [Connecting MCP Clients](Connecting-Clients) for Claude Desktop and Claude.ai.

---

## Examples

The repository includes **examples/** for different scenarios:

- **Minimal** — Bare-minimum setup (one controller, one minimal API).
- **WithAuth** — API-key auth, role-based tool visibility, `[Authorize]`.
- **WithEnrichment** — Result enrichment, suggested follow-ups, streaming options.
- **Enterprise** — Auth, enrichment, observability, ToolFilter, ToolVisibilityFilter.

---

## Next steps

- [Configuration](Configuration) — Route prefix, tool filters, observability, enrichment, streaming, tool inspector
- [The [Mcp] Attribute](The-Mcp-Attribute) — Description, tags, category, examples, hints, roles, policy
- [Controllers and Minimal APIs](Controllers-and-Minimal-APIs) — Exposing minimal API endpoints as tools
- [Enterprise Usage](Enterprise-Usage) — Production deployment checklist
