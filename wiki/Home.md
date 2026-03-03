# ZeroMcp Wiki

**ZeroMcp** exposes your existing ASP.NET Core API as an [MCP](https://modelcontextprotocol.io/) (Model Context Protocol) server with a single attribute and two lines of setup. No separate process. No code duplication.

---

## How it works

Tag controller actions with **`[Mcp]`** or minimal APIs with **`.AsMcp(...)`**. ZeroMcp will:

1. **Discover** tools at startup from controller API descriptions (same source as Swagger) and from minimal API endpoints that use `AsMcp`
2. **Generate** a JSON Schema for each tool's inputs (route, query, and body merged)
3. **Expose** a single endpoint (GET and POST `/mcp`) that speaks the MCP Streamable HTTP transport
4. **Dispatch** tool calls in-process through your real action or endpoint pipeline — filters, validation, and authorization run normally

```
MCP Client (Claude Desktop, Claude.ai, etc.)
    │
    │  GET /mcp (info)  or  POST /mcp (JSON-RPC 2.0)
    ▼
ZeroMcp Endpoint
    │
    │  in-process dispatch (controller or minimal endpoint)
    ▼
Your Action / Endpoint  ← [Mcp] or .AsMcp(...)
    │
    │  real response
    ▼
MCP Client gets structured result
```

---

## Wiki pages

| Page | Description |
|------|-------------|
| [Quick Start](Quick-Start) | Install, register, map endpoint, tag actions |
| [Configuration](Configuration) | Options, route prefix, tool filters, observability, Phase 2 enrichment/streaming |
| [The [Mcp] Attribute](The-Mcp-Attribute) | Attribute usage, name, description, tags, category, examples, hints, roles, policy |
| [Parameters and Schemas](Parameters-and-Schemas) | How route/query/body map to MCP input schema |
| [Controllers and Minimal APIs](Controllers-and-Minimal-APIs) | Using both together, minimal API `.AsMcp` |
| [Governance and Security](Governance-and-Security) | Roles, policy, per-request visibility, auth |
| [Observability](Observability) | Logging, correlation ID, metrics, OpenTelemetry |
| [Dispatch and Pipeline](Dispatch-and-Pipeline) | In-process dispatch, result enrichment, chunked responses |
| [Connecting MCP Clients](Connecting-Clients) | Claude Desktop, Claude.ai, production auth |
| [Versioning](Versioning) | SemVer, breaking-change policy, MCP protocol version |
| [Project Structure](Project-Structure) | Repo layout, build, test commands |
| [Limitations](Limitations) | Known limitations and workarounds |
| [Contributing](Contributing) | How to contribute |

---

## Repository docs

- **README.md** (repo root) — Full documentation; same content expanded in this wiki.
- **VERSIONING.md** — Versioning and breaking-change policy (summary in [Versioning](Versioning)).
- **ZeroMcp/README.md** — NuGet package README (shipped inside the package).
- **progress.md** — Change log for development sessions.
