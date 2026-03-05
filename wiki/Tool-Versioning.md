# Tool Versioning

When your API evolves, tool signatures change — parameters are added or renamed, response shapes change. ZeroMCP supports **versioned MCP endpoints** so you can expose multiple tool versions without breaking existing clients.

---

## Overview

- Add an optional **`Version`** (integer) to the `[Mcp]` attribute or `.AsMcp(...)`.
- Tools **without** a version appear on **all** version endpoints.
- Tools **with** a version appear only on that version’s endpoint (`/mcp/v1`, `/mcp/v2`, etc.).
- The unversioned **`/mcp`** endpoint always resolves to a single version (by default the **highest** registered version, or a configured default).

---

## Attribute and minimal API

### Controller

```csharp
[Mcp("get_order", Version = 1, Description = "Retrieves a single order by ID.")]
public ActionResult<Order> GetOrderV1(int id) { ... }

[Mcp("get_order", Version = 2, Description = "Retrieves a single order by ID with expanded detail.")]
public ActionResult<OrderDetail> GetOrderV2(int id, bool includeHistory = false) { ... }

[Mcp("list_orders", Description = "Lists all orders.")]  // no Version — appears on every version
public ActionResult<List<Order>> ListOrders(string? status = null) { ... }
```

- Use **`Version = 0`** or omit **Version** for “unversioned” (tool appears on all version endpoints).
- Use **`Version = 1`**, **`Version = 2`**, etc. for version-specific tools. The same tool **name** can exist in multiple versions (e.g. `get_order` in v1 and v2).

### Minimal API

```csharp
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .AsMcp("health_check", "Returns API health status.");  // unversioned

app.MapGet("/api/v2/health", () => Results.Ok(new { status = "ok", version = "2" }))
   .AsMcp("health_check", "Returns enhanced health status.", version: 2);
```

---

## Endpoint behaviour

| Case | Behaviour |
|------|-----------|
| **No versioned tools** | Only `/mcp`, `/mcp/tools`, `/mcp/ui` are registered (same as before versioning). |
| **At least one versioned tool** | `/mcp/v1`, `/mcp/v2`, … are registered for each distinct version; `/mcp` (and `/mcp/tools`, `/mcp/ui`) use the default version. |
| **`/mcp`** | Resolves to the **default** version (see [Configuration](#configuration)). |
| **`/mcp/v{n}`** | Serves unversioned tools plus tools with `Version = n`. |
| **Non-existent version** (e.g. `/mcp/v99`) | Returns **HTTP 404**. |

---

## Configuration

In **AddZeroMCP** you can set **DefaultVersion** so the unversioned `/mcp` endpoint is pinned to a specific version instead of “highest”:

```csharp
builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "My Orders API";
    options.DefaultVersion = 1;  // /mcp resolves to v1 even if v2 exists (e.g. during migration)
});
```

- **`DefaultVersion = null`** (default): `/mcp` uses the **highest** registered version.
- **`DefaultVersion = 1`** (or any registered version): `/mcp` uses that version.

---

## Tool Inspector

- **GET /mcp/tools** and **GET /mcp/v{n}/tools** return JSON that includes **`version`** and **`availableVersions`** when versioning is in use. Each tool entry can include a **`version`** field (null for unversioned).
- **GET /mcp/ui** (and **GET /mcp/v{n}/ui** when versioned) can show a **version selector** in the header when multiple versions exist. Changing the version reloads the tool list and sends **Invoke** to the selected version’s endpoint.
- Version-specific tools can show a small version badge (e.g. `v2`) in the UI.

See [Tool Inspector UI](Tool-Inspector-UI.md) for details.

---

## Duplicate names

- The **same tool name** in different versions (e.g. `get_order` in v1 and v2) is allowed; each version endpoint has its own tool set.
- Duplicate **names within the same version** (e.g. two tools named `get_order` both with `Version = 1`) are logged as warnings and one is skipped.

---

## Limitations

- Only **integer** versions are supported (e.g. 1, 2). Date or string versions may be considered in a future release.
- The MCP protocol itself is unchanged; each `/mcp` and `/mcp/v{n}` endpoint is a normal MCP server (initialize, tools/list, tools/call).

---

## See also

- [Configuration](Configuration.md) — **DefaultVersion**
- [The [Mcp] Attribute](The-Mcp-Attribute.md) — **Version** property
- [Tool Inspector UI](Tool-Inspector-UI.md) — version selector and versioned endpoints
- [Migration Guide](Migration-Guide.md) — no migration needed when not using versioning
