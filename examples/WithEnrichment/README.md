# WithEnrichment Example

ZeroMCP with Phase 2 result enrichment, suggested follow-ups, and chunked streaming.

## What this demonstrates

- **EnableResultEnrichment** — `tools/call` results include `metadata` (statusCode, contentType, correlationId, durationMs) and optional `hints`
- **EnableSuggestedFollowUps** + **SuggestedFollowUpsProvider** — results can include `suggestedNextActions` (toolName + rationale) for AI clients
- **EnableStreamingToolResults** + **StreamingChunkSize** — long response content is split into chunks with `chunkIndex` and `isFinal`
- **ResponseHintProvider** — custom hints (e.g. "Consider retrying") on error or non-2xx
- **`[Mcp(..., Category, Examples, Hints)]`** — AI-native metadata on tools for better LLM reasoning

Call `get_catalog` or `get_item` and inspect the JSON-RPC result: you should see `metadata`, optional `suggestedNextActions`, and `hints` when enabled.

## Run

```bash
dotnet run
```

- **POST /mcp** — `tools/call` for `get_catalog` or `get_item`; response shape includes enrichment fields
- **GET /mcp/tools** — inspector shows category, examples, hints per tool

## Next steps

- See **Enterprise** for auth + enrichment + observability + governance in one app
