# Enterprise Example

Full-featured ZeroMCP setup: authentication, authorization, result enrichment, streaming, observability, and governance.

## What this demonstrates

- **Auth** — API-key auth (`X-Api-Key`: `dev-key` or `admin-key`); Admin role for admin-key
- **Governance** — `ToolFilter` (exclude tools whose name starts with `internal_`), `ToolVisibilityFilter` (admin_* only when user is Admin)
- **Observability** — `CorrelationIdHeader`, `EnableOpenTelemetryEnrichment`
- **Phase 2** — `EnableResultEnrichment`, `EnableSuggestedFollowUps`, `EnableStreamingToolResults`, hint and suggested-action providers
- **Controllers** — CRUD with `[Authorize]` and `[Mcp(..., Roles = ["Admin"])]` where appropriate

Use this as a reference for production-style configuration. See the main wiki for enterprise deployment checklists and security guidance.

## Run

```bash
dotnet run
```

- **GET /mcp/tools** — inspector shows all registered tools (respects ToolFilter; no per-request visibility on inspector)
- **POST /mcp** — `tools/list` filtered by user; `tools/call` returns enriched results with correlation ID in metadata

## Enterprise checklist

- Use HTTPS and secure the `/mcp` (and optionally `/mcp/tools`) endpoint in production
- Configure rate limiting and CORS as needed
- Wire `IMcpMetricsSink` to your metrics backend
- Consider `.RequireAuthorization()` on the inspector endpoint if it exposes sensitive metadata
