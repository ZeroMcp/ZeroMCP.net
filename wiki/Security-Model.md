# Security Model

How authentication and authorization work with ZeroMcp and how to secure the MCP endpoint and tool dispatch.

---

## Overview

- ZeroMcp does **not** add authentication or authorization by itself. You configure **AddAuthentication** and **AddAuthorization** and (optionally) endpoint-level requirements on the MCP route.
- The **MCP endpoint** (GET/POST at **RoutePrefix**, e.g. `/mcp`) is the only HTTP surface ZeroMcp registers by default; when **EnableToolInspector** is true, **GET {RoutePrefix}/tools** is also registered.
- **Tool visibility** (who sees which tools in `tools/list`) is controlled by **Roles**, **Policy** on `[Mcp]` / `.AsMcp(...)`, and **ToolVisibilityFilter**.
- **Tool invocation** runs inside your app via a **synthetic HttpContext**. The same auth identity and headers you forward are used so that **`[Authorize]`** and authorization policies on the underlying action or endpoint are enforced.

---

## Auth flow

1. **Request hits the MCP endpoint** — Your ASP.NET Core pipeline runs (routing, authentication, authorization if you added them before **MapZeroMcp**).
2. **Authentication** runs on the incoming request (e.g. API key or Bearer). The resulting **ClaimsPrincipal** is set on **HttpContext.User**.
3. **tools/list** — ZeroMcp calls **GetToolDefinitionsAsync(HttpContext)**. For each tool it checks **RequiredRoles** (user must be in at least one role), **RequiredPolicy** (IAuthorizationService), and **ToolVisibilityFilter**. Only tools that pass are returned. So the client only sees tools it is allowed to see.
4. **tools/call** — ZeroMcp looks up the tool by name, builds a **synthetic HttpContext** for the underlying action or minimal endpoint, and invokes it in-process. The synthetic context gets:
   - **User** — Copied from the MCP request's **HttpContext.User** so the dispatched action sees the same identity.
   - **Headers** — Those in **ForwardHeaders** (e.g. **Authorization**) are copied so the action can use the same token if needed.
5. **Authorization on the action** — When the action or endpoint has **`[Authorize]`** or policy requirements, the framework evaluates them against the synthetic context's **User**. If authorization fails, the action returns 401/403 and ZeroMcp turns that into an MCP error result.

So: **auth runs on the MCP request first**; that identity is **propagated** into the synthetic request used for dispatch, and **authorization on the action/endpoint** is enforced as usual.

---

## Header forwarding (ForwardHeaders)

- **ForwardHeaders** (default includes **Authorization**) specifies which request headers are copied from the MCP HTTP request to the synthetic request used for tool dispatch.
- This allows the underlying action to see the same **Authorization** header (e.g. Bearer token) that the client sent to `/mcp`. If your action or downstream code validates the token again, it will see the same token.
- Set to an empty list or null to disable forwarding.

---

## Role- and policy-based tool visibility

- **RequiredRoles** on `[Mcp]` or `.AsMcp(..., roles: ...)` — Tool appears in **tools/list** only if the current user is in at least one of the given roles.
- **RequiredPolicy** — Tool appears only if **IAuthorizationService.AuthorizeAsync(context.User, null, policy)** succeeds.
- **ToolVisibilityFilter** — Optional extra per-request filter; return **false** to hide the tool from **tools/list** even if roles/policy passed.

Tools that are **hidden** from **tools/list** are not callable by name: a direct **tools/call** for that name returns an "unknown tool" error.

---

## Securing the MCP endpoint

- **Option 1 — Middleware:** Place **UseAuthentication** and **UseAuthorization** before **MapZeroMcp**. Then use **RequireAuthorization** on the mapped endpoint so only authenticated (and optionally authorized) callers can hit GET/POST `/mcp`:

  ```csharp
  app.MapZeroMcp().RequireAuthorization("McpPolicy");
  ```

  (The convention builder returned by **MapZeroMcp** applies to the main MCP route; the inspector route, when enabled, is registered separately and does not automatically get this. You can disable the inspector in production or add custom logic to protect it.)

- **Option 2 — Reverse proxy / API gateway:** Put the MCP endpoint behind a gateway that enforces auth and rate limiting.

---

## Tool inspector endpoint

- **GET /mcp/tools** returns all **registered** tools (subject only to **ToolFilter** at discovery time). It does **not** apply per-request visibility (no **User** or **ToolVisibilityFilter**). So it can expose tool names and schemas to anyone who can reach the URL.
- In production, either set **EnableToolInspector** to **false** or protect the inspector route (e.g. require auth or restrict by IP). See [Enterprise Usage](Enterprise-Usage).

---

## Known attack surfaces and mitigations

| Surface | Mitigation |
|--------|------------|
| Unauthenticated access to MCP | Use **RequireAuthorization** on the MCP endpoint or enforce auth in front (gateway/middleware). |
| Unauthenticated access to inspector | Disable **EnableToolInspector** or protect **GET /mcp/tools**. |
| Tool list reveals internal tools | Use **ToolFilter** and **ToolVisibilityFilter**; use **Roles** / **Policy** so only allowed users see sensitive tools. |
| Invocation without auth | Identity is propagated to the synthetic request; **`[Authorize]`** on the action still runs. Ensure auth middleware runs before **MapZeroMcp**. |
| Header injection | Only headers listed in **ForwardHeaders** are copied; do not forward untrusted headers that could override security-related headers. |

---

## See also

- [Governance and Security](Governance-and-Security) — Roles, policy, filters
- [Enterprise Usage](Enterprise-Usage) — Deployment checklist
- [Connecting MCP Clients](Connecting-Clients) — Production auth
- [Dispatch and Pipeline](Dispatch-and-Pipeline) — How dispatch and auth propagation work
