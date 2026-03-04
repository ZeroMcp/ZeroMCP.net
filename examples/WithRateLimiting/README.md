# WithRateLimiting Example

Shows how to apply **ASP.NET Core rate limiting** to the MCP endpoint (Option A: no built-in ZeroMCP rate limiter; use framework middleware).

## What this demonstrates

- **AddRateLimiter** — Fixed-window policy (e.g. 10 requests per 10 seconds) for the MCP route
- **UseRateLimiter** — Enable rate limiting middleware (after **UseRouting**)
- **MapZeroMcp().RequireRateLimiting("McpPolicy")** — Apply the policy only to GET/POST `/mcp`; other routes (e.g. `/api/health`, `/mcp/tools`, `/mcp/ui`) are not limited by this policy
- **OnRejected** — Return HTTP 429 and a JSON-RPC–style error body when the limit is exceeded so MCP clients receive a consistent response

Per-tool or per-user partitioning (e.g. by `HttpContext.User` or a header) is done by configuring a custom partitioner in **AddRateLimiter**; see the wiki [Configuration](../../wiki/Configuration.md) and [Enterprise Usage](../../wiki/Enterprise-Usage.md).

## Run

```bash
dotnet run
```

- **GET /mcp**, **POST /mcp** — Rate limited (10 requests per 10 seconds). Exceeding the limit returns 429 and `{"jsonrpc":"2.0","error":{"code":-32029,"message":"Rate limit exceeded. Try again later."}}`.
- **GET /mcp/tools**, **GET /mcp/ui** — Not covered by this example’s policy; add a separate policy or auth if you need to protect them.

## Next steps

- See **WithAuth** for authentication and role-based tool visibility
- See **Enterprise** for a full production-style setup
