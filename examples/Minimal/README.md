# Minimal Example

Bare-minimum ZeroMCP setup: one controller action and one minimal API exposed as MCP tools.

## What this demonstrates

- **AddControllers** + **AddEndpointsApiExplorer** + **AddZeroMcp** — required for controller tool discovery
- **MapControllers** then **MapZeroMcp** — order matters; map MCP after your API endpoints
- **`[Mcp("get_weather")]`** on a controller action — exposes the action as the MCP tool `get_weather`
- **`.AsMcp("health_check", "Returns API health status.")`** on a minimal API — exposes the endpoint as the MCP tool `health_check`

No authentication, no enrichment, no governance. Use this as a starting point for adding ZeroMCP to an existing API.

## Run

```bash
dotnet run
```

- **GET /mcp** — MCP endpoint info
- **POST /mcp** — JSON-RPC (e.g. `initialize`, `tools/list`, `tools/call`)
- **GET /mcp/tools** — Tool inspector (if enabled)

## Next steps

- See **WithAuth** for authentication and role-based tool visibility
- See **WithEnrichment** for result enrichment and streaming options
- See **Enterprise** for a full-featured setup
