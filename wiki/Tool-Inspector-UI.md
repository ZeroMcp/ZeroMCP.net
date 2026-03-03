# Tool Inspector UI

The **Tool Inspector UI** is a built-in, Swagger-like web page that lets you browse MCP tools, view their input schemas, and invoke them from the browser. It is intended for development and debugging.

---

## URL and when it’s available

- **URL:** **GET {RoutePrefix}/ui** (e.g. **GET /mcp/ui** when `RoutePrefix` is `"/mcp"`).
- The UI is registered only when both are true:
  - **EnableToolInspector** is `true` (default) — enables the inspector (including the JSON tool list).
  - **EnableToolInspectorUI** is `true` (default) — enables this HTML UI in addition to the JSON endpoint.

Set either option to `false` in [Configuration](Configuration) to disable the JSON list or the UI (e.g. in production if the tool list is sensitive). The sample app enables both only when `builder.Environment.IsDevelopment()` is true.

---

## What you can do

1. **Browse tools** — The page loads the full tool list from **GET {RoutePrefix}/tools** (same data as the “JSON (tools)” link on the page).
2. **Grouping by category** — Tools are grouped by **category** when set on `[Mcp]` or `.AsMcp(...)`. Tools without a category appear under “(Uncategorized)”. Category sections are sorted alphabetically.
3. **View details** — Expand a tool to see its description and **input schema** (JSON Schema for route, query, and body parameters).
4. **Try it out** — Edit the JSON arguments in the text area and click **Invoke**. The page sends a **tools/call** request to the MCP endpoint (POST {RoutePrefix}) and shows the JSON-RPC response (success or error).

The UI does not implement MCP session state (e.g. **initialize**); it only calls **tools/call** with the arguments you provide. It is a thin test client over your MCP endpoint.

---

## Authentication and roles

- **Same HTTP context** — Invocations from the UI use the same HTTP context as the browser: cookies, **Authorization** header, and any other headers your app sends with requests to the same origin. There is no separate “API key” field in the UI.
- **Role- and policy-restricted tools** — **tools/call** enforces the same visibility as **tools/list**. If a tool has **Roles** or **Policy**, the server checks the current user before invoking. So:
  - If you open the UI **without** logging in (or without the required role), invoking a role-restricted tool returns an error (e.g. “Tool 'x' is not available (roles or policy not satisfied).”).
  - To test role-restricted tools from the UI, open the UI **while authenticated** with the required role (e.g. log in first, or use a browser profile that sends the right API key or cookie).

See [Governance and Security](Governance-and-Security) and [Security Model](Security-Model) for how roles, policy, and **ToolVisibilityFilter** work.

---

## JSON tool list

The top-right link **“JSON (tools)”** goes to **GET {RoutePrefix}/tools**, which returns the full tool list as JSON (server name, version, protocol version, and a `tools` array with name, description, httpMethod, route, inputSchema, category, tags, examples, hints, requiredRoles, requiredPolicy). The UI uses this endpoint to render the page; you can use it for scripting or tooling.

Note: **GET /mcp/tools** does **not** apply per-request visibility (roles/policy are not used to filter that list). It shows all registered tools. So in the UI you may see tools you cannot invoke if you are not in the right role. Invoking them will correctly return an error.

---

## Production

- **Disable when not needed** — If the tool list or test UI is sensitive, set **EnableToolInspector** or **EnableToolInspectorUI** to `false` (e.g. only in non-Development). See [Enterprise Usage](Enterprise-Usage) and [Configuration](Configuration).
- **Protecting the route** — The inspector and UI routes are registered by **MapZeroMcp** when the options are true. To require authentication on them, use middleware or a custom setup that applies to the inspector path; see [Security Model](Security-Model) and [Enterprise Usage](Enterprise-Usage).

---

## See also

- [Configuration](Configuration) — EnableToolInspector, EnableToolInspectorUI, RoutePrefix, “Tool Inspector and UI” section
- [Governance and Security](Governance-and-Security) — Roles, policy, and how they apply to tools/call and the UI
- [Security Model](Security-Model) — Auth flow, tool inspector endpoint, attack surfaces
- [Enterprise Usage](Enterprise-Usage) — Tool inspector in production, rate limiting
