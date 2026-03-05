# The [Mcp] Attribute

Use the **`[Mcp]`** attribute (from **ZeroMCP.Attributes**) on controller actions to expose them as MCP tools.

---

## Basic usage

```csharp
[HttpGet("{id}")]
[Mcp("get_order", Description = "Retrieves a single order by ID.")]
public ActionResult<Order> GetOrder(int id) { ... }
```

- **Name** (first argument) — Required. Snake_case tool name for the LLM (e.g. `get_order`, `create_customer`).
- **Description** — Optional. Shown to the LLM; if omitted, ZeroMCP uses the method's XML doc `<summary>` when available (requires `EnableXMLDocAnalysis` in [Configuration](Configuration.md)).

---

## Full attribute

```csharp
[Mcp(
    name: "create_order",
    Description = "Creates an order. Returns the created order with its ID.",
    Tags = ["write", "orders"],
    Category = "orders",
    Examples = ["Create order for Alice with quantity 2"],
    Hints = ["idempotent", "cost=low"],
    Roles = ["Editor", "Admin"],
    Policy = "RequireEditor"
)]
```

| Parameter | Description |
|-----------|-------------|
| **name** | Required. Snake_case tool name. |
| **Description** | Optional. Shown to the LLM. |
| **Tags** | Optional. For grouping/filtering (e.g. `["write", "orders"]`). |
| **Category** | Optional. Primary grouping/category label in `tools/list`. |
| **Examples** | Optional. Free-form usage examples for AI clients. |
| **Hints** | Optional. AI-facing hints/metadata strings. |
| **Roles** | Optional. Tool only in `tools/list` if user is in at least one role. Requires auth. |
| **Policy** | Optional. Tool only in `tools/list` if `IAuthorizationService` authorizes the policy. |
| **Version** | Optional. When set to a value &gt; 0, the tool is exposed only on the versioned endpoint `/mcp/v{Version}`. When 0 or not set, the tool appears on all version endpoints. See [Tool Versioning](Tool-Versioning.md). |

---

## Placement rules

- **Per-action only** — `[Mcp]` goes on individual action methods, not on the controller class.
- **One name per version** — Duplicate tool names within the same version are logged and skipped; the same name in different versions (e.g. `get_order` in v1 and v2) is allowed. When no versioning is used, one name per application.
- **Any HTTP method** — GET, POST, PATCH, DELETE all work.
- **Description** — Prefer setting `Description` explicitly; otherwise XML `<summary>` is used when present (see `EnableXMLDocAnalysis` in [Configuration](Configuration.md)).

---

## Minimal APIs

For minimal API endpoints, use **`.AsMcp(...)`** instead of an attribute:

```csharp
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .AsMcp("health_check", "Returns API health status.", tags: new[] { "system" });
```

See [Controllers and Minimal APIs](Controllers-and-Minimal-APIs).

---

## See also

- [Governance and Security](Governance-and-Security) — How roles and policy affect tool visibility
- [Parameters and Schemas](Parameters-and-Schemas) — How action parameters become MCP input schema
