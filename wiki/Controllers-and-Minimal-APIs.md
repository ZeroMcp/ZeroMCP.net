# Controllers and Minimal APIs

You can expose **controller actions** and **minimal API endpoints** as MCP tools. Both can be used in the same app.

---

## Controllers

Tag actions with **`[Mcp]`**:

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id:int}")]
    [Mcp("get_order", Description = "Retrieves a single order by ID.")]
    public ActionResult<Order> GetOrder(int id) { ... }
}
```

Discovery uses the same API descriptions as Swagger (from **IApiDescriptionGroupCollectionProvider**). You must call **AddControllers()** and **AddEndpointsApiExplorer()** when using controllers.

---

## Minimal APIs

Use **`.AsMcp(...)`** when mapping the endpoint:

```csharp
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .AsMcp(
       "health_check",
       "Returns API health status.",
       tags: new[] { "system" },
       category: "system",
       examples: new[] { "Check service health before calling business tools" },
       hints: new[] { "read_only", "cost=low" });
```

- **Name** (required) — Snake_case tool name.
- **Description** (optional) — Shown to the LLM.
- **Tags** (optional) — e.g. `new[] { "system" }`.
- **Category** (optional) — Primary grouping/category for `tools/list`.
- **Examples** (optional) — Free-form usage examples for AI clients.
- **Hints** (optional) — AI-facing hint strings.
- **Roles** (optional) — Only list tool if user is in one of these roles.
- **Policy** (optional) — Only list tool if authorization policy succeeds.
- **Version** (optional) — When set to a value &gt; 0, the tool is exposed only on **/mcp/v{Version}**. When null or not set, the tool appears on all version endpoints. See [Tool Versioning](Tool-Versioning).

Discovery uses **EndpointDataSource**. Route parameters on minimal APIs are supported; query/body binding is limited to what the route pattern exposes.

---

## Using both together

If you expose **both** controller tools and minimal API tools:

1. Register **AddControllers()** and **AddEndpointsApiExplorer()**.
2. Register **AddZeroMCP(...)**.
3. Call **app.MapControllers()** then map your minimal APIs with **.AsMcp(...)**, then **app.MapZeroMCP()**.

Without **AddEndpointsApiExplorer()**, controller actions are not discovered and only minimal API tools will appear in `tools/list`.

---

## See also

- [The [Mcp] Attribute](The-Mcp-Attribute) — Controller attribute options
- [Configuration](Configuration) — AddEndpointsApiExplorer requirement
- [Governance and Security](Governance-and-Security) — Roles and policy on minimal APIs
