# Enterprise Usage

Checklist and guidance for deploying ZeroMcp in production and enterprise environments.

---

## Deployment checklist

- **HTTPS** — Serve the MCP endpoint over HTTPS. Use standard ASP.NET Core HTTPS configuration and (if applicable) reverse-proxy TLS termination.
- **Authentication** — Secure the `/mcp` (and optionally `/mcp/tools`) endpoint. Use **AddAuthentication** and **AddAuthorization**, then either middleware or endpoint-level auth (e.g. `app.MapZeroMcp().RequireAuthorization("McpPolicy")`). See [Connecting MCP Clients](Connecting-Clients) and [Security Model](Security-Model).
- **CORS** — If MCP clients call your API from a different origin, configure CORS as needed for your environment.
- **Rate limiting** — Apply rate limiting (e.g. ASP.NET Core rate limiting middleware or a reverse proxy) to protect the MCP and tool-inspector endpoints. Per-tool or per-user limits are application-specific (Phase 4 may add built-in options).
- **Correlation IDs** — Keep **CorrelationIdHeader** (default `X-Correlation-ID`) enabled so you can trace MCP requests and tool calls in logs and APM.
- **Health monitoring** — Use **GET /mcp** for a simple liveness check. Use **GET /mcp/tools** (when **EnableToolInspector** is true) to verify tool discovery; you can gate it with auth or disable it in production if desired.

---

## Recommended options for production

```csharp
builder.Services.AddZeroMcp(options =>
{
    options.ServerName = "My API";
    options.ServerVersion = "1.0.0";
    options.RoutePrefix = "/mcp";
    options.CorrelationIdHeader = "X-Correlation-ID";
    options.ForwardHeaders = ["Authorization"];  // so tool dispatch sees the same auth
    options.EnableOpenTelemetryEnrichment = true; // if you use OpenTelemetry
    options.EnableToolInspector = true;           // set to false to hide GET /mcp/tools in prod
    options.ToolFilter = name => ...;             // optional: hide internal tools by name
    options.ToolVisibilityFilter = (name, ctx) => ...; // optional: per-request visibility
});
```

---

## Tool inspector in production

- **GET /mcp/tools** returns the full registered tool list (no per-request visibility filtering). If that metadata is sensitive, either set **EnableToolInspector** to `false` or protect the route:

  ```csharp
  endpoints.MapGet("/mcp/tools", ...).RequireAuthorization();
  ```

  (You would need to register the inspector route yourself in that case, or use a wrapper that applies auth to the convention builder.)

- The built-in **MapZeroMcp** registers the inspector when **EnableToolInspector** is true; the returned convention builder is for the main MCP endpoint. To require auth on the inspector, consider a custom extension or middleware that applies to the `/mcp/tools` path.

---

## Distributed tracing

- Set **CorrelationIdHeader** and (optionally) **EnableOpenTelemetryEnrichment** so each tool invocation is tagged with `mcp.tool`, `mcp.status_code`, `mcp.duration_ms`, and `mcp.correlation_id`.
- Forward the same correlation ID from your MCP gateway or client so logs and traces can be correlated end-to-end.

---

## See also

- [Governance and Security](Governance-and-Security) — Roles, policy, ToolFilter, ToolVisibilityFilter
- [Security Model](Security-Model) — Auth flow, synthetic dispatch, attack surfaces
- [Configuration](Configuration) — All options
- [Observability](Observability) — Logging and metrics
