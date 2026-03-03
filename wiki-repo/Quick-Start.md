# Quick Start

Get ZeroMcp running in a few steps.

---

## 1. Install

Add the package to your ASP.NET Core project:

```xml
<PackageReference Include="ZeroMcp" Version="1.*" />
```

---

## 2. Register services

In `Program.cs`:

```csharp
builder.Services.AddZeroMcp(options =>
{
    options.ServerName = "My Orders API";
    options.ServerVersion = "1.0.0";
});
```

---

## 3. Map the endpoint

```csharp
app.MapZeroMcp();   // registers GET and POST /mcp
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

See [Connecting MCP Clients](Connecting-Clients) for Claude Desktop and Claude.ai.

---

## Next steps

- [Configuration](Configuration) — Route prefix, tool filters, observability
- [The [Mcp] Attribute](The-Mcp-Attribute) — Description, tags, roles, policy
- [Controllers and Minimal APIs](Controllers-and-Minimal-APIs) — Exposing minimal API endpoints as tools
