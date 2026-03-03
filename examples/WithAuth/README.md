# WithAuth Example

ZeroMCP with authentication and authorization: API-key auth, role-based tool visibility, and `[Authorize]` on actions.

## What this demonstrates

- **AddAuthentication** / **AddAuthorization** — API-key handler (`X-Api-Key`: `dev-key` or `admin-key`); `admin-key` adds the Admin role
- **UseAuthentication** / **UseAuthorization** before **MapControllers** and **MapZeroMcp**
- **`[Authorize]`** on an action — tool call returns 401 when unauthenticated
- **`[Mcp(..., Roles = ["Admin"])]`** — tool appears in `tools/list` only when the user is in the Admin role
- **`.AsMcp(..., roles: new[] { "Admin" })`** — same for minimal APIs

Without `X-Api-Key`, `tools/list` excludes `admin_health` and `get_admin_info`. With `X-Api-Key: admin-key`, they are included. Calling `get_secure_info` without a key returns an MCP error (401).

## Run

```bash
dotnet run
```

Try:

- `POST /mcp` with `tools/list` (no header) — no admin tools
- `POST /mcp` with `tools/list` and header `X-Api-Key: admin-key` — admin tools included
- `POST /mcp` with `tools/call` for `get_secure_info` without key — error

## Next steps

- See **WithEnrichment** for result enrichment and streaming
- See **Enterprise** for full observability and governance
