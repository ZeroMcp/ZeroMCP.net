# Governance and Security

Control which tools appear in **tools/list** per request and how auth is applied.

---

## Role-based exposure

On **`[Mcp]`** set **Roles** so the tool is only listed when the user is in at least one role:

```csharp
[Mcp("admin_report", Description = "Admin-only report.", Roles = ["Admin"])]
public IActionResult AdminReport() { ... }
```

Requires **AddAuthentication()** and **AddAuthorization()**. The same user identity is used when the tool is **invoked**; `[Authorize]` on the action is still enforced.

---

## Policy-based exposure

Set **Policy** so the tool is only listed when **IAuthorizationService.AuthorizeAsync(user, null, policy)** succeeds:

```csharp
[Mcp("edit_item", Description = "Edit item.", Policy = "RequireEditor")]
public IActionResult Edit(int id, [FromBody] EditRequest req) { ... }
```

---

## Minimal APIs

Use the same options on **.AsMcp(...)**:

```csharp
app.MapGet("/api/admin/health", () => Results.Ok(new { role = "admin" }))
   .AsMcp("admin_health", "Admin-only health.", tags: new[] { "system" }, roles: new[] { "Admin" });
```

---

## Discovery-time filter (ToolFilter)

Filter by **name** at startup so some tools are never registered:

```csharp
options.ToolFilter = name => !name.StartsWith("admin_");
```

Use for environment-specific exclusions (e.g. hide admin tools in non-production).

---

## Per-request filter (ToolVisibilityFilter)

Filter which tools appear in **tools/list** per request (e.g. by user, headers, feature flags):

```csharp
options.ToolVisibilityFilter = (name, ctx) =>
    ctx.Request.Headers.TryGetValue("X-Show-Admin", out _) || !name.StartsWith("admin_");
```

Return **true** to include the tool. Combined with roles/policy: the tool is listed only if it passes **and** (if it has Roles) the user is in a role **and** (if it has Policy) the policy succeeds.

---

## Call behavior

- Tools **hidden** from `tools/list` are not callable: a direct **tools/call** for that name returns "unknown tool".
- **Authorization** on the underlying action or endpoint is still enforced when the tool is invoked (e.g. `[Authorize]` returns 401).

---

## See also

- [The [Mcp] Attribute](The-Mcp-Attribute) — Roles and Policy parameters
- [Configuration](Configuration) — ToolFilter and ToolVisibilityFilter
- [Connecting MCP Clients](Connecting-Clients) — Securing the /mcp endpoint in production
